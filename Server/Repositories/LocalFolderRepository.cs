using Dapper;
using Npgsql;
using Server.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Server.Repositories
{
    public class LocalFolderRepository : BaseRepository, ILocalFolderRepository
    {
        public LocalFolderRepository(string connectionString, string provider) : base(connectionString, provider)
        {
        }

        public async Task<LocalFolderConfiguration?> GetConfigurationAsync()
        {
            using (var conn = CreateConnection())
            {
                var query = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? "SELECT TOP 1 * FROM LocalFolderConfigurations ORDER BY Id ASC" 
                    : "SELECT * FROM LocalFolderConfigurations ORDER BY Id ASC LIMIT 1";
                return await conn.QueryFirstOrDefaultAsync<LocalFolderConfiguration>(query);
            }
        }

        public async Task SaveConfigurationAsync(LocalFolderConfiguration config)
        {
            using (var conn = CreateConnection())
            {
                var existing = await GetConfigurationAsync();

                if (existing == null)
                {
                    var sql = @"
                    INSERT INTO LocalFolderConfigurations (AppId, PickImagesPath, BackupPath, IsEnabled, CreatedBy, CreatedOn)
                    VALUES (@AppId, @PickImagesPath, @BackupPath, @IsEnabled, @CreatedBy, @CreatedOn)";
                    
                    await conn.ExecuteAsync(sql, new {
                        config.AppId,
                        config.PickImagesPath,
                        config.BackupPath,
                        config.IsEnabled,
                        config.CreatedBy,
                        CreatedOn = DateTime.UtcNow
                    });
                }
                else
                {
                    var sql = @"
                    UPDATE LocalFolderConfigurations 
                    SET AppId = @AppId, PickImagesPath = @PickImagesPath, BackupPath = @BackupPath, 
                        IsEnabled = @IsEnabled, CreatedBy = @CreatedBy, CreatedOn = @CreatedOn
                    WHERE Id = @Id";

                    await conn.ExecuteAsync(sql, new {
                        config.AppId,
                        config.PickImagesPath,
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
                await conn.ExecuteAsync("UPDATE LocalFolderConfigurations SET LastChecked = @lastChecked WHERE Id = @id", new { id, lastChecked });
            }
        }
    }
}
