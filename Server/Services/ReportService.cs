using System;
using System.Threading.Tasks;
using Server.Models;
using Server.Repositories;

namespace Server.Services
{
    public class ReportService : IReportService
    {
        private readonly IReportRepository _reportRepository;

        public ReportService(IReportRepository reportRepository)
        {
            _reportRepository = reportRepository;
        }

        public async Task<IEnumerable<BatchReportDto>> GetApplicationBatchReportAsync(int applicationId, DateTime startDate, DateTime endDate)
        {
            if (applicationId <= 0)
                throw new ArgumentException("Invalid Application ID", nameof(applicationId));

            if (startDate > endDate)
                throw new ArgumentException("Start date cannot be after end date");

            return await _reportRepository.GetApplicationBatchReportAsync(applicationId, startDate, endDate);
        }
    }
}
