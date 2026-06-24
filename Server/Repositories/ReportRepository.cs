using Npgsql;
using Dapper;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Server.Models;
using System.Linq;

namespace Server.Repositories
{
    public class ReportRepository : BaseRepository, IReportRepository
    {
        public ReportRepository(string connectionString, string provider) : base(connectionString, provider)
        {
        }

        public async Task<IEnumerable<BatchReportDto>> GetApplicationBatchReportAsync(int applicationId, DateTime startDate, DateTime endDate)
        {
            using var conn = CreateConnection();

            var sql = @"
                SELECT 
                    a.Name as ApplicationName,
                    b.BatchName as BatchName,
                    b.username as UserName,
                    b.CreatedOn as CreatedOn,
                    COALESCE(s.StepName, 'Complete') as Status,
                    (SELECT COUNT(DISTINCT i.DocTypeID) FROM BatchDetail i WHERE i.BatchId = b.ID AND i.Status = 'A') as DocTypeCount,
                    (SELECT COUNT(i.ID) FROM BatchDetail i WHERE i.BatchId = b.ID AND i.Status = 'A') as PageCount
                FROM Batch b
                INNER JOIN ObjectTypes a ON b.BatchTypeId = a.Id
                LEFT JOIN Steps s ON b.StepId = s.Id
                WHERE b.BatchTypeId = @ApplicationId 
                  AND CAST(b.CreatedOn AS DATE) >= @StartDate 
                  AND CAST(b.CreatedOn AS DATE) <= @EndDate
                  AND COALESCE(b.BatchStatus, '') != 'D'
                ORDER BY b.CreatedOn DESC";

            var data = await conn.QueryAsync<BatchReportDto>(sql, new { 
                ApplicationId = applicationId, 
                StartDate = startDate.Date, 
                EndDate = endDate.Date 
            });

            return data;
        }
    }
}
