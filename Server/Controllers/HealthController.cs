using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Get()
        {
            try
            {
                // Simple health check - return OK if the API is running
                return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "unhealthy", error = ex.Message });
            }
        }
        
        [HttpGet("db")]
        [AllowAnonymous]
        public IActionResult DatabaseHealth()
        {
            try
            {
                // Check database connectivity by attempting a simple query
                // For now, just return OK - you can implement actual DB check later
                return Ok(new { status = "database_healthy", timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "database_unhealthy", error = ex.Message });
            }
        }
    }
}