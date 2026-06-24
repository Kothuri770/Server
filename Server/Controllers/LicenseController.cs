using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;
using System.Threading.Tasks;

namespace Server.Controllers;

[Route("api/[controller]")]
[ApiController]
public class LicenseController : ControllerBase
{
    private readonly ILicenseService _licenseService;

    public LicenseController(ILicenseService licenseService)
    {
        _licenseService = licenseService;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<LicenseStatusDto>> GetStatus()
    {
        var status = await _licenseService.ValidateLicenseAsync();
        return Ok(status);
    }

    [HttpPost("activate")]
    [Authorize(Roles = "admin,configeditor")]
    public async Task<ActionResult<LicenseStatusDto>> Activate([FromBody] ActivateLicenseRequest request)
    {
        if (string.IsNullOrEmpty(request.LicenseKey))
        {
            return BadRequest("License key is required.");
        }

        var status = await _licenseService.ActivateLicenseAsync(request.LicenseKey);
        
        if (!status.IsValid)
        {
            return BadRequest(status);
        }
        
        return Ok(status);
    }

    [HttpGet("installation-id")]
    [Authorize(Roles = "admin,configeditor")]
    public async Task<ActionResult> GetInstallationId()
    {
        var id = await _licenseService.GetInstallationIdAsync();
        if (string.IsNullOrEmpty(id))
        {
            return NotFound("Installation ID not found.");
        }
        return Ok(new { InstallationId = id });
    }

    [HttpPost("deactivate")]
    [Authorize(Roles = "admin,configeditor")]
    public async Task<ActionResult> Deactivate()
    {
        var result = await _licenseService.DeactivateLicenseAsync();
        if (result)
        {
            return Ok(new { Message = "License deactivated successfully." });
        }
        return StatusCode(500, "Error deactivating license.");
    }
}
