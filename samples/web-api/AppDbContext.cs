using Microsoft.EntityFrameworkCore;

namespace SampleApi;

internal sealed class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    internal DbSet<Order> Orders => Set<Order>();
}

internal sealed class Order
{
    internal int Id { get; set; }

    internal string Number { get; set; } = string.Empty;

    internal decimal Total { get; set; }
}
