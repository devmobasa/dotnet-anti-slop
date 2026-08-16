using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace DotNetAntiSlop.Analyzers;

/// <summary>Analyzes Blazor component callback usage in editable C# source.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlazorAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(RuleDescriptors.DAS2010);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            BlazorAnalysis.AnalyzeInvocation,
            SyntaxKind.InvocationExpression);
    }
}

/// <summary>Analyzes Blazor component callback usage mapped to editable Razor source.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RazorGeneratedBlazorAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(RuleDescriptors.DAS2010);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze |
            GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            BlazorAnalysis.AnalyzeMappedRazorInvocation,
            SyntaxKind.InvocationExpression);
    }
}

internal static class BlazorAnalysis
{
    internal static void AnalyzeInvocation(SyntaxNodeAnalysisContext context) =>
        AnalyzeInvocation(context, requireMappedRazor: false);

    internal static void AnalyzeMappedRazorInvocation(SyntaxNodeAnalysisContext context) =>
        AnalyzeInvocation(context, requireMappedRazor: true);

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        bool requireMappedRazor)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (requireMappedRazor && !IsMappedRazor(invocation))
        {
            return;
        }

        var method = AnalyzerUtilities.GetInvokedMethod(
            context.SemanticModel,
            invocation,
            context.CancellationToken);
        if (method == null ||
            method.Name != "InvokeAsync" ||
            !AnalyzerUtilities.IsType(
                method.ContainingType,
                "Microsoft.AspNetCore.Components",
                "EventCallback"))
        {
            return;
        }

        IOperation? operation = context.SemanticModel.GetOperation(
            invocation,
            context.CancellationToken) as IInvocationOperation;
        while (operation?.Parent is IConversionOperation or IConditionalAccessOperation)
        {
            operation = operation.Parent;
        }

        if (operation?.Parent is IExpressionStatementOperation ||
            operation?.Parent is ISimpleAssignmentOperation
            {
                Target: IDiscardOperation
            })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RuleDescriptors.DAS2010,
                invocation.GetLocation()));
        }
    }

    private static bool IsMappedRazor(SyntaxNode node)
    {
        var path = node.SyntaxTree.FilePath;
        return (path.EndsWith(
                    ".g.cs",
                    System.StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(
                    ".g.i.cs",
                    System.StringComparison.OrdinalIgnoreCase)) &&
               node.GetLocation()
            .GetMappedLineSpan()
            .Path.EndsWith(
                ".razor",
                System.StringComparison.OrdinalIgnoreCase);
    }
}
