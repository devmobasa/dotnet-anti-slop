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
            RuleDescriptors.DAS2008);

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
