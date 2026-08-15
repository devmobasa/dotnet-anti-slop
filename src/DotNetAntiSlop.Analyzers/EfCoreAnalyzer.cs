using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotNetAntiSlop.Analyzers;

/// <summary>Analyzes Entity Framework Core query and context usage.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EfCoreAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> RawSqlMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "FromSqlRaw",
            "ExecuteSqlRaw",
            "ExecuteSqlRawAsync",
            "SqlQueryRaw");

    private static readonly ImmutableHashSet<string> QueryMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ToList",
            "ToListAsync",
            "ToArray",
            "ToArrayAsync",
            "Any",
            "AnyAsync",
            "All",
            "AllAsync",
            "Count",
            "CountAsync",
            "LongCount",
            "LongCountAsync",
            "Contains",
            "ContainsAsync",
            "First",
            "FirstAsync",
            "FirstOrDefault",
            "FirstOrDefaultAsync",
            "Single",
            "SingleAsync",
            "SingleOrDefault",
            "SingleOrDefaultAsync",
            "Last",
            "LastAsync",
            "LastOrDefault",
            "LastOrDefaultAsync",
            "Find",
            "FindAsync",
            "Load",
            "LoadAsync",
            "ExecuteDelete",
            "ExecuteDeleteAsync",
            "ExecuteUpdate",
            "ExecuteUpdateAsync");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            RuleDescriptors.DAS3001,
            RuleDescriptors.DAS3002,
            RuleDescriptors.DAS3003,
            RuleDescriptors.DAS3004,
            RuleDescriptors.DAS3005,
            RuleDescriptors.DAS3006,
            RuleDescriptors.DAS3007,
            RuleDescriptors.DAS3008,
            RuleDescriptors.DAS3009,
            RuleDescriptors.DAS3010);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeInvocation,
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
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

        AnalyzeReadOnlyTracking(context, invocation, method);
        AnalyzeTrackingOverride(context, invocation, method);
        AnalyzeQueryCancellation(context, invocation, method);
        AnalyzeSaveCancellation(context, invocation, method);
        AnalyzeQueryInLoop(context, invocation, method);
        AnalyzeClientSideShaping(context, invocation, method);
        AnalyzeRawSql(context, invocation, method);
        AnalyzeUnboundedMaterialization(context, invocation, method);
        AnalyzeCountAsyncForExistence(context, invocation, method);
        AnalyzeParallelContextUse(context, invocation, method);
    }

    private static void AnalyzeReadOnlyTracking(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        var name = method.Name;
        if (!AnalyzerUtilities.IsEntityMaterializer(name) &&
            name != "Find" &&
            name != "FindAsync")
        {
            return;
        }

        if (!IsEfQueryMethod(method, invocation, context) ||
            !AnalyzerUtilities.IsReadOnlyContext(
                invocation,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        var receiver = AnalyzerUtilities.GetInvocationReceiver(invocation);
        if (receiver == null ||
            !AnalyzerUtilities.ContainsEfSource(
                receiver,
                context.SemanticModel,
                context.CancellationToken) ||
            AnalyzerUtilities.ContainsInvocationNamed(
                receiver,
                "AsNoTracking",
                "AsNoTrackingWithIdentityResolution") ||
            AnalyzerUtilities.ContainsInvocationNamed(receiver, "AsTracking"))
        {
            return;
        }

        var entityType = AnalyzerUtilities.FindDbSetEntityType(
            receiver,
            context.SemanticModel,
            context.CancellationToken);
        if (entityType == null)
        {
            return;
        }

        var receiverType = context.SemanticModel.GetTypeInfo(
            receiver,
            context.CancellationToken).Type;
        var elementType = AnalyzerUtilities.GetSequenceElementType(receiverType);

        if (name != "Find" &&
            name != "FindAsync" &&
            (elementType == null ||
             !SymbolEqualityComparer.Default.Equals(
                 elementType,
                 entityType)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS3001,
            invocation.GetLocation()));
    }

    private static void AnalyzeTrackingOverride(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (method.Name != "AsTracking" ||
            !AnalyzerUtilities.IsEfCoreMethod(method))
        {
            return;
        }

        var receiver = AnalyzerUtilities.GetInvocationReceiver(invocation);
        if (receiver == null)
        {
            return;
        }

        if (AnalyzerUtilities.ContainsInvocationNamed(
                receiver,
                "AsNoTracking",
                "AsNoTrackingWithIdentityResolution") ||
            AnalyzerUtilities.IsReadOnlyContext(
                invocation,
                context.SemanticModel,
                context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS3002,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeQueryCancellation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (!AnalyzerUtilities.IsKnownAsyncTerminal(method.Name) ||
            !AnalyzerUtilities.IsEfCoreMethod(method) ||
            method.Name == "SaveChangesAsync" ||
            !AnalyzerUtilities.IsInsideAsync(invocation))
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

        var cancellable = AnalyzerUtilities.HasCancellationTokenParameter(method) ||
                          AnalyzerUtilities.HasApplicableCancellableOverload(
                              invocation,
                              context.SemanticModel,
                              context.CancellationToken);
        if (!cancellable)
        {
            return;
        }

        if (!AnalyzerUtilities.HasCancellationTokenParameter(method) ||
            !AnalyzerUtilities.IsCancellableArgumentSupplied(operation))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS3003,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeSaveCancellation(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (method.Name != "SaveChangesAsync" ||
            !AnalyzerUtilities.IsDbContext(method.ContainingType) ||
            !AnalyzerUtilities.IsInsideAsync(invocation))
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

        if (!AnalyzerUtilities.HasCancellationTokenParameter(method) ||
            !AnalyzerUtilities.IsCancellableArgumentSupplied(operation))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS3004,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeQueryInLoop(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (!AnalyzerUtilities.IsInsideLoop(invocation) ||
            !QueryMethods.Contains(method.Name) ||
            !IsEfQueryMethod(method, invocation, context))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS3005,
            invocation.GetLocation()));
    }

    private static void AnalyzeClientSideShaping(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (!AnalyzerUtilities.IsQueryableShapingMethod(method.Name) ||
            !AnalyzerUtilities.IsLinqMethod(method))
        {
            return;
        }

        var receiver = AnalyzerUtilities.GetInvocationReceiver(invocation);
        if (receiver == null ||
            !AnalyzerUtilities.ContainsInvocationNamed(
                receiver,
                "ToList",
                "ToListAsync",
                "ToArray",
                "ToArrayAsync",
                "AsEnumerable") ||
            !AnalyzerUtilities.ContainsEfSource(
                receiver,
                context.SemanticModel,
                context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS3006,
            invocation.GetLocation()));
    }

    private static void AnalyzeRawSql(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (!RawSqlMethods.Contains(method.Name) ||
            !AnalyzerUtilities.IsEfCoreMethod(method))
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

        var sqlArgument = FindSqlArgument(operation);
        if (sqlArgument == null ||
            !(sqlArgument.Syntax is ArgumentSyntax argumentSyntax))
        {
            return;
        }

        var expression = argumentSyntax.Expression;
        var constant = context.SemanticModel.GetConstantValue(
            expression,
            context.CancellationToken);
        if (constant.HasValue && constant.Value is string)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS3007,
            expression.GetLocation()));
    }

    private static IArgumentOperation? FindSqlArgument(IInvocationOperation operation)
    {
        foreach (var argument in operation.Arguments)
        {
            var parameter = argument.Parameter;
            if (parameter == null ||
                parameter.Type.SpecialType != SpecialType.System_String)
            {
                continue;
            }

            if (parameter.Name.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0 ||
                parameter.Ordinal == 0 ||
                parameter.Ordinal == 1)
            {
                return argument;
            }
        }

        return null;
    }

    private static void AnalyzeUnboundedMaterialization(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (method.Name != "ToListAsync" &&
            method.Name != "ToArrayAsync" &&
            method.Name != "ToList" &&
            method.Name != "ToArray")
        {
            return;
        }

        if (!AnalyzerUtilities.IsReadOnlyRequestHandler(
                invocation,
                context.SemanticModel,
                context.CancellationToken) ||
            !IsEfQueryMethod(method, invocation, context))
        {
            return;
        }

        var receiver = AnalyzerUtilities.GetInvocationReceiver(invocation);
        if (receiver == null ||
            AnalyzerUtilities.ContainsBound(receiver))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS3008,
            invocation.GetLocation()));
    }

    private static void AnalyzeCountAsyncForExistence(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (method.Name != "CountAsync" ||
            !AnalyzerUtilities.IsEfCoreMethod(method))
        {
            return;
        }

        SyntaxNode current = invocation;
        for (var depth = 0; depth < 4 && current.Parent != null; depth++)
        {
            if (current.Parent is ParenthesizedExpressionSyntax ||
                current.Parent is AwaitExpressionSyntax)
            {
                current = current.Parent;
                continue;
            }

            if (current.Parent is MemberAccessExpressionSyntax member &&
                member.Name.Identifier.ValueText == "Result")
            {
                current = current.Parent;
                continue;
            }

            break;
        }

        var binary = current.Parent as BinaryExpressionSyntax;
        if (binary == null)
        {
            return;
        }

        ExpressionSyntax? other = null;
        if (binary.Left == current)
        {
            other = binary.Right;
        }
        else if (binary.Right == current)
        {
            other = binary.Left;
        }

        if (other == null)
        {
            return;
        }

        var value = context.SemanticModel.GetConstantValue(
            other,
            context.CancellationToken);
        if (!value.HasValue || !IsZero(value.Value))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            RuleDescriptors.DAS3009,
            invocation.GetLocation()));
    }

    private static bool IsZero(object? value)
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

    private static void AnalyzeParallelContextUse(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (method.Name != "WhenAll" ||
            !AnalyzerUtilities.IsType(
                method.ContainingType,
                "System.Threading.Tasks",
                "Task"))
        {
            return;
        }

        var counts = new Dictionary<ISymbol, int>(
            SymbolEqualityComparer.Default);

        foreach (var nested in invocation.ArgumentList.Arguments
                     .SelectMany(argument => argument.Expression.DescendantNodesAndSelf())
                     .OfType<InvocationExpressionSyntax>())
        {
            if (nested == invocation)
            {
                continue;
            }

            var nestedMethod = AnalyzerUtilities.GetInvokedMethod(
                context.SemanticModel,
                nested,
                context.CancellationToken);
            if (nestedMethod == null ||
                !nestedMethod.Name.EndsWith("Async", StringComparison.Ordinal) ||
                !IsEfQueryMethod(nestedMethod, nested, context))
            {
                continue;
            }

            var root = AnalyzerUtilities.FindDbContextRootSymbol(
                nested,
                context.SemanticModel,
                context.CancellationToken);
            if (root == null)
            {
                continue;
            }

            int count;
            counts.TryGetValue(root, out count);
            count++;
            counts[root] = count;
            if (count > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    RuleDescriptors.DAS3010,
                    invocation.GetLocation()));
                return;
            }
        }
    }

    private static bool IsEfQueryMethod(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SyntaxNodeAnalysisContext context)
    {
        if (AnalyzerUtilities.IsEfCoreMethod(method))
        {
            return true;
        }

        if (!AnalyzerUtilities.IsLinqMethod(method))
        {
            return false;
        }

        var receiver = AnalyzerUtilities.GetInvocationReceiver(invocation);
        return receiver != null &&
               AnalyzerUtilities.ContainsEfSource(
                   receiver,
                   context.SemanticModel,
                   context.CancellationToken);
    }
}
