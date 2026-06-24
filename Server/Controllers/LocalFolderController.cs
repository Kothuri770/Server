using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Repositories;
using Server.Services;
using System.Security.Claims;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LocalFolderController : ControllerBase
    {
        private readonly ILocalFolderRepository _repository;
        private readonly ILocalFolderPollingManager _pollingManager;

        public LocalFolderController(ILocalFolderRepository repository, ILocalFolderPollingManager pollingManager)
        {
            _repository = repository;
            _pollingManager = pollingManager;
        }

        [HttpGet("get")]
        public async Task<IActionResult> GetConfig()
        {
            try
            {
                var config = await _repository.GetConfigurationAsync();
                return Ok(config ?? new LocalFolderConfiguration());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching configuration", error = ex.Message });
            }
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveConfig([FromBody] LocalFolderConfiguration config)
        {
            try
            {
                var username = User?.FindFirst(ClaimTypes.Name)?.Value ?? "System";
                config.CreatedBy = username;

                await _repository.SaveConfigurationAsync(config);
                
                // Notify the background polling service that configuration changed
                _pollingManager.NotifyConfigurationChanged(config.IsEnabled);

                return Ok(new { message = "Local Folder configuration saved perfectly." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error saving configuration", error = ex.Message });
            }
        }
    }
}
