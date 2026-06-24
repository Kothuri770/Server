using Microsoft.AspNetCore.Mvc;
using Renci.SshNet;
using Server.Models;
using Server.Repositories;
using Server.Services;
using System.Security.Claims;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SftpController : ControllerBase
    {
        private readonly ISftpRepository _repository;
        private readonly ISftpPollingManager _pollingManager;
 
        public SftpController(ISftpRepository repository, ISftpPollingManager pollingManager)
        {
            _repository = repository;
            _pollingManager = pollingManager;
        }

        [HttpGet("config")]
        public async Task<ActionResult<SftpConfiguration>> GetConfig()
        {
            var config = await _repository.GetConfigurationAsync();
            if (config == null) return Ok(new SftpConfiguration());
            return Ok(config);
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveConfig([FromBody] SftpConfiguration config)
        {
            var username = User.Identity?.Name ?? "admin";
            config.CreatedBy = username;
            
            await _repository.SaveConfigurationAsync(config);
            _pollingManager.NotifyConfigurationChanged(config.IsEnabled);
            
            return Ok();
        }

        [HttpPost("test")]
        public IActionResult TestConnection([FromBody] SftpConfiguration config)
        {
            try
            {
                using (var client = new SftpClient(config.Host, config.Port, config.Username, config.Password))
                {
                    client.Connect();
                    if (client.IsConnected)
                    {
                        client.ListDirectory(config.RemotePath);
                        client.Disconnect();
                        return Ok(new { success = true, message = "Connection successful!" });
                    }
                    return BadRequest(new { success = false, message = "Failed to connect to SFTP server." });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"Error: {ex.Message}" });
            }
        }
    }
}
