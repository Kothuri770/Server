using Dapper;
using Server.Models;
using System.Data;

namespace Server.Repositories
{
    public interface ISftpRepository
    {
        Task<SftpConfiguration?> GetConfigurationAsync();
        Task SaveConfigurationAsync(SftpConfiguration config);
        Task UpdateLastCheckedAsync(int id, DateTime lastChecked);
    }

    public class SftpRepository : BaseRepository, ISftpRepository
    {
        public SftpRepository(string connectionString, string provider) : base(connectionString, provider)
        {
        }
 
        public async Task<SftpConfiguration?> GetConfigurationAsync()
        {
            using (var conn = CreateConnection())
            {
                var query = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? "SELECT TOP 1 * FROM SFTPConfigurations ORDER BY Id ASC" 
                    : "SELECT * FROM SFTPConfigurations ORDER BY Id ASC LIMIT 1";
                return await conn.QueryFirstOrDefaultAsync<SftpConfiguration>(query);
            }
        }
 
        public async Task SaveConfigurationAsync(SftpConfiguration config)
        {
            using (var conn = CreateConnection())
            {
                var existing = await GetConfigurationAsync();
 
                if (existing == null)
                {
                    var sql = @"
                    INSERT INTO SFTPConfigurations (AppId, Host, Port, Username, Password, RemotePath, BackupPath, IsEnabled, CreatedBy, CreatedOn)
                    VALUES (@AppId, @Host, @Port, @Username, @Password, @RemotePath, @BackupPath, @IsEnabled, @CreatedBy, @CreatedOn)";
                    
                    await conn.ExecuteAsync(sql, new {
                        config.AppId,
                        config.Host,
                        config.Port,
                        config.Username,
                        config.Password,
                        config.RemotePath,
                        config.BackupPath,
                        config.IsEnabled,
                        config.CreatedBy,
                        CreatedOn = DateTime.UtcNow
                    });
                }
                else
                {
                    var sql = @"
                    UPDATE SFTPConfigurations 
                    SET AppId = @AppId, Host = @Host, Port = @Port, Username = @Username, 
                        Password = @Password, RemotePath = @RemotePath, BackupPath = @BackupPath, 
                        IsEnabled = @IsEnabled, CreatedBy = @CreatedBy, CreatedOn = @CreatedOn
                    WHERE Id = @Id";
 
                    await conn.ExecuteAsync(sql, new {
                        config.AppId,
                        config.Host,
                        config.Port,
                        config.Username,
                        config.Password,
                        config.RemotePath,
                        config.BackupPath,
                        config.IsEnabled,
                        config.CreatedBy,
                        CreatedOn = DateTime.UtcNow,
                        existing.Id
                    });
                }
            }
        }
 
        public async Task UpdateLastCheckedAsync(int id, DateTime lastChecked)
        {
            using (var conn = CreateConnection())
            {
                await conn.ExecuteAsync("UPDATE SFTPConfigurations SET LastChecked = @lastChecked WHERE Id = @id", new { id, lastChecked });
            }
        }
    }
}
