using Dapper;
using Server.Models;
using System.Data;

namespace Server.Repositories
{
    public interface IPropertyValidationRuleRepository
    {
        Task<IEnumerable<PropertyValidationRule>> GetAllAsync();
        Task<PropertyValidationRule?> GetByIdAsync(int id);
        Task<PropertyValidationRule?> GetByPropertyAsync(string propertyName);
        Task<int> CreateAsync(PropertyValidationRule entity);
        Task<bool> UpdateAsync(PropertyValidationRule entity);
        Task<bool> DeleteAsync(int id);
    }

    public class PropertyValidationRuleRepository : BaseRepository, IPropertyValidationRuleRepository
    {
        public PropertyValidationRuleRepository(string connectionString, string provider) : base(connectionString, provider) { }

        public async Task<IEnumerable<PropertyValidationRule>> GetAllAsync()
        {
            using var conn = CreateConnection();
            return await conn.QueryAsync<PropertyValidationRule>(
                @"SELECT Id, PropertyName, DisplayName, ValidationType, ValidationRule, ErrorMessage, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM PropertyValidationRules 
                  ORDER BY PropertyName");
        }

        public async Task<PropertyValidationRule?> GetByIdAsync(int id)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<PropertyValidationRule>(
                @"SELECT Id, PropertyName, DisplayName, ValidationType, ValidationRule, ErrorMessage, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM PropertyValidationRules 
                  WHERE Id = @Id", new { Id = id });
        }

        public async Task<PropertyValidationRule?> GetByPropertyAsync(string propertyName)
        {
            using var conn = CreateConnection();
            return await conn.QuerySingleOrDefaultAsync<PropertyValidationRule>(
                @"SELECT Id, PropertyName, DisplayName, ValidationType, ValidationRule, ErrorMessage, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn 
                  FROM PropertyValidationRules 
                  WHERE LOWER(PropertyName) = LOWER(@PropertyName)", new { PropertyName = propertyName });
        }

        public async Task<int> CreateAsync(PropertyValidationRule entity)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    INSERT INTO PropertyValidationRules (PropertyName, DisplayName, ValidationType, ValidationRule, ErrorMessage, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    OUTPUT INSERTED.Id
                    VALUES (@PropertyName, @DisplayName, @ValidationType, @ValidationRule, @ErrorMessage, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)";
            }
            else
            {
                sql = @"
                    INSERT INTO PropertyValidationRules (PropertyName, DisplayName, ValidationType, ValidationRule, ErrorMessage, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) 
                    VALUES (@PropertyName, @DisplayName, @ValidationType, @ValidationRule, @ErrorMessage, @IsActive, @CreatedBy, @CreatedOn, @UpdatedBy, @UpdatedOn)
                    RETURNING Id";
            }
            
            var id = await conn.QuerySingleAsync<int>(sql, new
            {
                entity.PropertyName,
                entity.DisplayName,
                entity.ValidationType,
                entity.ValidationRule,
                entity.ErrorMessage,
                entity.IsActive,
                entity.CreatedBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = entity.UpdatedBy,
                UpdatedOn = DateTime.UtcNow
            });

            return id;
        }

        public async Task<bool> UpdateAsync(PropertyValidationRule entity)
        {
            using var conn = CreateConnection();
            var affected = await conn.ExecuteAsync(
                @"UPDATE PropertyValidationRules 
                  SET PropertyName = @PropertyName, DisplayName = @DisplayName, ValidationType = @ValidationType, 
                      ValidationRule = @ValidationRule, ErrorMessage = @ErrorMessage, IsActive = @IsActive, 
                      UpdatedBy = @UpdatedBy, UpdatedOn = @UpdatedOn 
                  WHERE Id = @Id",
                new
                {
                    entity.Id,
                    entity.PropertyName,
                    entity.DisplayName,
                    entity.ValidationType,
                    entity.ValidationRule,
                    entity.ErrorMessage,
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
                "DELETE FROM PropertyValidationRules WHERE Id = @Id", new { Id = id });
            return affected > 0;
        }
    }
}