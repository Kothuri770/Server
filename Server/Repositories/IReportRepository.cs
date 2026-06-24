using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Server.Models;

namespace Server.Repositories
{
    public interface IReportRepository
    {
        Task<IEnumerable<BatchReportDto>> GetApplicationBatchReportAsync(int applicationId, DateTime startDate, DateTime endDate);
    }
}
