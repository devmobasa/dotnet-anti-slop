using Xunit;

namespace DotNetAntiSlop.Analyzers.Tests;

public sealed class AspNetCoreAnalyzerTests
{
    [Fact]
    public Task Reports_nested_service_provider() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2001",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    _ = services.BuildServiceProvider();
                }
            }
            """);

    [Fact]
    public Task Ignores_unrelated_build_service_provider_method() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2001",
            """
            namespace Custom;
            sealed class Builder
            {
                public object BuildServiceProvider() => new object();
            }
            sealed class Sample
            {
                object Run(Builder builder) => builder.BuildServiceProvider();
            }
            """);

    [Fact]
    public Task Reports_db_context_registered_as_singleton() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2002",
            """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;
            sealed class AppDbContext : DbContext { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    services.AddSingleton<AppDbContext>();
                }
            }
            """);

    [Fact]
    public Task Reports_scoped_dependency_captured_by_singleton() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2003",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class RequestStore { }
            sealed class ReportCache
            {
                public ReportCache(RequestStore store) { }
            }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    services.AddScoped<RequestStore>();
                    services.AddSingleton<ReportCache>();
                }
            }
            """);

    [Fact]
    public Task Reports_async_endpoint_without_cancellation_token() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2004",
            """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Builder;
            sealed class Sample
            {
                void Map(IEndpointRouteBuilder app)
                {
                    app.MapGet("/value", async () =>
                    {
                        await Task.Delay(10);
                        return 42;
                    });
                }
            }
            """);

    [Fact]
    public Task Endpoint_with_cancellation_token_is_not_reported() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2004",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Builder;
            sealed class Sample
            {
                void Map(IEndpointRouteBuilder app)
                {
                    app.MapGet("/value", async (CancellationToken cancellationToken) =>
                    {
                        await Task.Delay(10, cancellationToken);
                        return 42;
                    });
                }
            }
            """);

    [Fact]
    public Task Custom_map_get_lookalike_is_ignored() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2004",
            """
            using System;
            using System.Threading.Tasks;
            namespace Custom
            {
                sealed class Router { }
                static class Routes
                {
                    public static void MapGet(
                        this Router router,
                        string route,
                        Func<Task<int>> handler) { }
                }
                sealed class Sample
                {
                    void Map(Router router)
                    {
                        router.MapGet("/value", async () =>
                        {
                            await Task.Delay(1);
                            return 1;
                        });
                    }
                }
            }
            """);

    [Fact]
    public Task Reports_http_client_construction_in_controller_action() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2005",
            """
            using System.Net.Http;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;
            sealed class ValuesController : ControllerBase
            {
                [HttpGet]
                public async Task<string> Get()
                {
                    using var client = new HttpClient();
                    return await client.GetStringAsync("https://example.invalid");
                }
            }
            """);

    [Fact]
    public Task Reports_unguarded_development_middleware() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2006",
            """
            using Microsoft.AspNetCore.Builder;
            sealed class Sample
            {
                void Configure(WebApplication app)
                {
                    app.UseDeveloperExceptionPage();
                }
            }
            """);

    [Fact]
    public Task Development_guard_is_recognized() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2006",
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.Extensions.Hosting;
            sealed class Sample
            {
                void Configure(WebApplication app)
                {
                    if (app.Environment.IsDevelopment())
                    {
                        app.UseDeveloperExceptionPage();
                        app.UseSwaggerUI();
                    }
                }
            }
            """);

    [Fact]
    public Task Negated_early_exit_development_guard_is_recognized() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2006",
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.Extensions.Hosting;
            sealed class Sample
            {
                void Configure(WebApplication app)
                {
                    if (!app.Environment.IsDevelopment())
                    {
                        return;
                    }

                    app.UseSwaggerUI();
                }
            }
            """);

    [Fact]
    public Task Reports_fire_and_forget_request_task() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2007",
            """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Builder;
            sealed class Sample
            {
                void Map(IEndpointRouteBuilder app)
                {
                    app.MapPost<string, int>("/jobs", async job =>
                    {
                        _ = Task.Delay(100);
                        await Task.Yield();
                        return 202;
                    });
                }
            }
            """);

    [Fact]
    public Task Reports_http_context_accessor_context_assigned_to_field() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2008",
            """
            using Microsoft.AspNetCore.Http;
            sealed class Sample
            {
                private readonly HttpContext? context;

                public Sample(IHttpContextAccessor accessor)
                {
                    context = accessor.HttpContext;
                }
            }
            """);

    [Fact]
    public Task Reports_http_context_accessor_context_used_in_field_initializer() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2008",
            """
            using Microsoft.AspNetCore.Http;
            sealed class Sample(IHttpContextAccessor accessor)
            {
                private readonly HttpContext? context = accessor.HttpContext;
            }
            """);

    [Fact]
    public Task Reports_http_context_accessor_context_assigned_to_property() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2008",
            """
            using Microsoft.AspNetCore.Http;
            sealed class Sample
            {
                private HttpContext? Context { get; set; }

                void Capture(IHttpContextAccessor accessor)
                {
                    Context = accessor.HttpContext;
                }
            }
            """);

    [Fact]
    public Task Reports_context_from_concrete_http_context_accessor_implementation() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2008",
            """
            using Microsoft.AspNetCore.Http;
            sealed class ContextAccessor : IHttpContextAccessor
            {
                public HttpContext? HttpContext { get; set; }
            }

            sealed class Sample
            {
                private readonly HttpContext? context;

                public Sample(ContextAccessor accessor)
                {
                    context = accessor.HttpContext;
                }
            }
            """);

    [Fact]
    public Task Allows_storing_http_context_accessor() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2008",
            """
            using Microsoft.AspNetCore.Http;
            sealed class Sample
            {
                private readonly IHttpContextAccessor accessor;

                public Sample(IHttpContextAccessor accessor)
                {
                    this.accessor = accessor;
                }

                HttpContext? Current => accessor.HttpContext;
            }
            """);

    [Fact]
    public Task Allows_local_http_context_snapshot() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2008",
            """
            using Microsoft.AspNetCore.Http;
            sealed class Sample
            {
                void Read(IHttpContextAccessor accessor)
                {
                    var current = accessor.HttpContext;
                    _ = current?.RequestAborted;
                }
            }
            """);

    [Fact]
    public Task Ignores_unrelated_http_context_accessor_lookalike() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2008",
            """
            namespace Custom
            {
                sealed class HttpContext { }

                interface IHttpContextAccessor
                {
                    HttpContext? HttpContext { get; }
                }

                sealed class Sample
                {
                    private readonly HttpContext? context;

                    public Sample(IHttpContextAccessor accessor)
                    {
                        context = accessor.HttpContext;
                    }
                }
            }
            """);
}
