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

    [Fact]
    public Task Reports_bound_options_without_startup_validation() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<Settings>().Bind(configuration);
                }
            }
            """);

    [Fact]
    public Task Reports_BindConfiguration_without_startup_validation() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    services.AddOptions<Settings>().BindConfiguration("Settings");
                }
            }
            """);

    [Fact]
    public Task Allows_fluent_ValidateOnStart_after_binding() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions<Settings>()
                        .Bind(configuration)
                        .Validate(settings => true)
                        .ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Allows_AddOptionsWithValidateOnStart_before_binding() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    services.AddOptionsWithValidateOnStart<Settings>()
                        .BindConfiguration("Settings");
                }
            }
            """);

    [Fact]
    public Task Allows_prevalidated_builder_stored_in_local_before_binding() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptionsWithValidateOnStart<Settings>();
                    options.BindConfiguration("Settings");
                }
            }
            """);

    [Fact]
    public Task Reports_prevalidated_local_reassigned_before_binding() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    OptionsBuilder<Settings> options =
                        services.AddOptionsWithValidateOnStart<Settings>();
                    options = services.AddOptions<Settings>();
                    options.BindConfiguration("Settings");
                }
            }
            """);

    [Fact]
    public Task Allows_deferred_assignment_after_prevalidated_builder_creation() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    OptionsBuilder<Settings> options =
                        services.AddOptionsWithValidateOnStart<Settings>();
                    Action replace = () => options = services.AddOptions<Settings>();
                    options.BindConfiguration("Settings");
                }
            }
            """);

    [Fact]
    public Task Allows_assignment_in_unused_local_function_after_prevalidated_builder_creation() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    OptionsBuilder<Settings> options =
                        services.AddOptionsWithValidateOnStart<Settings>();
                    void Replace() => options = services.AddOptions<Settings>();
                    options.BindConfiguration("Settings");
                }
            }
            """);

    [Fact]
    public Task Reports_assignment_in_invoked_local_function_before_binding() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    OptionsBuilder<Settings> options =
                        services.AddOptionsWithValidateOnStart<Settings>();
                    void Replace() => options = services.AddOptions<Settings>();
                    Replace();
                    options.BindConfiguration("Settings");
                }
            }
            """);

    [Fact]
    public Task Reports_assignment_in_invoked_delegate_before_binding() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    OptionsBuilder<Settings> options =
                        services.AddOptionsWithValidateOnStart<Settings>();
                    Action replace = () => options = services.AddOptions<Settings>();
                    replace();
                    options.BindConfiguration("Settings");
                }
            }
            """);

    [Fact]
    public Task Reports_conditionally_prevalidated_builder() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services, bool prevalidate)
                {
                    var options = prevalidate
                        ? services.AddOptionsWithValidateOnStart<Settings>()
                        : services.AddOptions<Settings>();
                    options.BindConfiguration("Settings");
                }
            }
            """);

    [Fact]
    public Task Allows_bound_options_local_validated_later() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptions<Settings>()
                        .BindConfiguration("Settings");
                    options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_after_bound_builder_local_is_reassigned() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptions<Settings>("A")
                        .BindConfiguration("A");
                    options = services.AddOptions<Settings>("B");
                    options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_after_invoked_local_function_reassigns_builder() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptions<Settings>("A")
                        .BindConfiguration("A");
                    void Replace() => options = services.AddOptions<Settings>("B");
                    Replace();
                    options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_after_predeclared_local_function_reassigns_builder() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptions<Settings>("A");
                    void Replace() => options = services.AddOptions<Settings>("B");
                    options.BindConfiguration("A");
                    Replace();
                    options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_after_predeclared_delegate_reassigns_builder() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptions<Settings>("A");
                    Action replace = () => options = services.AddOptions<Settings>("B");
                    options.BindConfiguration("A");
                    replace();
                    options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Allows_validation_in_unconditional_local_initializer() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptions<Settings>()
                        .BindConfiguration("Settings");
                    var validated = options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Does_not_accept_validation_of_a_different_builder() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class FirstSettings { }
            sealed class SecondSettings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var first = services.AddOptions<FirstSettings>()
                        .BindConfiguration("First");
                    var second = services.AddOptions<SecondSettings>();
                    second.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_inside_conditional_branch() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services, bool validate)
                {
                    var options = services.AddOptions<Settings>()
                        .BindConfiguration("Settings");
                    if (validate)
                    {
                        options.ValidateOnStart();
                    }
                }
            }
            """);

    [Fact]
    public Task Reports_validation_deferred_in_lambda() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptions<Settings>()
                        .BindConfiguration("Settings");
                    Action validate = () => options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_deferred_in_invoked_method_argument() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptions<Settings>()
                        .BindConfiguration("Settings");
                    Register(() => options.ValidateOnStart());
                }

                void Register(Action callback) { }
            }
            """);

    [Fact]
    public Task Reports_binding_deferred_in_lambda_despite_outer_validation() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    Register(() => services.AddOptions<Settings>()
                            .BindConfiguration("Settings"))
                        .ValidateOnStart();
                }

                OptionsBuilder<Settings> Register(
                    Func<OptionsBuilder<Settings>> factory) => factory();
            }
            """);

    [Fact]
    public Task Reports_validation_deferred_in_local_function() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var options = services.AddOptions<Settings>()
                        .BindConfiguration("Settings");
                    void Validate() => options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_after_possible_early_exit() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services, bool skip)
                {
                    var options = services.AddOptions<Settings>()
                        .BindConfiguration("Settings");
                    if (skip) return;
                    options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_of_different_named_options_builder() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    services.AddOptions<Settings>("A").BindConfiguration("A");
                    services.AddOptions<Settings>("B").ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Allows_validation_of_same_named_options_builder() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    services.AddOptions<Settings>("A").BindConfiguration("A");
                    services.AddOptions<Settings>("A").ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_on_same_property_from_different_receiver() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Holder
            {
                internal Holder(IServiceCollection services) => Services = services;
                internal IServiceCollection Services { get; }
            }
            sealed class Sample
            {
                void Configure(Holder first, Holder second)
                {
                    first.Services.AddOptions<Settings>("A").BindConfiguration("A");
                    second.Services.AddOptions<Settings>("A").ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_after_option_name_local_changes() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var name = "A";
                    services.AddOptions<Settings>(name).BindConfiguration("A");
                    name = "B";
                    services.AddOptions<Settings>(name).ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_after_service_property_changes() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Holder
            {
                internal IServiceCollection Services { get; set; } = default!;
            }
            sealed class Sample
            {
                void Configure(
                    Holder holder,
                    IServiceCollection replacement)
                {
                    holder.Services.AddOptions<Settings>("A").BindConfiguration("A");
                    holder.Services = replacement;
                    holder.Services.AddOptions<Settings>("A").ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_after_option_name_property_changes() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Holder
            {
                internal string Name { get; set; } = "A";
            }
            sealed class Sample
            {
                void Configure(IServiceCollection services, Holder holder)
                {
                    services.AddOptions<Settings>(holder.Name).BindConfiguration("A");
                    holder.Name = "B";
                    services.AddOptions<Settings>(holder.Name).ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Reports_validation_for_separate_service_method_calls() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                IServiceCollection GetServices(int index) => default!;

                void Configure()
                {
                    GetServices(0).AddOptions<Settings>("A").BindConfiguration("A");
                    GetServices(1).AddOptions<Settings>("A").ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Allows_validation_with_unchanged_option_name_local() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services)
                {
                    var name = "A";
                    services.AddOptions<Settings>(name).BindConfiguration("A");
                    services.AddOptions<Settings>(name).ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Allows_validation_with_unchanged_option_name_property() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Holder
            {
                internal string Name { get; } = "A";
            }
            sealed class Sample
            {
                void Configure(IServiceCollection services, Holder holder)
                {
                    services.AddOptions<Settings>(holder.Name).BindConfiguration("A");
                    services.AddOptions<Settings>(holder.Name).ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Allows_validation_on_same_property_and_receiver() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                internal Sample(IServiceCollection services) => Services = services;
                private IServiceCollection Services { get; }

                void Configure()
                {
                    this.Services.AddOptions<Settings>("A").BindConfiguration("A");
                    this.Services.AddOptions<Settings>("A").ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Allows_validation_on_same_implicit_property_receiver() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                internal Sample(IServiceCollection services) => Services = services;
                private IServiceCollection Services { get; }

                void Configure()
                {
                    Services.AddOptions<Settings>("A").BindConfiguration("A");
                    Services.AddOptions<Settings>("A").ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Allows_existing_builder_validated_after_binding() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            sealed class Settings { }
            sealed class Sample
            {
                void Configure(IServiceCollection services, IConfiguration configuration)
                {
                    var options = services.AddOptions<Settings>();
                    options.Bind(configuration);
                    options.ValidateOnStart();
                }
            }
            """);

    [Fact]
    public Task Ignores_custom_Bind_lookalike() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            namespace Custom
            {
                sealed class Builder { }
                static class Extensions
                {
                    public static Builder Bind(this Builder builder) => builder;
                }
                sealed class Sample
                {
                    void Configure(Builder builder) => builder.Bind();
                }
            }
            """);

    [Fact]
    public Task Ignores_Bind_on_an_unrelated_extension_type_in_Microsoft_namespace() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2009",
            """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Options;
            namespace Microsoft.Extensions.DependencyInjection
            {
                static class CustomBindingExtensions
                {
                    public static OptionsBuilder<T> Bind<T>(
                        this OptionsBuilder<T> builder,
                        int value) where T : class => builder;
                }

                sealed class Settings { }
                sealed class Sample
                {
                    void Configure(IServiceCollection services)
                    {
                        services.AddOptions<Settings>().Bind(42);
                    }
                }
            }
            """);

    [Fact]
    public Task Reports_dropped_EventCallback_task() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2010",
            """
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                void Notify(EventCallback changed)
                {
                    changed.InvokeAsync();
                }
            }
            """);

    [Fact]
    public Task Reports_dropped_generic_EventCallback_task() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2010",
            """
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                void Notify(EventCallback<int> changed)
                {
                    changed.InvokeAsync(42);
                }
            }
            """);

    [Fact]
    public Task Reports_explicitly_discarded_EventCallback_task() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2010",
            """
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                void Notify(EventCallback changed)
                {
                    _ = changed.InvokeAsync();
                }
            }
            """);

    [Fact]
    public Task Reports_conditionally_accessed_EventCallback_task() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS2010",
            """
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                void Notify(EventCallback? changed)
                {
                    changed?.InvokeAsync();
                }
            }
            """);

    [Fact]
    public Task Reports_EventCallback_task_in_generated_component_code() =>
        AnalyzerTestHost.AssertHasDiagnosticInGeneratedCodeAsync(
            "DAS2010",
            """
            using Microsoft.AspNetCore.Components;
            sealed class GeneratedComponent
            {
                void Notify(EventCallback changed)
                {
                    changed.InvokeAsync();
                }
            }
            """);

    [Fact]
    public Task Ignores_EventCallback_task_in_non_Razor_generated_code() =>
        AnalyzerTestHost.AssertNoDiagnosticInRazorAnalyzerGeneratedCodeAsync(
            "DAS2010",
            """
            using Microsoft.AspNetCore.Components;
            sealed class GeneratedComponent
            {
                void Notify(EventCallback changed)
                {
                    changed.InvokeAsync();
                }
            }
            """);

    [Fact]
    public async Task Reports_one_EventCallback_diagnostic_for_handwritten_mapped_source()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            #line 1 "Fake.razor"
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                void Notify(EventCallback changed)
                {
                    changed.InvokeAsync();
                }
            }
            #line default
            """,
            "HandWritten.cs",
            new BlazorAnalyzer(),
            new RazorGeneratedBlazorAnalyzer());

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == "DAS2010");
    }

    [Fact]
    public Task Generated_code_analysis_remains_scoped_to_Blazor_rule() =>
        AnalyzerTestHost.AssertNoDiagnosticInGeneratedCodeAsync(
            "DAS1001",
            """
            using System.Threading.Tasks;
            sealed class GeneratedSample
            {
                int Read(Task<int> task) => task.Result;
            }
            """);

    [Fact]
    public Task Allows_assignment_to_a_local_named_underscore() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2010",
            """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                void Notify(EventCallback changed)
                {
                    Task _;
                    _ = changed.InvokeAsync();
                }
            }
            """);

    [Fact]
    public Task Allows_awaited_EventCallback_task() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2010",
            """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                async Task NotifyAsync(EventCallback changed)
                {
                    await changed.InvokeAsync();
                }
            }
            """);

    [Fact]
    public Task Allows_returned_EventCallback_task() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2010",
            """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                Task NotifyAsync(EventCallback changed) => changed.InvokeAsync();
            }
            """);

    [Fact]
    public Task Allows_EventCallback_task_returned_from_lambda() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2010",
            """
            using System;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                Func<Task> CreateHandler(EventCallback<int> changed) =>
                    () => changed.InvokeAsync(42);
            }
            """);

    [Fact]
    public Task Allows_stored_EventCallback_task() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2010",
            """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                Task Notify(EventCallback changed)
                {
                    Task pending = changed.InvokeAsync();
                    return pending;
                }
            }
            """);

    [Fact]
    public Task Allows_composed_EventCallback_task() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2010",
            """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Components;
            sealed class Sample
            {
                Task NotifyAsync(EventCallback changed) =>
                    Task.WhenAll(changed.InvokeAsync());
            }
            """);

    [Fact]
    public Task Ignores_custom_InvokeAsync_lookalike() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2010",
            """
            using System.Threading.Tasks;
            namespace Custom
            {
                sealed class Callback
                {
                    public Task InvokeAsync() => Task.CompletedTask;
                }

                sealed class Sample
                {
                    void Notify(Callback changed)
                    {
                        changed.InvokeAsync();
                    }
                }
            }
            """);

    [Fact]
    public Task Ignores_custom_EventCallback_type_lookalike() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS2010",
            """
            using System.Threading.Tasks;
            namespace Custom
            {
                readonly struct EventCallback
                {
                    public Task InvokeAsync() => Task.CompletedTask;
                }

                sealed class Sample
                {
                    void Notify(EventCallback changed)
                    {
                        changed.InvokeAsync();
                    }
                }
            }
            """);
}
