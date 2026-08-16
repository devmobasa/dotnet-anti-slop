namespace DotNetAntiSlop.Analyzers.Tests;

internal static class FrameworkStubs
{
    internal const string All = """
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FactAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TheoryAttribute : Attribute { }
}

namespace Microsoft.VisualStudio.TestTools.UnitTesting
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestMethodAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DataTestMethodAttribute : Attribute { }
}

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestCaseAttribute : Attribute { }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
        public virtual Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    public sealed class DatabaseFacade { }

    public class DbSet<T> : IQueryable<T>
    {
        public Type ElementType => typeof(T);
        public Expression Expression => Array.Empty<T>().AsQueryable().Expression;
        public IQueryProvider Provider => Array.Empty<T>().AsQueryable().Provider;

        public IEnumerator<T> GetEnumerator() =>
            ((IEnumerable<T>)Array.Empty<T>()).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public ValueTask<T?> FindAsync(params object?[] keyValues) =>
            ValueTask.FromResult(default(T));

        public ValueTask<T?> FindAsync(
            object?[] keyValues,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(default(T));
    }

    public static class EntityFrameworkQueryableExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsNoTrackingWithIdentityResolution<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> AsTracking<T>(this IQueryable<T> source) => source;
        public static IQueryable<T> Include<T, TProperty>(
            this IQueryable<T> source,
            Expression<Func<T, TProperty>> path) => source;

        public static Task<List<T>> ToListAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<T>());

        public static Task<T[]> ToArrayAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Array.Empty<T>());

        public static Task<bool> AnyAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public static Task<int> CountAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public static Task<T> FirstAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T)!);

        public static Task<T?> FirstOrDefaultAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T));

        public static Task<T> SingleAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T)!);

        public static Task<T> SingleAsync<T>(
            this IQueryable<T> source,
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T)!);

        public static Task<T?> SingleOrDefaultAsync<T>(
            this IQueryable<T> source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(default(T));
    }

    public static class RelationalQueryableExtensions
    {
        public static IQueryable<T> FromSqlRaw<T>(
            this DbSet<T> source,
            string sql,
            params object[] parameters) => source;

        public static IQueryable<T> FromSqlInterpolated<T>(
            this DbSet<T> source,
            FormattableString sql) => source;
    }

    public static class RelationalDatabaseFacadeExtensions
    {
        public static int ExecuteSqlRaw(
            this DatabaseFacade database,
            string sql,
            params object[] parameters) => 0;

        public static Task<int> ExecuteSqlRawAsync(
            this DatabaseFacade database,
            string sql,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceCollection { }

    public sealed class ServiceCollection : IServiceCollection { }

    public static class ServiceCollectionServiceExtensions
    {
        public static IServiceCollection AddSingleton<TService>(
            this IServiceCollection services) => services;

        public static IServiceCollection AddSingleton<TService, TImplementation>(
            this IServiceCollection services)
            where TImplementation : TService => services;

        public static IServiceCollection AddSingleton<TService>(
            this IServiceCollection services,
            Func<IServiceProvider, TService> factory) => services;

        public static IServiceCollection AddScoped<TService>(
            this IServiceCollection services) => services;

        public static IServiceCollection AddScoped<TService, TImplementation>(
            this IServiceCollection services)
            where TImplementation : TService => services;

        public static IServiceProvider BuildServiceProvider(
            this IServiceCollection services) =>
            new DefaultServiceProvider();

        public static T GetRequiredService<T>(
            this IServiceProvider provider) =>
            default!;

        private sealed class DefaultServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }
    }

    public static class EntityFrameworkServiceCollectionExtensions
    {
        public static IServiceCollection AddDbContext<TContext>(
            this IServiceCollection services)
            where TContext : Microsoft.EntityFrameworkCore.DbContext =>
            services;
    }

    public static class OptionsServiceCollectionExtensions
    {
        public static Microsoft.Extensions.Options.OptionsBuilder<TOptions> AddOptions<TOptions>(
            this IServiceCollection services) where TOptions : class => new();

        public static Microsoft.Extensions.Options.OptionsBuilder<TOptions> AddOptions<TOptions>(
            this IServiceCollection services,
            string name) where TOptions : class => new();

        public static Microsoft.Extensions.Options.OptionsBuilder<TOptions> AddOptionsWithValidateOnStart<TOptions>(
            this IServiceCollection services) where TOptions : class => new();
    }

    public static class OptionsBuilderConfigurationExtensions
    {
        public static Microsoft.Extensions.Options.OptionsBuilder<TOptions> Bind<TOptions>(
            this Microsoft.Extensions.Options.OptionsBuilder<TOptions> builder,
            Microsoft.Extensions.Configuration.IConfiguration configuration) where TOptions : class => builder;

        public static Microsoft.Extensions.Options.OptionsBuilder<TOptions> BindConfiguration<TOptions>(
            this Microsoft.Extensions.Options.OptionsBuilder<TOptions> builder,
            string configSectionPath) where TOptions : class => builder;
    }

    public static class OptionsBuilderExtensions
    {
        public static Microsoft.Extensions.Options.OptionsBuilder<TOptions> Validate<TOptions>(
            this Microsoft.Extensions.Options.OptionsBuilder<TOptions> builder,
            Func<TOptions, bool> validation) where TOptions : class => builder;

        public static Microsoft.Extensions.Options.OptionsBuilder<TOptions> ValidateOnStart<TOptions>(
            this Microsoft.Extensions.Options.OptionsBuilder<TOptions> builder) where TOptions : class => builder;
    }
}

namespace Microsoft.Extensions.Configuration
{
    public interface IConfiguration { }
}

namespace Microsoft.Extensions.Options
{
    public sealed class OptionsBuilder<TOptions> where TOptions : class { }
}

namespace Microsoft.Extensions.Hosting
{
    public interface IHostEnvironment
    {
        string EnvironmentName { get; }
    }

    public static class HostEnvironmentEnvExtensions
    {
        public static bool IsDevelopment(this IHostEnvironment environment) =>
            environment.EnvironmentName == "Development";

        public static bool IsEnvironment(
            this IHostEnvironment environment,
            string environmentName) =>
            environment.EnvironmentName == environmentName;
    }
}

namespace Microsoft.AspNetCore.Http
{
    public sealed class HttpContext
    {
        public CancellationToken RequestAborted { get; set; }
    }

    public interface IHttpContextAccessor
    {
        HttpContext? HttpContext { get; set; }
    }
}

namespace Microsoft.AspNetCore.Components
{
    public readonly struct EventCallback
    {
        public Task InvokeAsync(object? argument = null) => Task.CompletedTask;
    }

    public readonly struct EventCallback<TValue>
    {
        public Task InvokeAsync(TValue argument) => Task.CompletedTask;
    }
}

namespace Microsoft.AspNetCore.Mvc
{
    public abstract class ControllerBase { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpGetAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HttpPostAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RouteAttribute : Attribute
    {
        public RouteAttribute(string template) { }
    }
}

namespace Microsoft.AspNetCore.Builder
{
    using Microsoft.Extensions.Hosting;

    public interface IEndpointRouteBuilder { }

    public sealed class WebApplication : IEndpointRouteBuilder
    {
        public IHostEnvironment Environment { get; set; } = default!;
    }

    public static class EndpointRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapGet<TResult>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Func<Task<TResult>> handler) => endpoints;

        public static IEndpointRouteBuilder MapGet<TResult>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Func<CancellationToken, Task<TResult>> handler) => endpoints;

        public static IEndpointRouteBuilder MapGet<T1, TResult>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Func<T1, Task<TResult>> handler) => endpoints;

        public static IEndpointRouteBuilder MapGet<T1, T2, TResult>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Func<T1, T2, Task<TResult>> handler) => endpoints;

        public static IEndpointRouteBuilder MapPost<T1, TResult>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Func<T1, Task<TResult>> handler) => endpoints;

        public static IEndpointRouteBuilder MapPost<T1, T2, TResult>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            Func<T1, T2, Task<TResult>> handler) => endpoints;

        public static IEndpointRouteBuilder MapMethods<TResult>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            IEnumerable<string> methods,
            Func<Task<TResult>> handler) => endpoints;

        public static IEndpointRouteBuilder MapMethods<T1, TResult>(
            this IEndpointRouteBuilder endpoints,
            string pattern,
            IEnumerable<string> methods,
            Func<T1, Task<TResult>> handler) => endpoints;
    }

    public static class DeveloperExceptionPageExtensions
    {
        public static WebApplication UseDeveloperExceptionPage(
            this WebApplication app) => app;

        public static WebApplication UseMigrationsEndPoint(
            this WebApplication app) => app;
    }

    public static class SwaggerBuilderExtensions
    {
        public static WebApplication UseSwagger(this WebApplication app) => app;
        public static WebApplication UseSwaggerUI(this WebApplication app) => app;
    }
}
""";
}
