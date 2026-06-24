using System.Text.Json;

namespace Server.Services
{
    public class KeycloakAuthService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _serverUrl;
        private readonly string _realm;
        private readonly string _clientId;
        private readonly string _clientSecret;

        public KeycloakAuthService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _serverUrl = configuration["Keycloak:ServerUrl"]?.TrimEnd('/') ?? string.Empty;
            _realm = configuration["Keycloak:Realm"] ?? string.Empty;
            _clientId = configuration["Keycloak:ClientId"] ?? string.Empty;
            _clientSecret = configuration["Keycloak:ClientSecret"] ?? string.Empty;
        }

        public async Task<string?> AuthenticateAndGetTokenAsync(string username, string password)
        {
            if (string.IsNullOrEmpty(_serverUrl) || string.IsNullOrEmpty(_realm) || string.IsNullOrEmpty(_clientId))
            {
                throw new Exception("Keycloak configuration is missing on the server.");
            }

            var tokenEndpoint = $"{_serverUrl}/realms/{_realm}/protocol/openid-connect/token";

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret),
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password),
                new KeyValuePair<string, string>("scope", "openid")
            });

            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.PostAsync(tokenEndpoint, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(responseBody))
                    {
                        if (doc.RootElement.TryGetProperty("access_token", out JsonElement tokenElement))
                        {
                            return tokenElement.GetString();
                        }
                    }
                }
                else
                {
                    var errorResponse = await response.Content.ReadAsStringAsync();
                    string errorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    
                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(errorResponse))
                        {
                            if (doc.RootElement.TryGetProperty("error_description", out JsonElement desc))
                                errorMessage += $": {desc.GetString()}";
                            else if (doc.RootElement.TryGetProperty("error", out JsonElement err))
                                errorMessage += $": {err.GetString()}";
                        }
                    }
                    catch { /* Fallback to raw response if JSON parse fails */ }

                    throw new Exception(errorMessage);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Keycloak Authentication error: {ex.Message}");
            }

            return null;
        }
    }
}
