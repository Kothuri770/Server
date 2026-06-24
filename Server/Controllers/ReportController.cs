using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Server.Services;
using Server.Models;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportController : ControllerBase
    {
        private readonly IReportService _reportService;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

        public ReportController(IReportService reportService, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _reportService = reportService;
            _config = config;
        }

        [HttpGet("debug-monitor")]
        [AllowAnonymous]
        public async Task<IActionResult> DebugMonitor()
        {
            var connStr = _config.GetConnectionString("TrueCaptureDb");
            var provider = _config.GetSection("ConnectionStrings")["DatabaseProvider"] ?? "PostgreSql";
            
            using System.Data.IDbConnection conn = provider == "SqlServer" 
                ? new Microsoft.Data.SqlClient.SqlConnection(connStr) 
                : new Npgsql.NpgsqlConnection(connStr);
                
            var sql = provider == "SqlServer" 
                ? "SELECT TOP 5 * FROM JobMonitorReportQuery" 
                : "SELECT * FROM JobMonitorReportQuery LIMIT 5";
                
            var data = await Dapper.SqlMapper.QueryAsync(conn, sql);
            return Ok(data);
        }

        [HttpGet("reconciliation")]
        public async Task<ActionResult<IEnumerable<BatchReportDto>>> GetApplicationBatchReport(
            [FromQuery] int applicationId, 
            [FromQuery] DateTime startDate, 
            [FromQuery] DateTime endDate)
        {
            try
            {
                var report = await _reportService.GetApplicationBatchReportAsync(applicationId, startDate, endDate);
                return Ok(report);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // In production, log the exception.
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
