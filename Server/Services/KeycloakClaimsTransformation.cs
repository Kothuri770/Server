using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Text.Json;

namespace Server.Services
{
    public class KeycloakClaimsTransformation : IClaimsTransformation
    {
        private readonly IConfiguration _configuration;

        public KeycloakClaimsTransformation(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var identity = (ClaimsIdentity?)principal.Identity;

            if (identity == null || !identity.IsAuthenticated)
            {
                return Task.FromResult(principal);
            }

            // Only care about JWTs issued by Keycloak
            var issuer = identity.FindFirst("iss")?.Value;
            var expectedKeycloakUrl = _configuration["Keycloak:ServerUrl"]?.TrimEnd('/') + "/realms/" + _configuration["Keycloak:Realm"];

            if (issuer != null && issuer.StartsWith(expectedKeycloakUrl, StringComparison.OrdinalIgnoreCase))
            {
                // Keycloak roles are nested inside the "resource_access" claim. Example:
                // "resource_access": {
                //   "TrueCapture": { "roles": ["admin", "scanner"] }
                // }

                var resourceAccessClaim = identity.FindFirst("resource_access")?.Value;
                var clientId = _configuration["Keycloak:ClientId"];

                if (!string.IsNullOrEmpty(resourceAccessClaim) && !string.IsNullOrEmpty(clientId))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(resourceAccessClaim);
                        if (doc.RootElement.TryGetProperty(clientId, out JsonElement clientNode) &&
                            clientNode.TryGetProperty("roles", out JsonElement rolesNode) && 
                            rolesNode.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var role in rolesNode.EnumerateArray())
                            {
                                var roleName = role.GetString()?.ToLower();
                                if (!string.IsNullOrEmpty(roleName) && !identity.HasClaim(ClaimTypes.Role, roleName))
                                {
                                    identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore parsing errors - fallback to whatever claims are already present
                    }
                }
            }

            return Task.FromResult(principal);
        }
    }
}
