using System.Collections.Immutable;
using DotNetAntiSlop.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace DotNetAntiSlop.Analyzers.Tests;

internal static class AnalyzerTestHost
{
    private static readonly ImmutableArray<MetadataReference> References = LoadReferences();

    internal static ImmutableArray<DiagnosticAnalyzer> AllAnalyzers { get; } =
        ImmutableArray.Create<DiagnosticAnalyzer>(
            new RuntimeAnalyzer(),
            new AspNetCoreAnalyzer(),
            new BlazorAnalyzer(),
            new RazorGeneratedBlazorAnalyzer(),
            new EfCoreAnalyzer(),
            new TestingAnalyzer());

    internal static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        params DiagnosticAnalyzer[] analyzers)
    {
        return await GetDiagnosticsAsync(source, "Test0.cs", analyzers);
    }

    internal static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        string sourcePath,
        params DiagnosticAnalyzer[] analyzers)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(
                FrameworkStubs.All,
                new CSharpParseOptions(LanguageVersion.Preview),
                path: "FrameworkStubs.cs"),
            CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                path: sourcePath)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: syntaxTrees,
            references: References,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var selected = analyzers.Length == 0
            ? AllAnalyzers
            : analyzers.ToImmutableArray();

        var compilerErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            compilerErrors.Length == 0,
            string.Join(Environment.NewLine, compilerErrors.Select(item => item.ToString())));

        var diagnostics = await compilation
            .WithAnalyzers(selected)
            .GetAnalyzerDiagnosticsAsync();

        var analyzerFailures = diagnostics
            .Where(diagnostic => diagnostic.Id == "AD0001")
            .ToArray();
        Assert.True(
            analyzerFailures.Length == 0,
            string.Join(Environment.NewLine, analyzerFailures.Select(item => item.ToString())));

        return diagnostics
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal static async Task AssertHasDiagnosticAsync(
        string id,
        string source)
    {
        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == id);
    }

    internal static async Task AssertNoDiagnosticAsync(
        string id,
        string source)
    {
        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == id);
    }

    internal static async Task AssertHasDiagnosticInGeneratedCodeAsync(
        string id,
        string source)
    {
        var diagnostics = await GetDiagnosticsAsync(
            "#line 1 \"Component.razor\"" + Environment.NewLine +
            source + Environment.NewLine +
            "#line default",
            "Component.razor.g.cs",
            new RazorGeneratedBlazorAnalyzer());
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == id);
    }

    internal static async Task AssertNoDiagnosticInRazorAnalyzerGeneratedCodeAsync(
        string id,
        string source)
    {
        var diagnostics = await GetDiagnosticsAsync(
            source,
            "Generated.g.cs",
            new BlazorAnalyzer(),
            new RazorGeneratedBlazorAnalyzer());
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == id);
    }

    internal static async Task AssertNoDiagnosticInGeneratedCodeAsync(
        string id,
        string source)
    {
        var diagnostics = await GetDiagnosticsAsync(
            source,
            "Generated.g.cs");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == id);
    }

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");

        Assert.False(string.IsNullOrWhiteSpace(trustedPlatformAssemblies));

        return trustedPlatformAssemblies!
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToImmutableArray();
    }
}
