using Xunit;

namespace DotNetAntiSlop.Analyzers.Tests;

public sealed class RuntimeAnalyzerTests
{
    [Fact]
    public Task Reports_sync_over_async() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1001",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                int Run() => Task.FromResult(42).Result;
            }
            """);

    [Fact]
    public Task Reports_thread_sleep_in_async_code() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1002",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync()
                {
                    Thread.Sleep(10);
                    await Task.Yield();
                }
            }
            """);

    [Fact]
    public Task Reports_async_void_but_allows_event_handler() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1003",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                public async void RefreshAsync()
                {
                    await Task.Yield();
                }
            }
            """);

    [Fact]
    public Task Event_handler_is_not_reported_as_async_void() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1003",
            """
            using System;
            using System.Threading.Tasks;
            sealed class Sample
            {
                private async void OnChanged(object sender, EventArgs args)
                {
                    await Task.Yield();
                }
            }
            """);

    [Fact]
    public Task Reports_dropped_cancellation_token() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await Task.Delay(10);
                }
            }
            """);

    [Fact]
    public Task Reports_omitted_optional_cancellation_token() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await WorkAsync();
                }

                Task WorkAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            }
            """);

    [Fact]
    public Task Reports_when_a_separate_cancellable_overload_exists() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await WorkAsync(42);
                }

                Task WorkAsync(int value) => Task.CompletedTask;
                Task WorkAsync(int value, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

    [Fact]
    public Task Does_not_report_an_incompatible_cancellable_overload() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await WorkAsync(42);
                }

                Task WorkAsync(int value) => Task.CompletedTask;
                Task WorkAsync(string value, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

    [Fact]
    public Task Does_not_report_when_synthesized_token_binds_to_a_required_object_parameter() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await WorkAsync(42);
                }

                Task WorkAsync(int value) => Task.CompletedTask;
                Task WorkAsync(
                    int value,
                    object requiredState,
                    CancellationToken cancellationToken = default) => Task.CompletedTask;
            }
            """);

    [Fact]
    public Task Reports_an_applicable_reduced_extension_overload() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            static class WorkExtensions
            {
                public static Task WorkAsync(this string value, int count) => Task.CompletedTask;
                public static Task WorkAsync(this string value, int count, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            sealed class Sample
            {
                async Task RunAsync(string value, CancellationToken cancellationToken)
                {
                    await value.WorkAsync(42);
                }
            }
            """);

    [Fact]
    public Task Does_not_report_a_ref_incompatible_cancellable_overload() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await WorkAsync(42);
                }

                Task WorkAsync(int value) => Task.CompletedTask;
                Task WorkAsync(ref int value, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

    [Fact]
    public Task Does_not_report_an_inaccessible_cancellable_overload() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Worker
            {
                public Task WorkAsync(int value) => Task.CompletedTask;
                private Task WorkAsync(int value, CancellationToken cancellationToken) => Task.CompletedTask;
            }

            sealed class Sample
            {
                async Task RunAsync(Worker worker, CancellationToken cancellationToken)
                {
                    await worker.WorkAsync(42);
                }
            }
            """);

    [Fact]
    public Task Does_not_report_an_uninferrable_generic_cancellable_overload() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await WorkAsync(42);
                }

                Task WorkAsync<T>(T value) => Task.CompletedTask;
                Task WorkAsync<T, TOther>(T value, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

    [Fact]
    public Task Does_not_report_forwarded_cancellation_token() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await Task.Delay(10, cancellationToken);
                }
            }
            """);

    [Fact]
    public Task Cancellation_none_is_not_reported_as_an_omitted_token() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await Task.Delay(10, CancellationToken.None);
                }
            }
            """);

    [Fact]
    public Task Does_not_report_without_an_available_cancellation_token() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1004",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync()
                {
                    await Task.Delay(10);
                }
            }
            """);

    [Fact]
    public Task Reports_cancellation_none_when_token_is_available() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1005",
            """
            using System.Threading;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(CancellationToken cancellationToken)
                {
                    await Task.Delay(10, CancellationToken.None);
                }
            }
            """);

    [Fact]
    public Task Reports_string_accumulation_in_loop() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1006",
            """
            using System.Collections.Generic;
            sealed class Sample
            {
                string Join(IEnumerable<string> values)
                {
                    var result = "";
                    foreach (var value in values)
                    {
                        result += value;
                    }

                    return result;
                }
            }
            """);

    [Fact]
    public Task Does_not_report_non_accumulating_string_assignment() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1006",
            """
            using System.Collections.Generic;
            sealed class Sample
            {
                string Last(IEnumerable<int> values)
                {
                    var result = "";
                    foreach (var value in values)
                    {
                        result = value.ToString();
                    }

                    return result;
                }
            }
            """);

    [Fact]
    public Task Reports_count_used_for_existence() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1007",
            """
            using System.Collections.Generic;
            using System.Linq;
            sealed class Sample
            {
                bool HasAny(IEnumerable<int> values) => values.Count() > 0;
            }
            """);

    [Fact]
    public Task Does_not_report_collection_count_property() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1007",
            """
            using System.Collections.Generic;
            sealed class Sample
            {
                bool HasAny(List<int> values) => values.Count > 0;
            }
            """);

    [Fact]
    public Task Reports_repeated_lazy_enumeration() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1008",
            """
            using System.Collections.Generic;
            using System.Linq;
            sealed class Sample
            {
                List<int> Materialize(IEnumerable<int> values)
                {
                    if (values.Any())
                    {
                        return values.ToList();
                    }

                    return new List<int>();
                }
            }
            """);

    [Fact]
    public Task Does_not_report_repeated_list_access() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1008",
            """
            using System.Collections.Generic;
            using System.Linq;
            sealed class Sample
            {
                List<int> Materialize(List<int> values)
                {
                    if (values.Any())
                    {
                        return values.ToList();
                    }

                    return new List<int>();
                }
            }
            """);

    [Fact]
    public Task Reports_missing_collection_capacity() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1009",
            """
            using System.Collections.Generic;
            sealed class Sample
            {
                List<int> Copy(IReadOnlyCollection<int> values)
                {
                    var result = new List<int>();
                    foreach (var value in values)
                    {
                        result.Add(value);
                    }

                    return result;
                }
            }
            """);

    [Fact]
    public Task Reports_boxing_in_loop() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1010",
            """
            using System.Collections;
            using System.Collections.Generic;
            sealed class Sample
            {
                void Copy(IEnumerable<int> values, ArrayList target)
                {
                    foreach (var value in values)
                    {
                        target.Add(value);
                    }
                }
            }
            """);

    [Fact]
    public Task Reports_multiple_value_task_consumption() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1011",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync()
                {
                    var pending = GetAsync();
                    await pending;
                    await pending;
                }

                ValueTask<int> GetAsync() => ValueTask.FromResult(42);
            }
            """);

    [Fact]
    public Task Reports_unbounded_when_all_projection() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1012",
            """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            sealed class Sample
            {
                async Task RunAsync(IEnumerable<int> ids)
                {
                    await Task.WhenAll(ids.Select(LoadAsync));
                }

                Task LoadAsync(int id) => Task.CompletedTask;
            }
            """);

    [Fact]
    public Task Reports_generic_task_completion_source_without_creation_options() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1013",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                TaskCompletionSource<int> Create() => new TaskCompletionSource<int>();
            }
            """);

    [Fact]
    public Task Reports_non_generic_task_completion_source_without_creation_options() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1013",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                TaskCompletionSource Create() => new TaskCompletionSource();
            }
            """);

    [Fact]
    public Task Reports_target_typed_task_completion_source_without_creation_options() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1013",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                TaskCompletionSource<int> Create() => new();
            }
            """);

    [Fact]
    public Task Ignores_unrelated_task_completion_source_lookalike() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1013",
            """
            namespace Custom
            {
                sealed class TaskCompletionSource<T> { }

                sealed class Sample
                {
                    TaskCompletionSource<int> Create() => new();
                }
            }
            """);

    [Fact]
    public Task Reports_task_completion_source_state_overload_without_creation_options() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1013",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                TaskCompletionSource<int> Create(object state) =>
                    new TaskCompletionSource<int>(state);
            }
            """);

    [Fact]
    public Task Reports_task_completion_source_when_constant_options_omit_async_continuations() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1013",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                TaskCompletionSource<int> Create() =>
                    new TaskCompletionSource<int>(TaskCreationOptions.AttachedToParent);
            }
            """);

    [Fact]
    public Task Allows_task_completion_source_with_combined_async_continuation_options() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1013",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                TaskCompletionSource<int> Create(object state) =>
                    new TaskCompletionSource<int>(
                        state,
                        TaskCreationOptions.RunContinuationsAsynchronously |
                            TaskCreationOptions.AttachedToParent);
            }
            """);

    [Fact]
    public Task Does_not_report_task_completion_source_with_unknown_runtime_options() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1013",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                TaskCompletionSource<int> Create(TaskCreationOptions options) =>
                    new TaskCompletionSource<int>(options);
            }
            """);

    [Fact]
    public Task Reports_task_continuation_options_passed_as_task_completion_source_state() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1013",
            """
            using System.Threading.Tasks;
            sealed class Sample
            {
                TaskCompletionSource<int> Create() =>
                    new TaskCompletionSource<int>(
                        TaskContinuationOptions.RunContinuationsAsynchronously);
            }
            """);

    [Fact]
    public Task Reports_async_lambda_converted_to_action() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1014",
            """
            using System;
            using System.Threading.Tasks;
            sealed class Sample
            {
                Action Create() => async () => await Task.Yield();
            }
            """);

    [Fact]
    public Task Allows_synchronous_lambda_converted_to_action() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1014",
            """
            using System;
            sealed class Sample
            {
                Action Create() => () => { };
            }
            """);

    [Fact]
    public Task Reports_async_anonymous_method_converted_to_action() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1014",
            """
            using System;
            using System.Threading.Tasks;
            sealed class Sample
            {
                Action Create() => async delegate { await Task.Yield(); };
            }
            """);

    [Fact]
    public Task Reports_async_lambda_converted_to_custom_void_delegate() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1014",
            """
            using System.Threading.Tasks;
            delegate void Work();
            sealed class Sample
            {
                Work Create() => async () => await Task.Yield();
            }
            """);

    [Fact]
    public Task Allows_async_lambda_converted_to_func_of_task() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1014",
            """
            using System;
            using System.Threading.Tasks;
            sealed class Sample
            {
                Func<Task> Create() => async () => await Task.Yield();
            }
            """);

    [Fact]
    public Task Allows_async_lambda_used_as_direct_event_handler() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1014",
            """
            using System;
            using System.Threading.Tasks;
            sealed class Sample
            {
                event EventHandler? Changed;

                void Subscribe()
                {
                    Changed += async (sender, args) => await Task.Yield();
                }
            }
            """);

    [Fact]
    public Task Reports_empty_untyped_catch() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1015",
            """
            sealed class Sample
            {
                void Run()
                {
                    try { Throw(); }
                    catch { }
                }

                void Throw() { }
            }
            """);

    [Fact]
    public Task Reports_empty_SystemException_catch_even_with_comment() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1015",
            """
            using System;
            sealed class Sample
            {
                void Run()
                {
                    try { Throw(); }
                    catch (Exception)
                    {
                        // Intentionally ignored.
                    }
                }

                void Throw() { }
            }
            """);

    [Fact]
    public Task Reports_empty_specific_exception_catch_without_justification() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1015",
            """
            using System;
            sealed class Sample
            {
                void Run()
                {
                    try { Throw(); }
                    catch (OperationCanceledException) { }
                }

                void Throw() { }
            }
            """);

    [Fact]
    public Task Does_not_treat_comment_before_catch_block_as_justification() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1015",
            """
            using System;
            sealed class Sample
            {
                void Run()
                {
                    try { Throw(); }
                    // Cancellation is expected.
                    catch (OperationCanceledException) { }
                }

                void Throw() { }
            }
            """);

    [Fact]
    public Task Does_not_treat_an_empty_comment_as_justification() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS1015",
            """
            using System;
            sealed class Sample
            {
                void Run()
                {
                    try { Throw(); }
                    catch (OperationCanceledException) { /* */ }
                }

                void Throw() { }
            }
            """);

    [Fact]
    public Task Allows_documented_specific_exception_swallow() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1015",
            """
            using System;
            sealed class Sample
            {
                void Run()
                {
                    try { Throw(); }
                    catch (OperationCanceledException)
                    {
                        // Cancellation is expected after the caller disconnects.
                    }
                }

                void Throw() { }
            }
            """);

    [Fact]
    public Task Allows_documented_specific_exception_with_block_comment() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1015",
            """
            using System;
            sealed class Sample
            {
                void Run()
                {
                    try { Throw(); }
                    catch (OperationCanceledException) { /* Expected during shutdown. */ }
                }

                void Throw() { }
            }
            """);

    [Fact]
    public Task Allows_catch_with_a_handling_statement() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1015",
            """
            using System;
            sealed class Sample
            {
                void Run()
                {
                    try { Throw(); }
                    catch (Exception exception) { Log(exception); }
                }

                void Throw() { }
                void Log(Exception exception) { }
            }
            """);

    [Fact]
    public Task Allows_catch_that_rethrows() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS1015",
            """
            using System;
            sealed class Sample
            {
                void Run()
                {
                    try { Throw(); }
                    catch (Exception) { throw; }
                }

                void Throw() { }
            }
            """);
}
