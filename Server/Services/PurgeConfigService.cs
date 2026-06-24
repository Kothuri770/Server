using Server.Models;

namespace Server.Services
{
    public interface IPurgeConfigService
    {
        Task<PurgeConfigDto> GetConfigAsync();
        Task SaveConfigAsync(PurgeConfigDto config);
    }

    public class PurgeConfigService : IPurgeConfigService
    {
        private readonly string _configFilePath;

        public PurgeConfigService()
        {
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PurgingConfig.json");
        }

        public async Task<PurgeConfigDto> GetConfigAsync()
        {
            if (!File.Exists(_configFilePath))
                return new PurgeConfigDto { IsEnabled = false };

            try
            {
                var json = await File.ReadAllTextAsync(_configFilePath);
                return System.Text.Json.JsonSerializer.Deserialize<PurgeConfigDto>(json) ?? new PurgeConfigDto();
            }
            catch
            {
                return new PurgeConfigDto { IsEnabled = false };
            }
        }

        public async Task SaveConfigAsync(PurgeConfigDto config)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_configFilePath, json);
        }
    }
}
