using Dapper;
using Server.Models;

namespace Server.Repositories
{
    public interface ITestRepository
    {
        Task<bool> TestDatabaseConnectionAsync();
        Task<int> GetBatchCountAsync();
        Task<IEnumerable<dynamic>> GetRawBatchDataAsync();
    }

    public class TestRepository : BaseRepository, ITestRepository
    {
        public TestRepository(string connectionString, string provider) : base(connectionString, provider) { }

        public async Task<bool> TestDatabaseConnectionAsync()
        {
            try
            {
                using var conn = CreateConnection();
                await conn.ExecuteAsync("SELECT 1");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<int> GetBatchCountAsync()
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleAsync<int>("SELECT COUNT(*) FROM Batch");
        }

        public async Task<IEnumerable<dynamic>> GetRawBatchDataAsync()
        {
            using var conn = CreateConnection();
            
            var batchCount = await conn.QuerySingleAsync<int>("SELECT COUNT(*) FROM Batch");
            var viewCount = await conn.QuerySingleAsync<int>("SELECT COUNT(*) FROM JobMonitorReportQuery");
            
            if (viewCount > 0)
            {
                var topSql = _provider == "SqlServer" 
                    ? "SELECT TOP 5 * FROM JobMonitorReportQuery"
                    : "SELECT * FROM JobMonitorReportQuery LIMIT 5";
                return await conn.QueryAsync(topSql);
            }
            
            return new List<dynamic>();
        }
    }
}