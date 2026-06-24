using Dapper;
using Server.Models;
using System.Data;

namespace Server.Repositories
{
    public interface ILookupTableValueRepository
    {
        Task<IEnumerable<LookupTableValue>> GetAllAsync();
        Task<IEnumerable<LookupTableValue>> GetByLookupTableIdAsync(int lookupTableId);
        Task<IEnumerable<LookupTableValue>> GetByLookupTableNameAsync(string tableName);
        Task<LookupTableValue?> GetByIdAsync(int id);
        Task<int> CreateAsync(LookupTableValue entity);
        Task<bool> UpdateAsync(LookupTableValue entity);
        Task<bool> DeleteAsync(int id);
    }

    public class LookupTableValueRepository : BaseRepository, ILookupTableValueRepository
    {
        public LookupTableValueRepository(string connectionString, string provider) : base(connectionString, provider) { }

        public async Task<IEnumerable<LookupTableValue>> GetAllAsync()
        {
            using var conn = CreateConnection();
            return await conn.QueryAsync<LookupTableValue>(
                @"SELECT Id, LookupTableId, DisplayValue, ValueCode, Description, SortOrder, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM LookupTableValues 
                  ORDER BY LookupTableId, SortOrder, DisplayValue");
        }

        public async Task<IEnumerable<LookupTableValue>> GetByLookupTableIdAsync(int lookupTableId)
        {
            using var conn = CreateConnection();
            return await conn.QueryAsync<LookupTableValue>(
                @"SELECT Id, LookupTableId, DisplayValue, ValueCode, Description, SortOrder, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM LookupTableValues 
                  WHERE LookupTableId = @LookupTableId 
                  ORDER BY SortOrder, DisplayValue", new { LookupTableId = lookupTableId });
        }

        public async Task<IEnumerable<LookupTableValue>> GetByLookupTableNameAsync(string tableName)
        {
            using var conn = CreateConnection();
            string isActiveFilter = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "TRUE";
            string sql = $@"SELECT ltv.Id, ltv.LookupTableId, ltv.DisplayValue, ltv.ValueCode, ltv.Description, ltv.SortOrder, ltv.IsActive, ltv.CreatedBy, ltv.CreatedOn, ltv.UpdatedBy, ltv.UpdatedOn 
                  FROM LookupTableValues ltv
                  JOIN LookupTables lt ON ltv.LookupTableId = lt.Id
                  WHERE LOWER(lt.TableName) = LOWER(@TableName) AND ltv.IsActive = {isActiveFilter}
                  ORDER BY ltv.SortOrder, ltv.DisplayValue";
            return await conn.QueryAsync<LookupTableValue>(sql, new { TableName = tableName });
        }

        public async Task<LookupTableValue?> GetByIdAsync(int id)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<LookupTableValue>(
                @"SELECT Id, LookupTableId, DisplayValue, ValueCode, Description, SortOrder, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM LookupTableValues 
                  WHERE Id = @Id", new { Id = id });
        }

        public async Task<int> CreateAsync(LookupTableValue entity)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    INSERT INTO LookupTableValues (LookupTableId, DisplayValue, ValueCode, Description, SortOrder, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    OUTPUT INSERTED.Id
                    VALUES (@LookupTableId, @DisplayValue, @ValueCode, @Description, @SortOrder, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)";
            }
            else
            {
                sql = @"
                    INSERT INTO LookupTableValues (LookupTableId, DisplayValue, ValueCode, Description, SortOrder, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    VALUES (@LookupTableId, @DisplayValue, @ValueCode, @Description, @SortOrder, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)
                    RETURNING Id";
            }
            
            var id = await conn.QuerySingleAsync<int>(sql, new
            {
                entity.LookupTableId,
                entity.DisplayValue,
                entity.ValueCode,
                entity.Description,
                entity.SortOrder,
                entity.IsActive,
                entity.CreatedBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = entity.UpdatedBy,
                UpdatedOn = DateTime.UtcNow
            });

            return id;
        }

        public async Task<bool> UpdateAsync(LookupTableValue entity)
        {
            using var conn = CreateConnection();
            var affected = await conn.ExecuteAsync(
                @"UPDATE LookupTableValues 
                  SET LookupTableId = @LookupTableId, DisplayValue = @DisplayValue, ValueCode = @ValueCode, 
                      Description = @Description, SortOrder = @SortOrder, IsActive = @IsActive, 
                      UpdatedBy = @UpdatedBy, UpdatedOn = @UpdatedOn 
                  WHERE Id = @Id",
                new
                {
                    entity.Id,
                    entity.LookupTableId,
                    entity.DisplayValue,
                    entity.ValueCode,
                    entity.Description,
                    entity.SortOrder,
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
                "DELETE FROM LookupTableValues WHERE Id = @Id", new { Id = id });
            return affected > 0;
        }
    }
}