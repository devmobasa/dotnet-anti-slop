using System;
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

/// <summary>Analyzes general C# runtime, async, LINQ, and collection usage.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RuntimeAnalyzer : DiagnosticAnalyzer
{
    private const int RunContinuationsAsynchronouslyOption = 64;

    private static readonly ImmutableHashSet<string> EnumerationTerminals =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Any",
            "All",
            "Count",
            "LongCount",
            "First",
            "FirstOrDefault",
            "Single",
            "SingleOrDefault",
            "Last",
            "LastOrDefault",
            "ToList",
            "ToArray",
            "ToDictionary",
            "ToHashSet",
            "Max",
            "Min",
            "Sum",
            "Average",
            "Aggregate");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            RuleDescriptors.DAS1001,
            RuleDescriptors.DAS1002,
            RuleDescriptors.DAS1003,
            RuleDescriptors.DAS1004,
            RuleDescriptors.DAS1005,
            RuleDescriptors.DAS1006,
            RuleDescriptors.DAS1007,
            RuleDescriptors.DAS1008,
            RuleDescriptors.DAS1009,
            RuleDescriptors.DAS1010,
            RuleDescriptors.DAS1011,
            RuleDescriptors.DAS1012,
            RuleDescriptors.DAS1013,
            RuleDescriptors.DAS1014,
            RuleDescriptors.DAS1015);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeInvocation,
            SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeMemberAccess,
            SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeMethod,
            SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(
            AnalyzeLocalFunction,
            SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(
            AnalyzeAssignment,
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.AddAssignmentExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeArgument,
            SyntaxKind.Argument);
        context.RegisterSyntaxNodeAction(
            AnalyzeBlock,
            SyntaxKind.Block);
        context.RegisterSyntaxNodeAction(
            AnalyzeTaskCompletionSourceCreation,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeAsyncAnonymousFunction,
            SyntaxKind.ParenthesizedLambdaExpression,
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.AnonymousMethodExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeCatchClause,
            SyntaxKind.CatchClause);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var method = AnalyzerUtilities.GetInvokedMethod(
            context.SemanticModel,
            invocation,
            context.CancellationToken);

        AnalyzeSyncOverAsyncInvocation(context, invocation, method);
        AnalyzeThreadSleep(context, invocation, method);
        AnalyzeCancellationForwarding(context, invocation, method);
        AnalyzeCancellationNone(context, invocation);
        AnalyzeCountForEmptiness(context, invocation, method);
        AnalyzeUnboundedWhenAll(context, invocation, method);
    }

    private static void AnalyzeSyncOverAsyncInvocation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method)
    {
        if (method == null)
        {
            return;
        }

        if (method.Name == "Wait" && AnalyzerUtilities.IsTaskLike(method.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1001,
                invocation.GetLocation()));
            return;
        }

        if (method.Name != "GetResult")
        {
            return;
        }

        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        var getAwaiterInvocation = memberAccess?.Expression as InvocationExpressionSyntax;
        if (getAwaiterInvocation == null ||
            AnalyzerUtilities.GetInvocationName(getAwaiterInvocation) != "GetAwaiter")
        {
            return;
        }

        var receiver = AnalyzerUtilities.GetInvocationReceiver(getAwaiterInvocation);
        var receiverType = receiver == null
            ? null
            : context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type;

        if (AnalyzerUtilities.IsTaskLike(receiverType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1001,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (memberAccess.Name.Identifier.ValueText != "Result")
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(
            memberAccess,
            context.CancellationToken).Symbol as IPropertySymbol;

        if (symbol != null && AnalyzerUtilities.IsTaskLike(symbol.ContainingType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1001,
                memberAccess.Name.GetLocation()));
        }
    }

    private static void AnalyzeThreadSleep(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method)
    {
        if (method == null ||
            method.Name != "Sleep" ||
            !AnalyzerUtilities.IsType(method.ContainingType, "System.Threading", "Thread"))
        {
            return;
        }

        if (AnalyzerUtilities.IsInsideAsync(invocation) ||
            AnalyzerUtilities.IsRequestHandler(
                invocation,
                context.SemanticModel,
                context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1002,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        if (method.Modifiers.Any(SyntaxKind.AsyncKeyword) &&
            method.ReturnType is PredefinedTypeSyntax predefined &&
            predefined.Keyword.IsKind(SyntaxKind.VoidKeyword) &&
            !AnalyzerUtilities.IsTestMethod(method, context.SemanticModel, context.CancellationToken) &&
            !LooksLikeEventHandler(method, context.SemanticModel, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1003,
                method.ReturnType.GetLocation()));
        }

        AnalyzeRepeatedEnumeration(
            context,
            method,
            method.DescendantNodes(),
            method.GetLocation());

        AnalyzeValueTaskConsumption(
            context,
            method.DescendantNodes(),
            method.GetLocation());
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var local = (LocalFunctionStatementSyntax)context.Node;

        if (local.Modifiers.Any(SyntaxKind.AsyncKeyword) &&
            local.ReturnType is PredefinedTypeSyntax predefined &&
            predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1003,
                local.ReturnType.GetLocation()));
        }

        AnalyzeRepeatedEnumeration(
            context,
            local,
            local.DescendantNodes(),
            local.GetLocation());

        AnalyzeValueTaskConsumption(
            context,
            local.DescendantNodes(),
            local.GetLocation());
    }

    private static bool LooksLikeEventHandler(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (method.ParameterList.Parameters.Count != 2)
        {
            return false;
        }

        var first = semanticModel.GetDeclaredSymbol(
            method.ParameterList.Parameters[0],
            cancellationToken);
        var second = semanticModel.GetDeclaredSymbol(
            method.ParameterList.Parameters[1],
            cancellationToken);

        if (first == null || second == null ||
            first.Type.SpecialType != SpecialType.System_Object)
        {
            return false;
        }

        return AnalyzerUtilities.InheritsFrom(second.Type, "System", "EventArgs") ||
               AnalyzerUtilities.IsType(second.Type, "System", "EventArgs");
    }

    private static void AnalyzeCancellationForwarding(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method)
    {
        if (method == null ||
            !AnalyzerUtilities.IsInsideAsync(invocation) ||
            AnalyzerUtilities.IsEfCoreMethod(method))
        {
            return;
        }

        var availableToken = AnalyzerUtilities.GetAvailableCancellationToken(
            invocation,
            context.SemanticModel,
            context.CancellationToken);
        if (availableToken == null)
        {
            return;
        }

        var operation = AnalyzerUtilities.GetInvocationOperation(
            context.SemanticModel,
            invocation,
            context.CancellationToken);
        if (operation == null)
        {
            return;
        }

        if (AnalyzerUtilities.HasCancellationTokenParameter(method))
        {
            if (!AnalyzerUtilities.IsCancellableArgumentSupplied(operation))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RuleDescriptors.DAS1004,
                    invocation.GetLocation()));
            }

            return;
        }

        if ((method.Name.EndsWith("Async", StringComparison.Ordinal) ||
             AnalyzerUtilities.IsTaskLike(method.ReturnType)) &&
            AnalyzerUtilities.HasApplicableCancellableOverload(
                invocation,
                context.SemanticModel,
                context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1004,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeCancellationNone(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        var availableToken = AnalyzerUtilities.GetAvailableCancellationToken(
            invocation,
            context.SemanticModel,
            context.CancellationToken);
        if (availableToken == null)
        {
            return;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (AnalyzerUtilities.IsCancellationNone(
                argument.Expression,
                context.SemanticModel,
                context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RuleDescriptors.DAS1005,
                    argument.Expression.GetLocation()));
            }
        }
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        if (AnalyzerUtilities.IsInsideLoop(assignment))
        {
            AnalyzeStringAccumulation(context, assignment);
        }
    }

    private static void AnalyzeStringAccumulation(
        SyntaxNodeAnalysisContext context,
        AssignmentExpressionSyntax assignment)
    {
        var leftType = context.SemanticModel.GetTypeInfo(
            assignment.Left,
            context.CancellationToken).Type;
        if (leftType?.SpecialType != SpecialType.System_String)
        {
            return;
        }

        var accumulates = assignment.IsKind(SyntaxKind.AddAssignmentExpression);
        if (!accumulates && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            var leftSymbol = context.SemanticModel.GetSymbolInfo(
                assignment.Left,
                context.CancellationToken).Symbol;

            foreach (var candidate in assignment.Right
                         .DescendantNodesAndSelf()
                         .OfType<ExpressionSyntax>())
            {
                var rightSymbol = context.SemanticModel.GetSymbolInfo(
                    candidate,
                    context.CancellationToken).Symbol;
                if (AnalyzerUtilities.SymbolEquals(leftSymbol, rightSymbol))
                {
                    if (assignment.Right is BinaryExpressionSyntax binary &&
                        binary.IsKind(SyntaxKind.AddExpression))
                    {
                        accumulates = true;
                        break;
                    }

                    if (assignment.Right is InterpolatedStringExpressionSyntax)
                    {
                        accumulates = true;
                        break;
                    }
                }
            }
        }

        if (accumulates)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1006,
                assignment.GetLocation()));
        }
    }

    private static void AnalyzeCountForEmptiness(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method)
    {
        if (method == null ||
            (method.Name != "Count" && method.Name != "LongCount") ||
            !AnalyzerUtilities.IsLinqMethod(method))
        {
            return;
        }

        ExpressionSyntax current = invocation;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized;
        }

        var binary = current.Parent as BinaryExpressionSyntax;
        if (binary == null)
        {
            return;
        }

        ExpressionSyntax other;
        if (binary.Left == current)
        {
            other = binary.Right;
        }
        else if (binary.Right == current)
        {
            other = binary.Left;
        }
        else
        {
            return;
        }

        var constant = context.SemanticModel.GetConstantValue(
            other,
            context.CancellationToken);
        if (!constant.HasValue ||
            !IsNumericZero(constant.Value) ||
            !IsEmptinessComparison(binary.Kind()))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS1007,
            invocation.GetLocation()));
    }

    private static bool IsNumericZero(object? value)
    {
        if (value == null)
        {
            return false;
        }

        try
        {
            return Convert.ToDecimal(value) == decimal.Zero;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsEmptinessComparison(SyntaxKind kind)
    {
        return kind == SyntaxKind.EqualsExpression ||
               kind == SyntaxKind.NotEqualsExpression ||
               kind == SyntaxKind.GreaterThanExpression ||
               kind == SyntaxKind.GreaterThanOrEqualExpression ||
               kind == SyntaxKind.LessThanExpression ||
               kind == SyntaxKind.LessThanOrEqualExpression;
    }

    private static void AnalyzeRepeatedEnumeration(
        SyntaxNodeAnalysisContext context,
        SyntaxNode callable,
        IEnumerable<SyntaxNode> nodes,
        Location fallbackLocation)
    {
        var seen = new Dictionary<ISymbol, Location>(SymbolEqualityComparer.Default);

        foreach (var invocation in nodes.OfType<InvocationExpressionSyntax>())
        {
            var name = AnalyzerUtilities.GetInvocationName(invocation);
            if (name == null || !EnumerationTerminals.Contains(name))
            {
                continue;
            }

            var method = AnalyzerUtilities.GetInvokedMethod(
                context.SemanticModel,
                invocation,
                context.CancellationToken);
            if (method == null || !AnalyzerUtilities.IsLinqMethod(method))
            {
                continue;
            }

            var receiver = AnalyzerUtilities.GetInvocationReceiver(invocation) as IdentifierNameSyntax;
            if (receiver == null)
            {
                continue;
            }

            var symbol = context.SemanticModel.GetSymbolInfo(
                receiver,
                context.CancellationToken).Symbol;
            var type = context.SemanticModel.GetTypeInfo(
                receiver,
                context.CancellationToken).Type;
            if (symbol == null || !AnalyzerUtilities.IsLazySequence(type))
            {
                continue;
            }

            if (seen.ContainsKey(symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RuleDescriptors.DAS1008,
                    invocation.GetLocation()));
            }
            else
            {
                seen.Add(symbol, invocation.GetLocation());
            }
        }

        foreach (var forEach in nodes.OfType<ForEachStatementSyntax>())
        {
            var identifier = forEach.Expression as IdentifierNameSyntax;
            if (identifier == null)
            {
                continue;
            }

            var symbol = context.SemanticModel.GetSymbolInfo(
                identifier,
                context.CancellationToken).Symbol;
            var type = context.SemanticModel.GetTypeInfo(
                identifier,
                context.CancellationToken).Type;
            if (symbol == null || !AnalyzerUtilities.IsLazySequence(type))
            {
                continue;
            }

            if (seen.ContainsKey(symbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RuleDescriptors.DAS1008,
                    forEach.Expression.GetLocation()));
            }
            else
            {
                seen.Add(symbol, fallbackLocation);
            }
        }
    }

    private static void AnalyzeBlock(SyntaxNodeAnalysisContext context)
    {
        var block = (BlockSyntax)context.Node;

        foreach (var declaration in block.Statements.OfType<LocalDeclarationStatementSyntax>())
        {
            foreach (var variable in declaration.Declaration.Variables)
            {
                var creation = variable.Initializer?.Value as ObjectCreationExpressionSyntax;
                if (creation == null || creation.ArgumentList == null ||
                    creation.ArgumentList.Arguments.Count != 0)
                {
                    continue;
                }

                var type = context.SemanticModel.GetTypeInfo(
                    creation,
                    context.CancellationToken).Type as INamedTypeSymbol;
                if (!SupportsCapacity(type))
                {
                    continue;
                }

                var local = context.SemanticModel.GetDeclaredSymbol(
                    variable,
                    context.CancellationToken) as ILocalSymbol;
                if (local == null)
                {
                    continue;
                }

                var declarationIndex = block.Statements.IndexOf(declaration);
                for (var index = declarationIndex + 1; index < block.Statements.Count; index++)
                {
                    var statement = block.Statements[index];
                    if (!IsLoopWithKnowableBound(statement, context.SemanticModel, context.CancellationToken))
                    {
                        continue;
                    }

                    if (LoopAddsTo(statement, local, context.SemanticModel, context.CancellationToken))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            RuleDescriptors.DAS1009,
                            creation.GetLocation()));
                        break;
                    }
                }
            }
        }
    }

    private static bool SupportsCapacity(INamedTypeSymbol? type)
    {
        if (type == null ||
            type.ContainingNamespace?.ToDisplayString() != "System.Collections.Generic")
        {
            return false;
        }

        return type.Name == "List" ||
               type.Name == "Dictionary" ||
               type.Name == "HashSet" ||
               type.Name == "Queue" ||
               type.Name == "Stack";
    }

    private static bool IsLoopWithKnowableBound(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var forEach = statement as ForEachStatementSyntax;
        if (forEach != null)
        {
            var type = semanticModel.GetTypeInfo(
                forEach.Expression,
                cancellationToken).Type;
            return AnalyzerUtilities.IsMaterializedCollection(type);
        }

        var forStatement = statement as ForStatementSyntax;
        if (forStatement != null && forStatement.Condition != null)
        {
            return forStatement.Condition.DescendantNodesAndSelf()
                .OfType<MemberAccessExpressionSyntax>()
                .Any(member =>
                    member.Name.Identifier.ValueText == "Count" ||
                    member.Name.Identifier.ValueText == "Length");
        }

        return false;
    }

    private static bool LoopAddsTo(
        StatementSyntax loop,
        ILocalSymbol local,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var invocation in loop.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = AnalyzerUtilities.GetInvocationName(invocation);
            if (name != "Add" && name != "Enqueue" && name != "Push" && name != "TryAdd")
            {
                continue;
            }

            var receiver = AnalyzerUtilities.GetInvocationReceiver(invocation);
            var symbol = receiver == null
                ? null
                : semanticModel.GetSymbolInfo(receiver, cancellationToken).Symbol;
            if (AnalyzerUtilities.SymbolEquals(local, symbol))
            {
                return true;
            }
        }

        foreach (var assignment in loop.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var elementAccess = assignment.Left as ElementAccessExpressionSyntax;
            if (elementAccess == null)
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(
                elementAccess.Expression,
                cancellationToken).Symbol;
            if (AnalyzerUtilities.SymbolEquals(local, symbol))
            {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeArgument(SyntaxNodeAnalysisContext context)
    {
        var argument = (ArgumentSyntax)context.Node;
        if (!AnalyzerUtilities.IsInsideLoop(argument))
        {
            return;
        }

        var typeInfo = context.SemanticModel.GetTypeInfo(
            argument.Expression,
            context.CancellationToken);
        if (typeInfo.Type == null ||
            !typeInfo.Type.IsValueType ||
            typeInfo.ConvertedType == null ||
            typeInfo.ConvertedType.IsValueType)
        {
            return;
        }

        var conversion = context.SemanticModel.ClassifyConversion(
            argument.Expression,
            typeInfo.ConvertedType);
        if (conversion.IsBoxing)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1010,
                argument.Expression.GetLocation()));
        }
    }

    private static void AnalyzeValueTaskConsumption(
        SyntaxNodeAnalysisContext context,
        IEnumerable<SyntaxNode> nodes,
        Location fallbackLocation)
    {
        var valueTasks = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var variable in nodes.OfType<VariableDeclaratorSyntax>())
        {
            var symbol = context.SemanticModel.GetDeclaredSymbol(
                variable,
                context.CancellationToken);
            if (symbol is ILocalSymbol local &&
                AnalyzerUtilities.IsValueTask(local.Type))
            {
                valueTasks.Add(local);
            }
        }

        foreach (var parameter in nodes.OfType<ParameterSyntax>())
        {
            var symbol = context.SemanticModel.GetDeclaredSymbol(
                parameter,
                context.CancellationToken);
            if (symbol is IParameterSymbol parameterSymbol &&
                AnalyzerUtilities.IsValueTask(parameterSymbol.Type))
            {
                valueTasks.Add(parameterSymbol);
            }
        }

        foreach (var valueTask in valueTasks)
        {
            Location? firstUse = null;
            foreach (var identifier in nodes.OfType<IdentifierNameSyntax>())
            {
                var symbol = context.SemanticModel.GetSymbolInfo(
                    identifier,
                    context.CancellationToken).Symbol;
                if (!AnalyzerUtilities.SymbolEquals(valueTask, symbol) ||
                    !IsValueTaskConsumption(identifier))
                {
                    continue;
                }

                if (firstUse == null)
                {
                    firstUse = identifier.GetLocation();
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        RuleDescriptors.DAS1011,
                        identifier.GetLocation()));
                }
            }
        }
    }

    private static bool IsValueTaskConsumption(IdentifierNameSyntax identifier)
    {
        if (identifier.Parent is AwaitExpressionSyntax)
        {
            return true;
        }

        var member = identifier.Parent as MemberAccessExpressionSyntax;
        if (member == null || member.Expression != identifier)
        {
            return false;
        }

        var name = member.Name.Identifier.ValueText;
        return name == "AsTask" || name == "GetAwaiter" || name == "Result";
    }

    private static void AnalyzeUnboundedWhenAll(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? method)
    {
        if (method == null ||
            method.Name != "WhenAll" ||
            !AnalyzerUtilities.IsType(method.ContainingType, "System.Threading.Tasks", "Task") ||
            invocation.ArgumentList.Arguments.Count == 0)
        {
            return;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var projection = argument.Expression.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(candidate =>
                    AnalyzerUtilities.GetInvocationName(candidate) == "Select");

            if (projection == null)
            {
                continue;
            }

            var receiver = AnalyzerUtilities.GetInvocationReceiver(projection);
            if (receiver == null ||
                AnalyzerUtilities.ContainsInvocationNamed(receiver, "Take"))
            {
                continue;
            }

            var type = context.SemanticModel.GetTypeInfo(
                receiver,
                context.CancellationToken).Type;
            if (AnalyzerUtilities.IsLazySequence(type) ||
                AnalyzerUtilities.IsMaterializedCollection(type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RuleDescriptors.DAS1012,
                    invocation.GetLocation()));
                return;
            }
        }
    }

    private static void AnalyzeTaskCompletionSourceCreation(
        SyntaxNodeAnalysisContext context)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;
        var operation = context.SemanticModel.GetOperation(
            creation,
            context.CancellationToken) as IObjectCreationOperation;
        if (operation == null ||
            !AnalyzerUtilities.IsType(
                operation.Type,
                "System.Threading.Tasks",
                "TaskCompletionSource"))
        {
            return;
        }

        var optionsArgument = operation.Arguments.FirstOrDefault(argument =>
            AnalyzerUtilities.IsType(
                argument.Parameter?.Type,
                "System.Threading.Tasks",
                "TaskCreationOptions"));
        if (optionsArgument == null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1013,
                creation.GetLocation()));
            return;
        }

        var constant = optionsArgument.Value.ConstantValue;
        if (constant.HasValue &&
            constant.Value is int options &&
            (options & RunContinuationsAsynchronouslyOption) == 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS1013,
                creation.GetLocation()));
        }
    }

    private static void AnalyzeAsyncAnonymousFunction(
        SyntaxNodeAnalysisContext context)
    {
        var anonymousFunction = (AnonymousFunctionExpressionSyntax)context.Node;
        if (!anonymousFunction.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
        {
            return;
        }

        var convertedType = context.SemanticModel.GetTypeInfo(
            anonymousFunction,
            context.CancellationToken).ConvertedType as INamedTypeSymbol;
        if (convertedType?.DelegateInvokeMethod?.ReturnsVoid != true ||
            IsDirectEventHandler(
                anonymousFunction,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS1014,
            anonymousFunction.AsyncKeyword.GetLocation()));
    }

    private static bool IsDirectEventHandler(
        AnonymousFunctionExpressionSyntax anonymousFunction,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax expression = anonymousFunction;
        while (expression.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized;
        }

        var assignment = expression.Parent as AssignmentExpressionSyntax;
        return assignment != null &&
               assignment.Right == expression &&
               semanticModel.GetSymbolInfo(
                   assignment.Left,
                   cancellationToken).Symbol is IEventSymbol;
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Block.Statements.Count != 0 ||
            IsDocumentedSpecificException(catchClause, context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS1015,
            catchClause.CatchKeyword.GetLocation()));
    }

    private static bool IsDocumentedSpecificException(
        CatchClauseSyntax catchClause,
        SyntaxNodeAnalysisContext context)
    {
        var exceptionType = catchClause.Declaration == null
            ? null
            : context.SemanticModel.GetTypeInfo(
                catchClause.Declaration.Type,
                context.CancellationToken).Type;
        if (exceptionType == null ||
            AnalyzerUtilities.IsType(exceptionType, "System", "Exception") ||
            !AnalyzerUtilities.InheritsFrom(exceptionType, "System", "Exception"))
        {
            return false;
        }

        return catchClause.Block.DescendantTrivia(descendIntoTrivia: true)
            .Any(HasExplanatoryComment);
    }

    private static bool HasExplanatoryComment(SyntaxTrivia trivia)
    {
        var text = trivia.ToString();
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
        {
            return text.Substring(2).Trim().Length != 0;
        }

        return trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) &&
               text.Length > 4 &&
               text.Substring(2, text.Length - 4).Trim().Length != 0;
    }
}
