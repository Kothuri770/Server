using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Services;

public class LicenseValidationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LicenseValidationBackgroundService> _logger;

    public LicenseValidationBackgroundService(IServiceProvider serviceProvider, ILogger<LicenseValidationBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("License Validation Background Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var licenseService = scope.ServiceProvider.GetRequiredService<ILicenseService>();

                var status = await licenseService.ValidateLicenseAsync();
                
                if (status.IsValid)
                {
                    await licenseService.UpdateLastCheckedAsync();

                    if (status.DaysRemaining.HasValue)
                    {
                        if (status.DaysRemaining.Value <= 1)
                            _logger.LogWarning("License expires in less than a day!");
                        else if (status.DaysRemaining.Value <= 7)
                            _logger.LogWarning($"License expires in {status.DaysRemaining.Value} days.");
                        else if (status.DaysRemaining.Value == 30)
                            _logger.LogWarning("License expires in 30 days.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in License Validation Background Service");
            }

            // Run every 5 minutes
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
