using Microsoft.Extensions.Hosting;

namespace Server.Services
{
    public class DatabaseInitializationHostedService : IHostedService
    {
        private readonly IDatabaseInitializerService _databaseInitializer;
        private readonly ILogger<DatabaseInitializationHostedService> _logger;

        public DatabaseInitializationHostedService(
            IDatabaseInitializerService databaseInitializer,
            ILogger<DatabaseInitializationHostedService> logger)
        {
            _databaseInitializer = databaseInitializer;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting database initialization...");
            
            try
            {
                await _databaseInitializer.InitializeDatabaseAsync();
                _logger.LogInformation("Database initialization completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during database initialization.");
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Database initialization service stopped.");
            return Task.CompletedTask;
        }
    }
}