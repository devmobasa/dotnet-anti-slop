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

        Assert.Equal(36, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());

        var supportedIds = AnalyzerTestHost.AllAnalyzers
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .Select(descriptor => descriptor.Id)
            .ToArray();
        var implemented = supportedIds
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ids, implemented);

        var duplicateImplementations = supportedIds
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();
        var razorRule = Assert.Single(duplicateImplementations);
        Assert.Equal(DiagnosticIds.DAS2010, razorRule.Key);
        Assert.Equal(2, razorRule.Count());

        Assert.All(
            AnalyzerTestHost.AllAnalyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics),
            descriptor => Assert.Equal(
                $"https://github.com/devmobasa/dotnet-anti-slop/blob/main/docs/rules/{descriptor.Id}.md",
                descriptor.HelpLinkUri));
    }
}
