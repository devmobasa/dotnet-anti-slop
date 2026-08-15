using Xunit;

namespace DotNetAntiSlop.Analyzers.Tests;

public sealed class TestingAnalyzerTests
{
    [Fact]
    public Task Reports_async_void_test() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS4001",
            """
            using System.Threading.Tasks;
            using Xunit;
            sealed class Tests
            {
                [Fact]
                public async void Saves_order()
                {
                    await Task.Yield();
                }
            }
            """);

    [Fact]
    public Task Async_task_test_is_allowed() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS4001",
            """
            using System.Threading.Tasks;
            using Xunit;
            sealed class Tests
            {
                [Fact]
                public async Task Saves_order()
                {
                    await Task.Yield();
                }
            }
            """);
}
