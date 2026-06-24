using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Server.Services
{
    /// <summary>
    /// Sends a periodic internal HTTP ping to the /health endpoint so that IIS
    /// never considers the worker process (w3wp.exe) idle and terminates it.
    ///
    /// Root cause addressed: WAS Event 5186 (Idle Timeout fires after 20 minutes
    /// of no HTTP traffic) kills w3wp.exe and stops all background services with it.
    /// This service creates synthetic activity every <see cref="_intervalMinutes"/>
    /// minutes to keep the IIS app pool alive indefinitely.
    /// </summary>
    public class KeepAliveService : BackgroundService
    {
        private readonly ILogger<KeepAliveService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public KeepAliveService(
            ILogger<KeepAliveService> logger,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalMinutes = _configuration.GetValue<int>("KeepAlive:IntervalMinutes", 10);
            var baseUrl = _configuration.GetValue<string>("KeepAlive:BaseUrl", "http://localhost");

            _logger.LogInformation(
                "KeepAlive Service started — pinging {BaseUrl}/health every {Interval} min to prevent IIS idle timeout.",
                baseUrl, intervalMinutes);

            // Using IHttpClientFactory to avoid socket exhaustion
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(baseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Initial delay to allow the application to fully start before first ping
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var response = await httpClient.GetAsync("/health", stoppingToken);

                    if (response.IsSuccessStatusCode)
                        _logger.LogDebug("KeepAlive ping OK ({StatusCode})", (int)response.StatusCode);
                    else
                        _logger.LogWarning("KeepAlive ping returned non-success: {StatusCode}", (int)response.StatusCode);
                }
                catch (OperationCanceledException)
                {
                    // Clean shutdown
                    break;
                }
                catch (HttpRequestException httpEx)
                {
                    // Non-fatal — the API may still be starting up or briefly unavailable
                    _logger.LogWarning(httpEx, "KeepAlive HTTP ping failed. Retrying in {Interval} min.", intervalMinutes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in KeepAlive Service. Continuing.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("KeepAlive Service stopped.");
        }
    }
}
