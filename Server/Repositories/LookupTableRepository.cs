using Dapper;
using Server.Models;
using System.Data;

namespace Server.Repositories
{
    public interface ILookupTableRepository
    {
        Task<IEnumerable<LookupTable>> GetAllAsync();
        Task<LookupTable?> GetByIdAsync(int id);
        Task<LookupTable?> GetByNameAsync(string tableName);
        Task<int> CreateAsync(LookupTable entity);
        Task<bool> UpdateAsync(LookupTable entity);
        Task<bool> DeleteAsync(int id);
    }

    public class LookupTableRepository : BaseRepository, ILookupTableRepository
    {
        public LookupTableRepository(string connectionString, string provider) : base(connectionString, provider) { }

        public async Task<IEnumerable<LookupTable>> GetAllAsync()
        {
            using var conn = CreateConnection();
            return await conn.QueryAsync<LookupTable>(
                @"SELECT Id, TableName, DisplayName, Description, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM LookupTables 
                  ORDER BY DisplayName");
        }

        public async Task<LookupTable?> GetByIdAsync(int id)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<LookupTable>(
                @"SELECT Id, TableName, DisplayName, Description, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM LookupTables 
                  WHERE Id = @Id", new { Id = id });
        }

        public async Task<LookupTable?> GetByNameAsync(string tableName)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<LookupTable>(
                @"SELECT Id, TableName, DisplayName, Description, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM LookupTables 
                  WHERE LOWER(TableName) = LOWER(@TableName)", new { TableName = tableName });
        }

        public async Task<int> CreateAsync(LookupTable entity)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    INSERT INTO LookupTables (TableName, DisplayName, Description, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    OUTPUT INSERTED.Id
                    VALUES (@TableName, @DisplayName, @Description, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)";
            }
            else
            {
                sql = @"
                    INSERT INTO LookupTables (TableName, DisplayName, Description, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    VALUES (@TableName, @DisplayName, @Description, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)
                    RETURNING Id";
            }
            
            var id = await conn.QuerySingleAsync<int>(sql, new
            {
                entity.TableName,
                entity.DisplayName,
                entity.Description,
                entity.IsActive,
                entity.CreatedBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = entity.UpdatedBy,
                UpdatedOn = DateTime.UtcNow
            });

            return id;
        }

        public async Task<bool> UpdateAsync(LookupTable entity)
        {
            using var conn = CreateConnection();
            var affected = await conn.ExecuteAsync(
                @"UPDATE LookupTables 
                  SET TableName = @TableName, DisplayName = @DisplayName, Description = @Description, 
                      IsActive = @IsActive, UpdatedBy = @UpdatedBy, UpdatedOn = @UpdatedOn 
                  WHERE Id = @Id",
                new
                {
                    entity.Id,
                    entity.TableName,
                    entity.DisplayName,
                    entity.Description,
                    entity.IsActive,
                    entity.UpdatedBy,
                    UpdatedOn = DateTime.UtcNow
                });

            return affected > 0;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var conn = CreateConnection();
            var affected = await conn.ExecuteAsync(
                "DELETE FROM LookupTables WHERE Id = @Id", new { Id = id });
            return affected > 0;
        }
    }
}