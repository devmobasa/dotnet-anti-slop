using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace SampleApi;

internal static class OrdersEndpoints
{
    private const int MaximumPageSize = 100;

    internal static IEndpointRouteBuilder MapOrders(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/orders",
            GetOrdersAsync);

        endpoints.MapPost(
            "/orders/{id:int}/refresh",
            QueueRefreshAsync);

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<OrderSummary>>> GetOrdersAsync(
        int? afterId,
        int? pageSize,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(pageSize ?? 50, 1, MaximumPageSize);

        var query = db.Orders
            .AsNoTracking()
            .OrderBy(order => order.Id);

        if (afterId is { } cursor)
        {
            query = query
                .Where(order => order.Id > cursor)
                .OrderBy(order => order.Id);
        }

        var result = await query
            .Take(take)
            .Select(order => new OrderSummary(
                order.Id,
                order.Number,
                order.Total))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<OrderSummary>>(result);
    }

    private static async Task<Accepted> QueueRefreshAsync(
        int id,
        IBackgroundTaskQueue queue,
        CancellationToken cancellationToken)
    {
        await queue.EnqueueAsync(
            new RefreshOrderCommand(id),
            cancellationToken);

        return TypedResults.Accepted($"/operations/orders/{id}");
    }
}

internal sealed record OrderSummary(
    int Id,
    string Number,
    decimal Total);

internal sealed record RefreshOrderCommand(int OrderId);
