using Dapper;
using Server.Models;
using System.Data;

namespace Server.Repositories
{
    public interface IDatabaseLookupRepository
    {
        // Connection methods
        Task<IEnumerable<DatabaseConnection>> GetAllConnectionsAsync();
        Task<DatabaseConnection?> GetConnectionByIdAsync(int id);
        Task<int> CreateConnectionAsync(DatabaseConnection entity);
        Task<bool> UpdateConnectionAsync(DatabaseConnection entity);
        Task<bool> DeleteConnectionAsync(int id);

        // Mapping methods
        Task<IEnumerable<DatabaseLookupMapping>> GetAllMappingsAsync();
        Task<DatabaseLookupMapping?> GetMappingByIdAsync(int id);
        Task<DatabaseLookupMapping?> GetMappingByPropertyAsync(string propertyName);
        Task<int> CreateMappingAsync(DatabaseLookupMapping entity);
        Task<bool> UpdateMappingAsync(DatabaseLookupMapping entity);
        Task<bool> DeleteMappingAsync(int id);
    }

    public class DatabaseLookupRepository : BaseRepository, IDatabaseLookupRepository
    {
        public DatabaseLookupRepository(string connectionString, string provider) : base(connectionString, provider) { }

        // Connection methods
        public async Task<IEnumerable<DatabaseConnection>> GetAllConnectionsAsync()
        {
            using var conn = CreateConnection();
            return await conn.QueryAsync<DatabaseConnection>(
                @"SELECT Id, ConnectionName, DbType, ConnectionString, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM DatabaseConnections 
                  ORDER BY ConnectionName");
        }

        public async Task<DatabaseConnection?> GetConnectionByIdAsync(int id)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<DatabaseConnection>(
                @"SELECT Id, ConnectionName, DbType, ConnectionString, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM DatabaseConnections 
                  WHERE Id = @Id", new { Id = id });
        }

        public async Task<int> CreateConnectionAsync(DatabaseConnection entity)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    INSERT INTO DatabaseConnections (ConnectionName, DbType, ConnectionString, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    OUTPUT INSERTED.Id
                    VALUES (@ConnectionName, @DbType, @ConnectionString, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)";
            }
            else
            {
                sql = @"
                    INSERT INTO DatabaseConnections (ConnectionName, DbType, ConnectionString, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    VALUES (@ConnectionName, @DbType, @ConnectionString, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)
                    RETURNING Id";
            }
            
            return await conn.QuerySingleAsync<int>(sql, new
            {
                entity.ConnectionName,
                entity.DbType,
                entity.ConnectionString,
                entity.IsActive,
                entity.CreatedBy,
                CreatedOn = DateTime.UtcNow,
                entity.UpdatedBy,
                UpdatedOn = DateTime.UtcNow
            });
        }

        public async Task<bool> UpdateConnectionAsync(DatabaseConnection entity)
        {
            using var conn = CreateConnection();
            var affected = await conn.ExecuteAsync(
                @"UPDATE DatabaseConnections 
                  SET ConnectionName = @ConnectionName, DbType = @DbType, ConnectionString = @ConnectionString, 
                      IsActive = @IsActive, UpdatedBy = @UpdatedBy, UpdatedOn = @UpdatedOn 
                  WHERE Id = @Id",
                new
                {
                    entity.Id,
                    entity.ConnectionName,
                    entity.DbType,
                    entity.ConnectionString,
                    entity.IsActive,
                    entity.UpdatedBy,
                    UpdatedOn = DateTime.UtcNow
                });

            return affected > 0;
        }

        public async Task<bool> DeleteConnectionAsync(int id)
        {
            using var conn = CreateConnection();
            var affected = await conn.ExecuteAsync(
                "DELETE FROM DatabaseConnections WHERE Id = @Id", new { Id = id });
            return affected > 0;
        }

        // Mapping methods
        public async Task<IEnumerable<DatabaseLookupMapping>> GetAllMappingsAsync()
        {
            using var conn = CreateConnection();
            return await conn.QueryAsync<DatabaseLookupMapping>(
                @"SELECT Id, PropertyName, ConnectionId, SqlQuery, ColumnMappings AS ColumnMappingsJson, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM DatabaseLookupMappings 
                  ORDER BY PropertyName");
        }

        public async Task<DatabaseLookupMapping?> GetMappingByIdAsync(int id)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<DatabaseLookupMapping>(
                @"SELECT Id, PropertyName, ConnectionId, SqlQuery, ColumnMappings AS ColumnMappingsJson, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM DatabaseLookupMappings 
                  WHERE Id = @Id", new { Id = id });
        }

        public async Task<DatabaseLookupMapping?> GetMappingByPropertyAsync(string propertyName)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<DatabaseLookupMapping>(
                @"SELECT Id, PropertyName, ConnectionId, SqlQuery, ColumnMappings AS ColumnMappingsJson, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM DatabaseLookupMappings 
                  WHERE LOWER(PropertyName) = LOWER(@PropertyName)", new { PropertyName = propertyName });
        }

        public async Task<int> CreateMappingAsync(DatabaseLookupMapping entity)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    INSERT INTO DatabaseLookupMappings (PropertyName, ConnectionId, SqlQuery, ColumnMappings, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    OUTPUT INSERTED.Id
                    VALUES (@PropertyName, @ConnectionId, @SqlQuery, @ColumnMappings, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)";
            }
            else
            {
                sql = @"
                    INSERT INTO DatabaseLookupMappings (PropertyName, ConnectionId, SqlQuery, ColumnMappings, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    VALUES (@PropertyName, @ConnectionId, @SqlQuery, @ColumnMappings::jsonb, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)
                    RETURNING Id";
            }
            
            return await conn.QuerySingleAsync<int>(sql, new
            {
                entity.PropertyName,
                entity.ConnectionId,
                entity.SqlQuery,
                ColumnMappings = entity.ColumnMappingsJson,
                entity.IsActive,
                entity.CreatedBy,
                CreatedOn = DateTime.UtcNow,
                entity.UpdatedBy,
                UpdatedOn = DateTime.UtcNow
            });
        }

        public async Task<bool> UpdateMappingAsync(DatabaseLookupMapping entity)
        {
            using var conn = CreateConnection();
            string jsonCast = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "" : "::jsonb";
            string sql = $@"UPDATE DatabaseLookupMappings 
                  SET PropertyName = @PropertyName, ConnectionId = @ConnectionId,
                      SqlQuery = @SqlQuery, ColumnMappings = @ColumnMappings{jsonCast}, IsActive = @IsActive, 
                      UpdatedBy = @UpdatedBy, UpdatedOn = @UpdatedOn 
                  WHERE Id = @Id";
            
            var affected = await conn.ExecuteAsync(sql,
                new
                {
                    entity.Id,
                    entity.PropertyName,
                    entity.ConnectionId,
                    entity.SqlQuery,
                    ColumnMappings = entity.ColumnMappingsJson,
                    entity.IsActive,
                    entity.CreatedBy,
                    entity.UpdatedBy,
                    UpdatedOn = DateTime.UtcNow
                });

            return affected > 0;
        }

        public async Task<bool> DeleteMappingAsync(int id)
        {
            using var conn = CreateConnection();
            var affected = await conn.ExecuteAsync(
                "DELETE FROM DatabaseLookupMappings WHERE Id = @Id", new { Id = id });
            return affected > 0;
        }
    }
}
