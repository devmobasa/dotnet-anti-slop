using System.Threading.Channels;

namespace SampleApi;

internal interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(
        RefreshOrderCommand command,
        CancellationToken cancellationToken);

    IAsyncEnumerable<RefreshOrderCommand> ReadAllAsync(
        CancellationToken cancellationToken);
}

internal sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<RefreshOrderCommand> _channel =
        Channel.CreateBounded<RefreshOrderCommand>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

    public ValueTask EnqueueAsync(
        RefreshOrderCommand command,
        CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(command, cancellationToken);

    public IAsyncEnumerable<RefreshOrderCommand> ReadAllAsync(
        CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

internal sealed class QueuedWorker(
    IBackgroundTaskQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<QueuedWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await foreach (var command in queue
            .ReadAllAsync(stoppingToken))
        {
            await using var scope =
                scopeFactory.CreateAsyncScope();

            var handler = scope.ServiceProvider
                .GetRequiredService<RefreshOrderHandler>();

            try
            {
                await handler.HandleAsync(
                    command,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Order refresh failed for {OrderId}",
                    command.OrderId);
            }
        }
    }
}
