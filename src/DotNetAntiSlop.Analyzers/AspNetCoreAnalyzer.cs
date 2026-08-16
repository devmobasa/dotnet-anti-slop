using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotNetAntiSlop.Analyzers;

/// <summary>Analyzes ASP.NET Core and dependency-injection usage.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AspNetCoreAnalyzer : DiagnosticAnalyzer
{
    private sealed class OptionsBuilderIdentity
    {
        internal OptionsBuilderIdentity(ISymbol builderSymbol)
        {
            BuilderSymbol = builderSymbol;
        }

        internal OptionsBuilderIdentity(
            ExpressionSyntax serviceExpression,
            ITypeSymbol optionsType,
            ExpressionSyntax? optionNameExpression,
            object? constantOptionName)
        {
            ServiceExpression = serviceExpression;
            OptionsType = optionsType;
            OptionNameExpression = optionNameExpression;
            ConstantOptionName = constantOptionName;
        }

        internal ISymbol? BuilderSymbol { get; }

        internal ExpressionSyntax? ServiceExpression { get; }

        internal ITypeSymbol? OptionsType { get; }

        internal ExpressionSyntax? OptionNameExpression { get; }

        internal object? ConstantOptionName { get; }
    }

    private sealed class SingletonRegistration
    {
        internal SingletonRegistration(
            INamedTypeSymbol implementationType,
            Location location)
        {
            ImplementationType = implementationType;
            Location = location;
        }

        internal INamedTypeSymbol ImplementationType { get; }

        internal Location Location { get; }
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            RuleDescriptors.DAS2001,
            RuleDescriptors.DAS2002,
            RuleDescriptors.DAS2003,
            RuleDescriptors.DAS2004,
            RuleDescriptors.DAS2005,
            RuleDescriptors.DAS2006,
            RuleDescriptors.DAS2007,
            RuleDescriptors.DAS2008,
            RuleDescriptors.DAS2009);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(startContext =>
        {
            var scopedTypes = new ConcurrentDictionary<INamedTypeSymbol, byte>(
                SymbolEqualityComparer.Default);
            var singletonRegistrations = new ConcurrentBag<SingletonRegistration>();

            startContext.RegisterSyntaxNodeAction(
                syntaxContext => AnalyzeInvocation(
                    syntaxContext,
                    scopedTypes,
                    singletonRegistrations),
                SyntaxKind.InvocationExpression);
            startContext.RegisterSyntaxNodeAction(
                AnalyzeMethod,
                SyntaxKind.MethodDeclaration);
            startContext.RegisterSyntaxNodeAction(
                AnalyzeLambda,
                SyntaxKind.ParenthesizedLambdaExpression,
                SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.AnonymousMethodExpression);
            startContext.RegisterSyntaxNodeAction(
                AnalyzeObjectCreation,
                SyntaxKind.ObjectCreationExpression);
            startContext.RegisterSyntaxNodeAction(
                AnalyzeHttpContextCapture,
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxKind.EqualsValueClause);

            startContext.RegisterCompilationEndAction(endContext =>
            {
                foreach (var registration in singletonRegistrations)
                {
                    if (CapturesScopedDependency(
                        registration.ImplementationType,
                        scopedTypes.Keys))
                    {
                        endContext.ReportDiagnostic(Diagnostic.Create(
                            RuleDescriptors.DAS2003,
                            registration.Location));
                    }
                }
            });
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, byte> scopedTypes,
        ConcurrentBag<SingletonRegistration> singletonRegistrations)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var method = AnalyzerUtilities.GetInvokedMethod(
            context.SemanticModel,
            invocation,
            context.CancellationToken);
        if (method == null)
        {
            return;
        }

        AnalyzeNestedProvider(context, invocation, method);
        CollectLifetimes(
            context,
            invocation,
            method,
            scopedTypes,
            singletonRegistrations);
        AnalyzeDevelopmentMiddleware(context, invocation, method);
        AnalyzeFireAndForget(context, invocation, method);
        AnalyzeBoundOptionsValidation(context, invocation, method);
    }

    private static void AnalyzeBoundOptionsValidation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        var candidate = method.ReducedFrom ?? method;
        if ((candidate.Name != "Bind" && candidate.Name != "BindConfiguration") ||
            candidate.ContainingType.Name != "OptionsBuilderConfigurationExtensions" ||
            !AnalyzerUtilities.IsNamespaceOrChild(
                candidate,
                "Microsoft.Extensions.DependencyInjection") ||
            !AnalyzerUtilities.IsType(
                method.ReturnType,
                "Microsoft.Extensions.Options",
                "OptionsBuilder"))
        {
            return;
        }

        if (ContainsOptionsValidation(context, invocation) ||
            IsPrevalidatedBuilder(context, invocation) ||
            IsValidatedLater(context, invocation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS2009,
            invocation.GetLocation()));
    }

    private static bool ContainsOptionsValidation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax binding)
    {
        return binding.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => IsOptionsMethod(
                context,
                invocation,
                "ValidateOnStart") &&
                ReceiverChainContains(invocation, binding));
    }

    private static bool IsPrevalidatedBuilder(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax binding)
    {
        if (StartsWithPrevalidatedOptions(context, binding))
        {
            return true;
        }

        var receiver = AnalyzerUtilities.GetInvocationReceiver(binding);
        var receiverSymbol = receiver == null
            ? null
            : context.SemanticModel.GetSymbolInfo(
                receiver,
                context.CancellationToken).Symbol as ILocalSymbol;
        if (receiverSymbol == null ||
            receiverSymbol.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        var declaration = receiverSymbol.DeclaringSyntaxReferences[0]
            .GetSyntax(context.CancellationToken) as VariableDeclaratorSyntax;
        var initializer = declaration?.Initializer?.Value;
        if (initializer == null ||
            HasAssignmentBeforeBinding(receiverSymbol, declaration!, binding, context))
        {
            return false;
        }

        return StartsWithPrevalidatedOptions(context, initializer);
    }

    private static bool StartsWithPrevalidatedOptions(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression)
    {
        ExpressionSyntax? current = expression;
        while (current != null)
        {
            if (current is ParenthesizedExpressionSyntax parenthesized)
            {
                current = parenthesized.Expression;
                continue;
            }

            if (current is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            if (IsOptionsMethod(
                context,
                invocation,
                "AddOptionsWithValidateOnStart"))
            {
                return true;
            }

            current = AnalyzerUtilities.GetInvocationReceiver(invocation);
        }

        return false;
    }

    private static bool ReceiverChainContains(
        InvocationExpressionSyntax outerInvocation,
        InvocationExpressionSyntax expectedReceiver)
    {
        ExpressionSyntax? current = AnalyzerUtilities.GetInvocationReceiver(outerInvocation);
        while (current != null)
        {
            if (current == expectedReceiver)
            {
                return true;
            }

            if (current is ParenthesizedExpressionSyntax parenthesized)
            {
                current = parenthesized.Expression;
                continue;
            }

            current = current is InvocationExpressionSyntax invocation
                ? AnalyzerUtilities.GetInvocationReceiver(invocation)
                : null;
        }

        return false;
    }

    private static bool HasAssignmentBeforeBinding(
        ILocalSymbol local,
        VariableDeclaratorSyntax declaration,
        InvocationExpressionSyntax binding,
        SyntaxNodeAnalysisContext context)
    {
        var block = binding.FirstAncestorOrSelf<BlockSyntax>();
        if (block == null)
        {
            return true;
        }

        return block.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => IsExecutedBetween(
                assignment,
                block,
                declaration.Span.End,
                binding.SpanStart,
                context))
            .Any(assignment => SymbolEqualityComparer.Default.Equals(
                local,
                context.SemanticModel.GetSymbolInfo(
                    assignment.Left,
                    context.CancellationToken).Symbol));
    }

    private static bool IsValidatedLater(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax binding)
    {
        var bindingIdentity = GetBindingIdentity(context, binding);
        var block = binding.FirstAncestorOrSelf<BlockSyntax>();
        var bindingStatement = binding.FirstAncestorOrSelf<StatementSyntax>();
        if (bindingIdentity == null ||
            block == null ||
            bindingStatement?.Parent != block)
        {
            return false;
        }

        var bindingIndex = block.Statements.IndexOf(bindingStatement);
        for (var index = bindingIndex + 1; index < block.Statements.Count; index++)
        {
            var statement = block.Statements[index];
            var validationInvocations = GetExecutedExpressions(statement)
                .SelectMany(expression => expression.DescendantNodesAndSelf())
                .OfType<InvocationExpressionSyntax>()
                .Where(candidate =>
                    IsOptionsMethod(context, candidate, "ValidateOnStart") &&
                    IsUnconditionalWithin(candidate, statement))
                .ToArray();
            if (validationInvocations.Length == 0)
            {
                if (!IsStraightLineStatement(statement))
                {
                    return false;
                }

                continue;
            }

            foreach (var validation in validationInvocations)
            {
                var validationIdentity = GetReceiverIdentity(context, validation);
                if (AreSameBuilder(
                    context,
                    bindingIdentity,
                    validationIdentity,
                    binding.Span.End,
                    validation.SpanStart,
                    block))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsStraightLineStatement(StatementSyntax statement) =>
        statement is ExpressionStatementSyntax or
            LocalDeclarationStatementSyntax or
            EmptyStatementSyntax;

    private static IEnumerable<ExpressionSyntax> GetExecutedExpressions(
        StatementSyntax statement)
    {
        if (statement is ExpressionStatementSyntax expressionStatement)
        {
            yield return expressionStatement.Expression;
            yield break;
        }

        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (variable.Initializer?.Value is { } initializer)
                {
                    yield return initializer;
                }
            }
        }
    }

    private static bool IsUnconditionalWithin(
        InvocationExpressionSyntax invocation,
        StatementSyntax statement)
    {
        return !invocation.Ancestors()
            .TakeWhile(ancestor => ancestor != statement)
            .Any(ancestor =>
                ancestor is AnonymousFunctionExpressionSyntax or
                    ConditionalExpressionSyntax or
                    ConditionalAccessExpressionSyntax);
    }

    private static OptionsBuilderIdentity? GetBindingIdentity(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax binding)
    {
        var variable = binding.Ancestors()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(declarator => declarator.Initializer?.Value.Span.Contains(binding.Span) == true);
        if (variable != null)
        {
            var symbol = context.SemanticModel.GetDeclaredSymbol(
                variable,
                context.CancellationToken);
            return symbol == null ? null : new OptionsBuilderIdentity(symbol);
        }

        var assignment = binding.Ancestors()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(candidate => candidate.Right.Span.Contains(binding.Span));
        if (assignment != null)
        {
            var symbol = context.SemanticModel.GetSymbolInfo(
                assignment.Left,
                context.CancellationToken).Symbol;
            return symbol == null ? null : new OptionsBuilderIdentity(symbol);
        }

        return GetReceiverIdentity(context, binding);
    }

    private static OptionsBuilderIdentity? GetReceiverIdentity(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var receiver = AnalyzerUtilities.GetInvocationReceiver(invocation);
        if (receiver == null)
        {
            return null;
        }

        var receiverSymbol = context.SemanticModel.GetSymbolInfo(
            receiver,
            context.CancellationToken).Symbol;
        if (receiverSymbol is ILocalSymbol or IFieldSymbol or IParameterSymbol)
        {
            return new OptionsBuilderIdentity(receiverSymbol);
        }

        var addOptions = receiver as InvocationExpressionSyntax;
        var method = addOptions == null
            ? null
            : AnalyzerUtilities.GetInvokedMethod(
                context.SemanticModel,
                addOptions,
                context.CancellationToken);
        var candidate = method?.ReducedFrom ?? method;
        if (addOptions == null ||
            candidate == null ||
            candidate.Name != "AddOptions" ||
            candidate.ContainingType.Name != "OptionsServiceCollectionExtensions" ||
            !AnalyzerUtilities.IsNamespaceOrChild(
                candidate,
                "Microsoft.Extensions.DependencyInjection") ||
            method!.TypeArguments.Length != 1)
        {
            return null;
        }

        var serviceReceiver = AnalyzerUtilities.GetInvocationReceiver(addOptions);
        var optionName = GetOptionNameIdentity(context, addOptions);
        return serviceReceiver == null || optionName == null
            ? null
            : new OptionsBuilderIdentity(
                serviceReceiver,
                method.TypeArguments[0],
                optionName.Value.Expression,
                optionName.Value.Constant);
    }

    private static (ExpressionSyntax? Expression, object? Constant)? GetOptionNameIdentity(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax addOptions)
    {
        var operation = context.SemanticModel.GetOperation(
            addOptions,
            context.CancellationToken) as IInvocationOperation;
        var nameArgument = operation?.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name == "name");
        if (nameArgument == null)
        {
            return (null, string.Empty);
        }

        if (nameArgument.Value.ConstantValue is { HasValue: true, Value: string name })
        {
            return (null, name);
        }

        return nameArgument.Value.Syntax is ExpressionSyntax expression
            ? (expression, null)
            : null;
    }

    private static bool AreSameBuilder(
        SyntaxNodeAnalysisContext context,
        OptionsBuilderIdentity expected,
        OptionsBuilderIdentity? actual,
        int start,
        int end,
        BlockSyntax block)
    {
        if (actual == null)
        {
            return false;
        }

        if (expected.BuilderSymbol != null || actual.BuilderSymbol != null)
        {
            return SymbolEqualityComparer.Default.Equals(
                expected.BuilderSymbol,
                actual.BuilderSymbol) &&
                expected.BuilderSymbol != null &&
                !HasImmediateAssignmentBetween(
                    context,
                    expected.BuilderSymbol,
                    start,
                    end,
                    block);
        }

        return SymbolEqualityComparer.Default.Equals(
                   expected.OptionsType,
                   actual.OptionsType) &&
               AreSameRuntimeValue(
                   context,
                   expected.ServiceExpression,
                   actual.ServiceExpression,
                   start,
                   end,
                   block) &&
               AreSameOptionName(
                   context,
                   expected,
                   actual,
                   start,
                   end,
                   block);
    }

    private static bool AreSameOptionName(
        SyntaxNodeAnalysisContext context,
        OptionsBuilderIdentity expected,
        OptionsBuilderIdentity actual,
        int start,
        int end,
        BlockSyntax block)
    {
        if (expected.OptionNameExpression == null || actual.OptionNameExpression == null)
        {
            return expected.OptionNameExpression == null &&
                   actual.OptionNameExpression == null &&
                   Equals(expected.ConstantOptionName, actual.ConstantOptionName);
        }

        return AreSameRuntimeValue(
            context,
            expected.OptionNameExpression,
            actual.OptionNameExpression,
            start,
            end,
            block);
    }

    private static bool AreSameRuntimeValue(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax? expected,
        ExpressionSyntax? actual,
        int start,
        int end,
        BlockSyntax block)
    {
        if (expected == null || actual == null)
        {
            return false;
        }

        expected = UnwrapExpression(expected);
        actual = UnwrapExpression(actual);

        if (expected is ThisExpressionSyntax or BaseExpressionSyntax ||
            actual is ThisExpressionSyntax or BaseExpressionSyntax)
        {
            return expected.Kind() == actual.Kind();
        }

        if (expected is MemberAccessExpressionSyntax expectedMember &&
            actual is MemberAccessExpressionSyntax actualMember)
        {
            var expectedMemberSymbol = context.SemanticModel.GetSymbolInfo(
                expectedMember.Name,
                context.CancellationToken).Symbol;
            var actualMemberSymbol = context.SemanticModel.GetSymbolInfo(
                actualMember.Name,
                context.CancellationToken).Symbol;
            if (!SymbolEqualityComparer.Default.Equals(
                    expectedMemberSymbol,
                    actualMemberSymbol) ||
                expectedMemberSymbol is not IFieldSymbol and not IPropertySymbol)
            {
                return false;
            }

            return AreSameRuntimeValue(
                       context,
                       expectedMember.Expression,
                       actualMember.Expression,
                       start,
                       end,
                       block) &&
                   !HasRuntimeValueAssignmentBetween(
                       context,
                       expectedMember,
                       start,
                       end,
                       block);
        }

        if (expected is InvocationExpressionSyntax or ObjectCreationExpressionSyntax ||
            actual is InvocationExpressionSyntax or ObjectCreationExpressionSyntax)
        {
            return false;
        }

        var expectedValue = context.SemanticModel.GetConstantValue(
            expected,
            context.CancellationToken);
        var actualValue = context.SemanticModel.GetConstantValue(
            actual,
            context.CancellationToken);
        if (expectedValue.HasValue || actualValue.HasValue)
        {
            return expectedValue.HasValue &&
                   actualValue.HasValue &&
                   Equals(expectedValue.Value, actualValue.Value);
        }

        var expectedSymbol = context.SemanticModel.GetSymbolInfo(
            expected,
            context.CancellationToken).Symbol;
        var actualSymbol = context.SemanticModel.GetSymbolInfo(
            actual,
            context.CancellationToken).Symbol;
        return expectedSymbol != null &&
               expectedSymbol is not IMethodSymbol &&
               SymbolEqualityComparer.Default.Equals(expectedSymbol, actualSymbol) &&
               !HasRuntimeValueAssignmentBetween(
                   context,
                   expected,
                   start,
                   end,
                   block);
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool HasImmediateAssignmentBetween(
        SyntaxNodeAnalysisContext context,
        ISymbol symbol,
        int start,
        int end,
        BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => IsExecutedBetween(
                assignment,
                block,
                start,
                end,
                context))
            .Any(assignment => SymbolEqualityComparer.Default.Equals(
                symbol,
                context.SemanticModel.GetSymbolInfo(
                    assignment.Left,
                    context.CancellationToken).Symbol));
    }

    private static bool HasRuntimeValueAssignmentBetween(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expected,
        int start,
        int end,
        BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment => IsExecutedBetween(
                assignment,
                block,
                start,
                end,
                context))
            .Any(assignment => AreSameAssignmentTarget(
                context,
                expected,
                assignment.Left));
    }

    private static bool AreSameAssignmentTarget(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expected,
        ExpressionSyntax actual)
    {
        expected = UnwrapExpression(expected);
        actual = UnwrapExpression(actual);
        if (expected is ThisExpressionSyntax or BaseExpressionSyntax ||
            actual is ThisExpressionSyntax or BaseExpressionSyntax)
        {
            return expected.Kind() == actual.Kind();
        }

        if (expected is MemberAccessExpressionSyntax expectedMember &&
            actual is MemberAccessExpressionSyntax actualMember)
        {
            var expectedSymbol = context.SemanticModel.GetSymbolInfo(
                expectedMember.Name,
                context.CancellationToken).Symbol;
            var actualSymbol = context.SemanticModel.GetSymbolInfo(
                actualMember.Name,
                context.CancellationToken).Symbol;
            return SymbolEqualityComparer.Default.Equals(expectedSymbol, actualSymbol) &&
                   AreSameAssignmentTarget(
                       context,
                       expectedMember.Expression,
                       actualMember.Expression);
        }

        var expectedLeaf = context.SemanticModel.GetSymbolInfo(
            expected,
            context.CancellationToken).Symbol;
        var actualLeaf = context.SemanticModel.GetSymbolInfo(
            actual,
            context.CancellationToken).Symbol;
        return expectedLeaf != null &&
               SymbolEqualityComparer.Default.Equals(expectedLeaf, actualLeaf);
    }

    private static bool IsExecutedBetween(
        SyntaxNode node,
        BlockSyntax block,
        int start,
        int end,
        SyntaxNodeAnalysisContext context)
    {
        var deferred = node.Ancestors()
            .TakeWhile(ancestor => ancestor != block)
            .FirstOrDefault(ancestor =>
                ancestor is AnonymousFunctionExpressionSyntax or
                    LocalFunctionStatementSyntax);
        if (deferred == null)
        {
            return node.SpanStart >= start && node.SpanStart < end;
        }

        ISymbol? callable = deferred switch
        {
            LocalFunctionStatementSyntax localFunction =>
                context.SemanticModel.GetDeclaredSymbol(
                    localFunction,
                    context.CancellationToken),
            AnonymousFunctionExpressionSyntax anonymousFunction =>
                GetAssignedDelegateSymbol(context, anonymousFunction),
            _ => null
        };
        return callable != null && block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation =>
                invocation.SpanStart >= start &&
                invocation.SpanStart < end &&
                !invocation.Ancestors()
                    .TakeWhile(ancestor => ancestor != block)
                    .Any(ancestor =>
                        ancestor is AnonymousFunctionExpressionSyntax or
                            LocalFunctionStatementSyntax))
            .Any(invocation => SymbolEqualityComparer.Default.Equals(
                callable,
                GetInvokedCallableSymbol(context, invocation)));
    }

    private static ISymbol? GetAssignedDelegateSymbol(
        SyntaxNodeAnalysisContext context,
        AnonymousFunctionExpressionSyntax anonymousFunction)
    {
        var declarator = anonymousFunction.Ancestors()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(variable =>
                variable.Initializer?.Value.Span.Contains(anonymousFunction.Span) == true);
        return declarator == null
            ? null
            : context.SemanticModel.GetDeclaredSymbol(
                declarator,
                context.CancellationToken);
    }

    private static ISymbol? GetInvokedCallableSymbol(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var expression = UnwrapExpression(invocation.Expression);
        var symbol = context.SemanticModel.GetSymbolInfo(
            expression,
            context.CancellationToken).Symbol;
        if (symbol is ILocalSymbol)
        {
            return symbol;
        }

        return AnalyzerUtilities.GetInvokedMethod(
            context.SemanticModel,
            invocation,
            context.CancellationToken);
    }

    private static bool IsOptionsMethod(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string methodName)
    {
        var method = AnalyzerUtilities.GetInvokedMethod(
            context.SemanticModel,
            invocation,
            context.CancellationToken);
        var candidate = method?.ReducedFrom ?? method;
        var containingTypeName = methodName == "AddOptionsWithValidateOnStart"
            ? "OptionsServiceCollectionExtensions"
            : "OptionsBuilderExtensions";
        return candidate?.Name == methodName &&
               candidate.ContainingType.Name == containingTypeName &&
               AnalyzerUtilities.IsNamespaceOrChild(
                   candidate,
                   "Microsoft.Extensions.DependencyInjection");
    }

    private static void AnalyzeNestedProvider(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        var candidate = method.ReducedFrom ?? method;
        if (candidate.Name == "BuildServiceProvider" &&
            AnalyzerUtilities.IsNamespaceOrChild(
                candidate,
                "Microsoft.Extensions.DependencyInjection"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS2001,
                invocation.GetLocation()));
        }
    }

    private static void CollectLifetimes(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ConcurrentDictionary<INamedTypeSymbol, byte> scopedTypes,
        ConcurrentBag<SingletonRegistration> singletonRegistrations)
    {
        var candidate = method.ReducedFrom ?? method;
        var isDependencyInjection =
            AnalyzerUtilities.IsNamespaceOrChild(
                candidate,
                "Microsoft.Extensions.DependencyInjection") ||
            AnalyzerUtilities.IsNamespaceOrChild(
                candidate,
                "Microsoft.EntityFrameworkCore");

        if (!isDependencyInjection)
        {
            return;
        }

        if (candidate.Name == "AddScoped" ||
            candidate.Name == "TryAddScoped" ||
            candidate.Name == "AddDbContext" ||
            candidate.Name == "AddDbContextPool")
        {
            foreach (var type in GetRegisteredTypes(
                context,
                invocation,
                method))
            {
                scopedTypes.TryAdd(type, 0);
            }

            return;
        }

        if (candidate.Name != "AddSingleton" &&
            candidate.Name != "TryAddSingleton")
        {
            return;
        }

        var registeredTypes = GetRegisteredTypes(
            context,
            invocation,
            method).ToArray();

        if (registeredTypes.Any(type => AnalyzerUtilities.IsDbContext(type)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS2002,
                invocation.GetLocation()));
        }

        var implementation = registeredTypes.LastOrDefault();
        if (implementation != null &&
            implementation.TypeKind != TypeKind.Interface &&
            !implementation.IsAbstract)
        {
            singletonRegistrations.Add(
                new SingletonRegistration(
                    implementation,
                    invocation.GetLocation()));
        }

        foreach (var lambda in invocation.ArgumentList.Arguments
                     .SelectMany(argument => argument.Expression.DescendantNodesAndSelf())
                     .OfType<AnonymousFunctionExpressionSyntax>())
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(
                lambda,
                context.CancellationToken);
            var delegateType = typeInfo.ConvertedType as INamedTypeSymbol;
            var returnType = delegateType?.DelegateInvokeMethod?.ReturnType;
            if (AnalyzerUtilities.IsDbContext(returnType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RuleDescriptors.DAS2002,
                    lambda.GetLocation()));
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetRegisteredTypes(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        foreach (var typeArgument in method.TypeArguments)
        {
            var named = typeArgument as INamedTypeSymbol;
            if (named != null)
            {
                yield return named;
            }
        }

        foreach (var typeOf in invocation.ArgumentList.Arguments
                     .SelectMany(argument => argument.Expression.DescendantNodesAndSelf())
                     .OfType<TypeOfExpressionSyntax>())
        {
            var type = context.SemanticModel.GetTypeInfo(
                typeOf.Type,
                context.CancellationToken).Type as INamedTypeSymbol;
            if (type != null)
            {
                yield return type;
            }
        }
    }

    private static bool CapturesScopedDependency(
        INamedTypeSymbol singletonType,
        IEnumerable<INamedTypeSymbol> scopedTypes)
    {
        var scoped = scopedTypes.ToArray();

        foreach (var constructor in singletonType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public &&
                constructor.DeclaredAccessibility != Accessibility.Internal)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                if (AnalyzerUtilities.IsDbContext(parameter.Type))
                {
                    return true;
                }

                foreach (var scopedType in scoped)
                {
                    if (SymbolEqualityComparer.Default.Equals(
                        parameter.Type.OriginalDefinition,
                        scopedType.OriginalDefinition))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (!AnalyzerUtilities.IsControllerAction(
                method,
                context.SemanticModel,
                context.CancellationToken) ||
            !AnalyzerUtilities.IsAsyncCallable(
                method,
                context.SemanticModel,
                context.CancellationToken) ||
            AnalyzerUtilities.HasCancellationTokenParameter(
                method,
                context.SemanticModel,
                context.CancellationToken) ||
            UsesRequestAborted(method))
        {
            return;
        }

        if (ContainsCancellableWork(
            method,
            context.SemanticModel,
            context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS2004,
                method.Identifier.GetLocation()));
        }
    }

    private static void AnalyzeLambda(SyntaxNodeAnalysisContext context)
    {
        var lambda = (AnonymousFunctionExpressionSyntax)context.Node;
        if (!AnalyzerUtilities.IsEndpointHandler(
                lambda,
                context.SemanticModel,
                context.CancellationToken) ||
            !AnalyzerUtilities.IsAsyncCallable(
                lambda,
                context.SemanticModel,
                context.CancellationToken) ||
            AnalyzerUtilities.HasCancellationTokenParameter(
                lambda,
                context.SemanticModel,
                context.CancellationToken) ||
            UsesRequestAborted(lambda))
        {
            return;
        }

        if (ContainsCancellableWork(
            lambda,
            context.SemanticModel,
            context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS2004,
                lambda.GetLocation()));
        }
    }

    private static bool ContainsCancellableWork(
        SyntaxNode callable,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (callable.DescendantNodes().OfType<AwaitExpressionSyntax>().Any())
        {
            return true;
        }

        foreach (var invocation in callable.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var method = AnalyzerUtilities.GetInvokedMethod(
                semanticModel,
                invocation,
                cancellationToken);
            if (method != null &&
                (method.Name.EndsWith("Async", StringComparison.Ordinal) ||
                 AnalyzerUtilities.HasCancellationTokenParameter(method)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsesRequestAborted(SyntaxNode callable)
    {
        return callable.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(member => member.Name.Identifier.ValueText == "RequestAborted");
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;
        var type = context.SemanticModel.GetTypeInfo(
            creation,
            context.CancellationToken).Type;

        if (!AnalyzerUtilities.IsType(type, "System.Net.Http", "HttpClient") ||
            !AnalyzerUtilities.IsRequestHandler(
                creation,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS2005,
            creation.GetLocation()));
    }

    private static void AnalyzeDevelopmentMiddleware(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (!IsDevelopmentOnlyMethod(method) ||
            AnalyzerUtilities.HasDevelopmentGuard(
                invocation,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS2006,
            invocation.GetLocation()));
    }

    private static bool IsDevelopmentOnlyMethod(IMethodSymbol method)
    {
        var candidate = method.ReducedFrom ?? method;
        if (!AnalyzerUtilities.IsNamespaceOrChild(candidate, "Microsoft.AspNetCore"))
        {
            return false;
        }

        switch (candidate.Name)
        {
            case "UseDeveloperExceptionPage":
            case "UseMigrationsEndPoint":
            case "UseDatabaseErrorPage":
            case "UseSwagger":
            case "UseSwaggerUI":
                return true;
            default:
                return false;
        }
    }

    private static void AnalyzeFireAndForget(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (!AnalyzerUtilities.IsTaskLike(method.ReturnType) ||
            !AnalyzerUtilities.IsRequestHandler(
                invocation,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        if (invocation.Parent is ExpressionStatementSyntax)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS2007,
                invocation.GetLocation()));
            return;
        }

        var assignment = invocation.Parent as AssignmentExpressionSyntax;
        if (assignment != null &&
            assignment.Right == invocation &&
            assignment.Left is IdentifierNameSyntax identifier &&
            identifier.Identifier.ValueText == "_")
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS2007,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeHttpContextCapture(
        SyntaxNodeAnalysisContext context)
    {
        ISymbol? target;
        ExpressionSyntax? value;

        if (context.Node is AssignmentExpressionSyntax assignment)
        {
            target = context.SemanticModel.GetSymbolInfo(
                assignment.Left,
                context.CancellationToken).Symbol;
            value = assignment.Right;
        }
        else if (context.Node is EqualsValueClauseSyntax initializer)
        {
            value = initializer.Value;
            if (initializer.Parent is VariableDeclaratorSyntax variable)
            {
                target = context.SemanticModel.GetDeclaredSymbol(
                    variable,
                    context.CancellationToken);
            }
            else if (initializer.Parent is PropertyDeclarationSyntax property)
            {
                target = context.SemanticModel.GetDeclaredSymbol(
                    property,
                    context.CancellationToken);
            }
            else
            {
                return;
            }
        }
        else
        {
            return;
        }

        if (!(target is IFieldSymbol) && !(target is IPropertySymbol))
        {
            return;
        }

        IOperation? operation = context.SemanticModel.GetOperation(
            value,
            context.CancellationToken);
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        var propertyReference = operation as IPropertyReferenceOperation;
        if (propertyReference == null ||
            !IsHttpContextAccessorProperty(propertyReference.Property))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS2008,
            value.GetLocation()));
    }

    private static bool IsHttpContextAccessorProperty(IPropertySymbol property)
    {
        if (property.Name == "HttpContext" &&
            AnalyzerUtilities.IsOrImplements(
                property.ContainingType,
                "Microsoft.AspNetCore.Http",
                "IHttpContextAccessor"))
        {
            return true;
        }

        return property.ExplicitInterfaceImplementations.Any(implementation =>
            implementation.Name == "HttpContext" &&
            AnalyzerUtilities.IsType(
                implementation.ContainingType,
                "Microsoft.AspNetCore.Http",
                "IHttpContextAccessor"));
    }
}
