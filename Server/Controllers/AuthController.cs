using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Server.Models;
using Server.Repositories;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IUserRepository _userRepo;
        private readonly IConfiguration _config;
        private readonly Server.Services.KeycloakAuthService _keycloakAuthService;
        private readonly Server.Services.ILicenseService _licenseService;

        public AuthController(IUserRepository userRepo, IConfiguration config, Server.Services.KeycloakAuthService keycloakAuthService, Server.Services.ILicenseService licenseService)
        {
            _userRepo = userRepo;
            _config = config;
            _keycloakAuthService = keycloakAuthService;
            _licenseService = licenseService;
        }

        [HttpPost("keycloak-login")]
        public async Task<IActionResult> KeycloakLogin([FromBody] KeycloakLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest("Username and password are required.");
            }

            try
            {
                var token = await _keycloakAuthService.AuthenticateAndGetTokenAsync(request.UserName, request.Password);
                
                if (!string.IsNullOrEmpty(token))
                {
                    // Return the raw token string as expected by the Client 
                    // (Matching the previous Client-side KeycloakTokenResponse structure by returning just the token object)
                    return Ok(new { access_token = token });
                }

                return Unauthorized("Invalid login response from Keycloak.");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _userRepo.ValidateCredentialsAsync(request.UserName, request.Password);
            if (user == null) return Unauthorized("Invalid credentials");
            if (!user.IsEnabled) return Unauthorized("Account is disabled. Please contact your administrator.");
            var token = GenerateJwtToken(user);
            var licenseStatus = await _licenseService.ValidateLicenseAsync();
            return Ok(new LoginResponse
            {
                Token = token,
                UserName = user.UserName,
                UserType = user.UserType,
                Expires = DateTime.UtcNow.AddHours(8),
                LicenseStatus = licenseStatus.Status,
                LicenseDaysRemaining = licenseStatus.DaysRemaining,
                LicenseMessage = licenseStatus.Message
            });
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userName = User.Identity!.Name!;
            var success = await _userRepo.ChangePasswordAsync(userName, request.OldPassword, request.NewPassword);
            return success ? Ok() : BadRequest("Password change failed");
        }

        private string GenerateJwtToken(UserDto user)
        {
            var claims = new[]
            {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(ClaimTypes.Role, user.UserType),
            new Claim("ViewLimit", user.ViewLimit ?? string.Empty)
        };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
