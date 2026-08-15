using DotNetAntiSlop.Analyzers;
using Xunit;

namespace DotNetAntiSlop.Analyzers.Tests;

public sealed class RuleInventoryTests
{
    [Fact]
    public void All_rule_ids_are_unique_and_implemented()
    {
        var ids = typeof(DiagnosticIds)
            .GetFields()
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(33, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());

        var implemented = AnalyzerTestHost.AllAnalyzers
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .Select(descriptor => descriptor.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ids, implemented);

        Assert.All(
            AnalyzerTestHost.AllAnalyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics),
            descriptor => Assert.Equal(
                $"https://github.com/devmobasa/dotnet-anti-slop/blob/main/docs/rules/{descriptor.Id}.md",
                descriptor.HelpLinkUri));
    }
}
