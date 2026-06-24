using Dapper;
using Npgsql;
using Server.Models;

namespace Server.Repositories
{
    public interface IEmailRepository
    {
        Task<EmailConfiguration?> GetConfigurationAsync();
        Task SaveConfigurationAsync(EmailConfiguration config);
        Task UpdateLastCheckedAsync(int id, DateTime lastChecked);
    }

    public class EmailRepository : BaseRepository, IEmailRepository
    {
        public EmailRepository(string connectionString, string provider) : base(connectionString, provider)
        {
        }

        public async Task<EmailConfiguration?> GetConfigurationAsync()
        {
            using var conn = CreateConnection();
            var count = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM EmailConfigurations");
            // Duplicate config check is informational only; no action needed
            var sql = _provider == "SqlServer" 
                ? "SELECT TOP 1 * FROM EmailConfigurations ORDER BY ID ASC" 
                : "SELECT * FROM EmailConfigurations ORDER BY ID ASC LIMIT 1";
            return await conn.QueryFirstOrDefaultAsync<EmailConfiguration>(sql);
        }

        public async Task SaveConfigurationAsync(EmailConfiguration config)
        {
            using var conn = CreateConnection();
            var existing = await GetConfigurationAsync();

            if (existing == null)
            {
                var sql = @"
                    INSERT INTO EmailConfigurations (EmailId, Password, AppId, DocumentType, ImapServer, ImapPort, IsEnabled, CreatedBy, CreatedOn)
                    VALUES (@EmailId, @Password, @AppId, @DocumentType, @ImapServer, @ImapPort, @IsEnabled, @CreatedBy, @CreatedOn)";
                await conn.ExecuteAsync(sql, config);
            }
            else
            {
                var sql = @"
                    UPDATE EmailConfigurations 
                    SET EmailId = @EmailId, 
                        Password = @Password, 
                        AppId = @AppId, 
                        DocumentType = @DocumentType, 
                        ImapServer = @ImapServer,
                        ImapPort = @ImapPort,
                        IsEnabled = @IsEnabled,
                        CreatedBy = @CreatedBy,
                        CreatedOn = @CreatedOn
                    WHERE Id = @Id";
                config.Id = existing.Id; // Ensure we update the existing one
                await conn.ExecuteAsync(sql, config);
            }
        }

        public async Task UpdateLastCheckedAsync(int id, DateTime lastChecked)
        {
             using var conn = CreateConnection();
             await conn.ExecuteAsync("UPDATE EmailConfigurations SET LastChecked = @lastChecked WHERE Id = @id", new { id, lastChecked });
        }
    }
}
