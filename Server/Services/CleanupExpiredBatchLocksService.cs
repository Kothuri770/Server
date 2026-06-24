using Microsoft.Extensions.Hosting;
using Server.Services;

namespace Server.Services;

public class CleanupExpiredBatchLocksService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CleanupExpiredBatchLocksService> _logger;

    public CleanupExpiredBatchLocksService(IServiceProvider serviceProvider, ILogger<CleanupExpiredBatchLocksService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting batch lock cleanup service");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5)); // Run every 5 minutes

        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CleanupExpiredLocks();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the service is stopped
        }

        _logger.LogInformation("Batch lock cleanup service stopped");
    }

    private async Task CleanupExpiredLocks()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var batchLockService = scope.ServiceProvider.GetRequiredService<IBatchLockService>();
            
            await batchLockService.CleanupExpiredLocksAsync();
            
            _logger.LogInformation("Completed batch lock cleanup cycle");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during batch lock cleanup");
        }
    }
}