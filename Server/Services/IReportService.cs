using System;
using System.Threading.Tasks;
using Server.Models;

namespace Server.Services
{
    public interface IReportService
    {
        Task<IEnumerable<BatchReportDto>> GetApplicationBatchReportAsync(int applicationId, DateTime startDate, DateTime endDate);
    }
}
