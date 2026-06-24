using Dapper;
using Server.Models;
using System.Data;

namespace Server.Repositories
{
    public interface IPropertyLookupMappingRepository
    {
        Task<IEnumerable<PropertyLookupMapping>> GetAllAsync();
        Task<PropertyLookupMapping?> GetByIdAsync(int id);
        Task<PropertyLookupMapping?> GetByPropertyAsync(string propertyName);
        Task<PropertyLookupMapping?> GetByPropertyAndLookupTableAsync(string propertyName, int lookupTableId);
        Task<int> CreateAsync(PropertyLookupMapping entity);
        Task<bool> UpdateAsync(PropertyLookupMapping entity);
        Task<bool> DeleteAsync(int id);
    }

    public class PropertyLookupMappingRepository : BaseRepository, IPropertyLookupMappingRepository
    {
        public PropertyLookupMappingRepository(string connectionString, string provider) : base(connectionString, provider) { }

        public async Task<IEnumerable<PropertyLookupMapping>> GetAllAsync()
        {
            using var conn = CreateConnection();
            return await conn.QueryAsync<PropertyLookupMapping>(
                @"SELECT Id, PropertyName, LookupTableId, IsRequired, DefaultValue, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM PropertyLookupMappings 
                  ORDER BY PropertyName");
        }

        public async Task<PropertyLookupMapping?> GetByIdAsync(int id)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<PropertyLookupMapping>(
                @"SELECT Id, PropertyName, LookupTableId, IsRequired, DefaultValue, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM PropertyLookupMappings 
                  WHERE Id = @Id", new { Id = id });
        }

        public async Task<PropertyLookupMapping?> GetByPropertyAsync(string propertyName)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<PropertyLookupMapping>(
                @"SELECT Id, PropertyName, LookupTableId, IsRequired, DefaultValue, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM PropertyLookupMappings 
                  WHERE LOWER(PropertyName) = LOWER(@PropertyName)", new { PropertyName = propertyName });
        }

        public async Task<PropertyLookupMapping?> GetByPropertyAndLookupTableAsync(string propertyName, int lookupTableId)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<PropertyLookupMapping>(
                @"SELECT Id, PropertyName, LookupTableId, IsRequired, DefaultValue, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM PropertyLookupMappings 
                  WHERE LOWER(PropertyName) = LOWER(@PropertyName) AND LookupTableId = @LookupTableId", 
                new { PropertyName = propertyName, LookupTableId = lookupTableId });
        }

        public async Task<int> CreateAsync(PropertyLookupMapping entity)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    INSERT INTO PropertyLookupMappings (PropertyName, LookupTableId, IsRequired, DefaultValue, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    OUTPUT INSERTED.Id
                    VALUES (@PropertyName, @LookupTableId, @IsRequired, @DefaultValue, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)";
            }
            else
            {
                sql = @"
                    INSERT INTO PropertyLookupMappings (PropertyName, LookupTableId, IsRequired, DefaultValue, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    VALUES (@PropertyName, @LookupTableId, @IsRequired, @DefaultValue, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)
                    RETURNING Id";
            }
            
            var id = await conn.QuerySingleAsync<int>(sql, new
            {
                entity.PropertyName,
                entity.LookupTableId,
                entity.IsRequired,
                entity.DefaultValue,
                entity.CreatedBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = entity.UpdatedBy,
                UpdatedOn = DateTime.UtcNow
            });

            return id;
        }

        public async Task<bool> UpdateAsync(PropertyLookupMapping entity)
        {
            using var conn = CreateConnection();
            var affected = await conn.ExecuteAsync(
                @"UPDATE PropertyLookupMappings 
                  SET PropertyName = @PropertyName, LookupTableId = @LookupTableId, IsRequired = @IsRequired, 
                      DefaultValue = @DefaultValue, UpdatedBy = @UpdatedBy, UpdatedOn = @UpdatedOn 
                  WHERE Id = @Id",
                new
                {
                    entity.Id,
                    entity.PropertyName,
                    entity.LookupTableId,
                    entity.IsRequired,
                    entity.DefaultValue,
                    entity.UpdatedBy,
                    UpdatedOn = DateTime.UtcNow
                });

            return affected > 0;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var conn = CreateConnection();
            var affected = await conn.ExecuteAsync(
                "DELETE FROM PropertyLookupMappings WHERE Id = @Id", new { Id = id });
            return affected > 0;
        }
    }
}