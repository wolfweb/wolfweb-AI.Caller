namespace AI.Caller.Phone.Services;

public class QueuedHostedService : BackgroundService {
    private readonly ILogger<QueuedHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;
    public IBackgroundTaskQueue TaskQueue { get; }

    public QueuedHostedService(IBackgroundTaskQueue taskQueue, ILogger<QueuedHostedService> logger, IServiceProvider serviceProvider) {
        TaskQueue = taskQueue;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        _logger.LogInformation("Queued Hosted Service is running.");

        await BackgroundProcessing(stoppingToken);
    }

    private async Task BackgroundProcessing(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            var workItem = await TaskQueue.DequeueAsync(stoppingToken);

            try {
                using (var scope = _serviceProvider.CreateScope()) {
                    await workItem(stoppingToken, scope.ServiceProvider);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error occurred executing a background work item.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken) {
        _logger.LogInformation("Queued Hosted Service is stopping.");
        await base.StopAsync(stoppingToken);
    }
}