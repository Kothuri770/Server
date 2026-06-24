using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Server.Repositories;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Services
{
    public class RecoverStaleLocksService : BackgroundService
    {
        private readonly ILogger<RecoverStaleLocksService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _interval;

        public RecoverStaleLocksService(
            ILogger<RecoverStaleLocksService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            
            var intervalMinutes = configuration.GetValue<int>("BackgroundServices:LockRecoveryIntervalMinutes", 5);
            _interval = TimeSpan.FromMinutes(intervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Recover Stale Locks Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RecoverLocks();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while recovering stale locks.");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Recover Stale Locks Service is stopping.");
        }

        private async Task RecoverLocks()
        {
            using var scope = _serviceProvider.CreateScope();
            var batchRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            
            var timeoutMinutes = configuration.GetValue<int>("BackgroundServices:LockTimeoutMinutes", 60);
            
            var recoveredCount = await batchRepository.RecoverStaleLocksAsync(timeoutMinutes);
            
            if (recoveredCount > 0)
            {
                _logger.LogInformation("Recovered {Count} stale batch locks.", recoveredCount);
            }
        }
    }
}
