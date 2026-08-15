using Xunit;

namespace DotNetAntiSlop.Analyzers.Tests;

public sealed class EfCoreAnalyzerTests
{
    private const string Header = """
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.EntityFrameworkCore;

        sealed class Order
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        sealed class AppDbContext : DbContext
        {
            public DbSet<Order> Orders { get; } = new();
        }
        """;

    private static string Source(string body) => Header + body;

    [Fact]
    public Task Reports_tracking_read_only_entity_query() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3001",
            Source(
                """
                sealed class Repository
                {
                    public async Task<List<Order>> GetOrdersAsync(
                        AppDbContext db,
                        CancellationToken cancellationToken)
                    {
                        return await db.Orders.ToListAsync(cancellationToken);
                    }
                }
                """));

    [Fact]
    public Task As_no_tracking_satisfies_read_only_rule() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS3001",
            Source(
                """
                sealed class Repository
                {
                    public async Task<List<Order>> GetOrdersAsync(
                        AppDbContext db,
                        CancellationToken cancellationToken)
                    {
                        return await db.Orders
                            .AsNoTracking()
                            .ToListAsync(cancellationToken);
                    }
                }
                """));

    [Fact]
    public Task Scalar_projection_does_not_require_no_tracking() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS3001",
            Source(
                """
                sealed class Repository
                {
                    public async Task<List<int>> GetOrderIdsAsync(
                        AppDbContext db,
                        CancellationToken cancellationToken)
                    {
                        return await db.Orders
                            .Select(order => order.Id)
                            .ToListAsync(cancellationToken);
                    }
                }
                """));

    [Fact]
    public Task Reports_tracking_override() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3002",
            Source(
                """
                sealed class Repository
                {
                    public async Task<List<Order>> GetOrdersAsync(
                        AppDbContext db,
                        CancellationToken cancellationToken)
                    {
                        return await db.Orders
                            .AsNoTracking()
                            .AsTracking()
                            .ToListAsync(cancellationToken);
                    }
                }
                """));

    [Fact]
    public Task Reports_missing_query_cancellation() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3003",
            Source(
                """
                sealed class Repository
                {
                    public async Task<List<Order>> GetOrdersAsync(
                        AppDbContext db,
                        CancellationToken cancellationToken)
                    {
                        return await db.Orders.AsNoTracking().ToListAsync();
                    }
                }
                """));

    [Fact]
    public Task Reports_missing_save_cancellation() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3004",
            Source(
                """
                sealed class Repository
                {
                    public async Task SaveAsync(
                        AppDbContext db,
                        CancellationToken cancellationToken)
                    {
                        await db.SaveChangesAsync();
                    }
                }
                """));

    [Fact]
    public Task Reports_query_inside_loop() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3005",
            Source(
                """
                sealed class Repository
                {
                    public async Task LoadAsync(
                        AppDbContext db,
                        IEnumerable<int> ids,
                        CancellationToken cancellationToken)
                    {
                        foreach (var id in ids)
                        {
                            _ = await db.Orders.SingleAsync(
                                order => order.Id == id,
                                cancellationToken);
                        }
                    }
                }
                """));

    [Fact]
    public Task Reports_filtering_after_materialization() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3006",
            Source(
                """
                sealed class Repository
                {
                    public async Task<List<Order>> GetOrdersAsync(
                        AppDbContext db,
                        CancellationToken cancellationToken)
                    {
                        return (await db.Orders
                            .AsNoTracking()
                            .ToListAsync(cancellationToken))
                            .Where(order => order.Id > 0)
                            .ToList();
                    }
                }
                """));

    [Fact]
    public Task Reports_interpolated_string_passed_to_raw_sql() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3007",
            Source(
                """
                sealed class Repository
                {
                    public IQueryable<Order> Query(
                        AppDbContext db,
                        string name)
                    {
                        return db.Orders.FromSqlRaw(
                            $"SELECT * FROM Orders WHERE Name = '{name}'");
                    }
                }
                """));

    [Fact]
    public Task Constant_raw_sql_fragment_is_allowed() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS3007",
            Source(
                """
                sealed class Repository
                {
                    private const string Sql =
                        "SELECT * FROM Orders WHERE IsDeleted = 0";

                    public IQueryable<Order> Query(AppDbContext db)
                    {
                        return db.Orders.FromSqlRaw(Sql);
                    }
                }
                """));

    [Fact]
    public Task Reports_unbounded_get_materialization() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3008",
            Source(
                """
                sealed class Endpoints
                {
                    public void Map(IEndpointRouteBuilder app)
                    {
                        app.MapGet<AppDbContext, CancellationToken, List<Order>>(
                            "/orders",
                            async (db, cancellationToken) =>
                                await db.Orders
                                    .AsNoTracking()
                                    .ToListAsync(cancellationToken));
                    }
                }
                """));

    [Fact]
    public Task Bounded_get_materialization_is_allowed() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS3008",
            Source(
                """
                sealed class Endpoints
                {
                    public void Map(IEndpointRouteBuilder app)
                    {
                        app.MapGet<AppDbContext, CancellationToken, List<Order>>(
                            "/orders",
                            async (db, cancellationToken) =>
                                await db.Orders
                                    .AsNoTracking()
                                    .OrderBy(order => order.Id)
                                    .Take(100)
                                    .ToListAsync(cancellationToken));
                    }
                }
                """));

    [Fact]
    public Task Map_methods_get_is_treated_as_read_only() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3001",
            Source(
                """
                sealed class Endpoints
                {
                    public void Map(IEndpointRouteBuilder app)
                    {
                        app.MapMethods<List<Order>>(
                            "/orders",
                            new[] { "GET", "HEAD" },
                            async () =>
                                await new AppDbContext()
                                    .Orders
                                    .ToListAsync());
                    }
                }
                """));

    [Fact]
    public Task Write_route_is_not_assumed_to_be_read_only() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS3001",
            Source(
                """
                sealed class Endpoints
                {
                    public void Map(IEndpointRouteBuilder app)
                    {
                        app.MapPost<AppDbContext, List<Order>>(
                            "/orders/recalculate",
                            async db =>
                                await db.Orders.ToListAsync());
                    }
                }
                """));

    [Fact]
    public Task Reports_count_async_existence_check() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3009",
            Source(
                """
                sealed class Repository
                {
                    public async Task<bool> HasOrdersAsync(
                        AppDbContext db,
                        CancellationToken cancellationToken)
                    {
                        return await db.Orders
                            .CountAsync(cancellationToken) > 0;
                    }
                }
                """));

    [Fact]
    public Task Reports_parallel_queries_on_same_context() =>
        AnalyzerTestHost.AssertHasDiagnosticAsync(
            "DAS3010",
            Source(
                """
                sealed class Repository
                {
                    public Task LoadAsync(
                        AppDbContext db,
                        CancellationToken cancellationToken)
                    {
                        return Task.WhenAll(
                            db.Orders.ToListAsync(cancellationToken),
                            db.Orders.ToListAsync(cancellationToken));
                    }
                }
                """));

    [Fact]
    public Task Parallel_queries_on_different_contexts_are_not_reported() =>
        AnalyzerTestHost.AssertNoDiagnosticAsync(
            "DAS3010",
            Source(
                """
                sealed class Repository
                {
                    public Task LoadAsync(
                        AppDbContext first,
                        AppDbContext second,
                        CancellationToken cancellationToken)
                    {
                        return Task.WhenAll(
                            first.Orders.ToListAsync(cancellationToken),
                            second.Orders.ToListAsync(cancellationToken));
                    }
                }
                """));
}
