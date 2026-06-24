using Server.Models;
using Server.Repositories;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Oracle.ManagedDataAccess.Client;
using System.Data;
using Microsoft.Extensions.Logging;

namespace Server.Services
{
    public interface ILookupService
    {
        // Lookup Table methods
        Task<List<LookupTableDto>> GetAllLookupTablesAsync();
        Task<LookupTableDto?> GetLookupTableByIdAsync(int id);
        Task<LookupTableDto?> GetLookupTableByNameAsync(string tableName);
        Task<LookupTableDto> CreateLookupTableAsync(CreateLookupTableRequest request, string? createdBy = null);
        Task<LookupTableDto> UpdateLookupTableAsync(UpdateLookupTableRequest request, string? updatedBy = null);
        Task<bool> DeleteLookupTableAsync(int id);

        // Lookup Table Value methods
        Task<List<LookupValueResponse>> GetLookupValuesByTableIdAsync(int lookupTableId);
        Task<List<LookupValueResponse>> GetLookupValuesByTableNameAsync(string tableName);
        Task<LookupTableValueDto?> GetLookupValueByIdAsync(int id);
        Task<LookupTableValueDto> CreateLookupValueAsync(CreateLookupTableValueRequest request, string? createdBy = null);
        Task<LookupTableValueDto> UpdateLookupValueAsync(UpdateLookupTableValueRequest request, string? updatedBy = null);
        Task<bool> DeleteLookupValueAsync(int id);

        // Property Validation Rule methods
        Task<List<PropertyValidationRuleDto>> GetAllValidationRulesAsync();
        Task<PropertyValidationRuleDto?> GetValidationRuleByIdAsync(int id);
        Task<PropertyValidationRuleDto?> GetValidationRuleByPropertyAsync(string propertyName);
        Task<PropertyValidationRuleDto> CreateValidationRuleAsync(CreatePropertyValidationRuleRequest request, string? createdBy = null);
        Task<PropertyValidationRuleDto> UpdateValidationRuleAsync(UpdatePropertyValidationRuleRequest request, string? updatedBy = null);
        Task<bool> DeleteValidationRuleAsync(int id);

        // Property Lookup Mapping methods
        Task<List<PropertyLookupMappingDto>> GetAllPropertyLookupMappingsAsync();
        Task<PropertyLookupMappingDto?> GetPropertyLookupMappingByIdAsync(int id);
        Task<PropertyLookupMappingDto?> GetPropertyLookupMappingByPropertyAsync(string propertyName);
        Task<PropertyLookupMappingDto> CreatePropertyLookupMappingAsync(CreatePropertyLookupMappingRequest request, string? createdBy = null);
        Task<PropertyLookupMappingDto> UpdatePropertyLookupMappingAsync(UpdatePropertyLookupMappingRequest request, string? updatedBy = null);
        Task<bool> DeletePropertyLookupMappingAsync(int id);

        // Validation methods
        Task<ValidationResult> ValidatePropertyValueAsync(string propertyName, string value);

        // Property System Integration methods
        Task<IEnumerable<LookupValueResponse>> GetLookupValuesByPropertyAsync(string propertyName);

        // Database Connection methods
        Task<List<DatabaseConnectionDto>> GetAllDatabaseConnectionsAsync();
        Task<DatabaseConnectionDto?> GetDatabaseConnectionByIdAsync(int id);
        Task<DatabaseConnectionDto> CreateDatabaseConnectionAsync(CreateDatabaseConnectionRequest request, string? createdBy = null);
        Task<DatabaseConnectionDto> UpdateDatabaseConnectionAsync(UpdateDatabaseConnectionRequest request, string? updatedBy = null);
        Task<bool> DeleteDatabaseConnectionAsync(int id);
        Task<(bool Success, string Message)> TestDatabaseConnectionAsync(DatabaseConnectionDto connection);

        // Database Lookup Mapping methods
        Task<List<DatabaseLookupMappingDto>> GetAllDatabaseLookupMappingsAsync();
        Task<DatabaseLookupMappingDto?> GetDatabaseLookupMappingByIdAsync(int id);
        Task<DatabaseLookupMappingDto?> GetDatabaseLookupMappingByPropertyAsync(string propertyName);
        Task<DatabaseLookupMappingDto> CreateDatabaseLookupMappingAsync(CreateDatabaseLookupMappingRequest request, string? createdBy = null);
        Task<DatabaseLookupMappingDto> UpdateDatabaseLookupMappingAsync(UpdateDatabaseLookupMappingRequest request, string? updatedBy = null);
        Task<bool> DeleteDatabaseLookupMappingAsync(int id);
        Task<DatabaseLookupResult> ExecuteDatabaseLookupAsync(DatabaseLookupSearchRequest request);
    }

    public class LookupService : BaseRepository, ILookupService
    {
        private readonly ILookupTableRepository _lookupTableRepository;
        private readonly ILookupTableValueRepository _lookupValueRepository;
        private readonly IPropertyValidationRuleRepository _validationRuleRepository;
        private readonly IPropertyLookupMappingRepository _lookupMappingRepository;
        private readonly IDatabaseLookupRepository _databaseLookupRepository;
        private readonly ILogger<LookupService> _logger;
        
        public LookupService(
            ILookupTableRepository lookupTableRepository,
            ILookupTableValueRepository lookupValueRepository,
            IPropertyValidationRuleRepository validationRuleRepository,
            IPropertyLookupMappingRepository lookupMappingRepository,
            IDatabaseLookupRepository databaseLookupRepository,
            IConfiguration configuration,
            ILogger<LookupService> logger,
            string provider) 
            : base(configuration.GetConnectionString("TrueCaptureDb") ??
                   configuration.GetConnectionString("DefaultConnection") ??
                   configuration.GetConnectionString("PostgreSqlConnection"), provider)
        {
            _lookupTableRepository = lookupTableRepository;
            _lookupValueRepository = lookupValueRepository;
            _validationRuleRepository = validationRuleRepository;
            _lookupMappingRepository = lookupMappingRepository;
            _databaseLookupRepository = databaseLookupRepository;
            _logger = logger;
        }

        // Lookup Table methods
        public async Task<List<LookupTableDto>> GetAllLookupTablesAsync()
        {
            var lookupTables = (await _lookupTableRepository.GetAllAsync()).ToList();
            // #6: Fetch ALL lookup values in one call, then group in-memory
            var allTableIds = lookupTables.Select(t => t.Id).ToList();
            var allValues = new List<LookupTableValue>();
            foreach (var id in allTableIds)
            {
                allValues.AddRange(await _lookupValueRepository.GetByLookupTableIdAsync(id));
            }
            var valuesByTableId = allValues.GroupBy(v => v.LookupTableId).ToDictionary(g => g.Key, g => g.ToList());

            var result = new List<LookupTableDto>();
            foreach (var table in lookupTables)
            {
                var values = valuesByTableId.TryGetValue(table.Id, out var vals) ? vals : new List<LookupTableValue>();
                result.Add(new LookupTableDto
                {
                    Id = table.Id,
                    TableName = table.TableName,
                    DisplayName = table.DisplayName,
                    Description = table.Description,
                    IsActive = table.IsActive,
                    Values = values.Select(v => new LookupTableValueDto
                    {
                        Id = v.Id,
                        LookupTableId = v.LookupTableId,
                        DisplayValue = v.DisplayValue,
                        ValueCode = v.ValueCode,
                        Description = v.Description,
                        SortOrder = v.SortOrder,
                        IsActive = v.IsActive
                    }).ToList()
                });
            }

            return result;
        }

        public async Task<LookupTableDto?> GetLookupTableByIdAsync(int id)
        {
            var lookupTable = await _lookupTableRepository.GetByIdAsync(id);
            if (lookupTable == null) return null;

            var lookupValues = (await _lookupValueRepository.GetByLookupTableIdAsync(id)).ToList();

            return new LookupTableDto
            {
                Id = lookupTable.Id,
                TableName = lookupTable.TableName,
                DisplayName = lookupTable.DisplayName,
                Description = lookupTable.Description,
                IsActive = lookupTable.IsActive,
                Values = lookupValues.Select(v => new LookupTableValueDto
                {
                    Id = v.Id,
                    LookupTableId = v.LookupTableId,
                    DisplayValue = v.DisplayValue,
                    ValueCode = v.ValueCode,
                    Description = v.Description,
                    SortOrder = v.SortOrder,
                    IsActive = v.IsActive
                }).ToList()
            };
        }

        public async Task<LookupTableDto?> GetLookupTableByNameAsync(string tableName)
        {
            var lookupTable = await _lookupTableRepository.GetByNameAsync(tableName);
            if (lookupTable == null) return null;

            var lookupValues = (await _lookupValueRepository.GetByLookupTableNameAsync(tableName)).ToList();

            return new LookupTableDto
            {
                Id = lookupTable.Id,
                TableName = lookupTable.TableName,
                DisplayName = lookupTable.DisplayName,
                Description = lookupTable.Description,
                IsActive = lookupTable.IsActive,
                Values = lookupValues.Select(v => new LookupTableValueDto
                {
                    Id = v.Id,
                    LookupTableId = v.LookupTableId,
                    DisplayValue = v.DisplayValue,
                    ValueCode = v.ValueCode,
                    Description = v.Description,
                    SortOrder = v.SortOrder,
                    IsActive = v.IsActive
                }).ToList()
            };
        }

        public async Task<LookupTableDto> CreateLookupTableAsync(CreateLookupTableRequest request, string? createdBy = null)
        {
            // Check if table name already exists
            var existingTable = await _lookupTableRepository.GetByNameAsync(request.TableName);
            if (existingTable != null)
            {
                throw new InvalidOperationException($"Lookup table with name '{request.TableName}' already exists.");
            }

            var lookupTable = new LookupTable
            {
                TableName = request.TableName,
                DisplayName = request.DisplayName,
                Description = request.Description,
                IsActive = request.IsActive,
                CreatedBy = createdBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = createdBy,
                UpdatedOn = DateTime.UtcNow
            };

            var createdId = await _lookupTableRepository.CreateAsync(lookupTable);
            var createdTable = await _lookupTableRepository.GetByIdAsync(createdId);

            if (createdTable == null)
                throw new InvalidOperationException("Failed to create lookup table");

            return new LookupTableDto
            {
                Id = createdTable.Id,
                TableName = createdTable.TableName,
                DisplayName = createdTable.DisplayName,
                Description = createdTable.Description,
                IsActive = createdTable.IsActive,
                Values = new List<LookupTableValueDto>()
            };
        }

        public async Task<LookupTableDto> UpdateLookupTableAsync(UpdateLookupTableRequest request, string? updatedBy = null)
        {
            var existingTable = await _lookupTableRepository.GetByIdAsync(request.Id);
            if (existingTable == null)
            {
                throw new InvalidOperationException($"Lookup table with ID {request.Id} not found.");
            }

            // Check if table name is being changed and if the new name already exists
            if (existingTable.TableName != request.TableName)
            {
                var duplicateTable = await _lookupTableRepository.GetByNameAsync(request.TableName);
                if (duplicateTable != null && duplicateTable.Id != request.Id)
                {
                    throw new InvalidOperationException($"Lookup table with name '{request.TableName}' already exists.");
                }
            }

            existingTable.TableName = request.TableName;
            existingTable.DisplayName = request.DisplayName;
            existingTable.Description = request.Description;
            existingTable.IsActive = request.IsActive;
            existingTable.UpdatedBy = updatedBy;
            existingTable.UpdatedOn = DateTime.UtcNow;

            var updated = await _lookupTableRepository.UpdateAsync(existingTable);
            if (!updated)
                throw new InvalidOperationException("Failed to update lookup table");

            // Fetch the updated table
            var updatedTable = await _lookupTableRepository.GetByIdAsync(request.Id);
            if (updatedTable == null)
                throw new InvalidOperationException("Failed to retrieve updated lookup table");

            // Get the current values for the table
            var lookupValues = (await _lookupValueRepository.GetByLookupTableIdAsync(updatedTable.Id)).ToList();

            return new LookupTableDto
            {
                Id = updatedTable.Id,
                TableName = updatedTable.TableName,
                DisplayName = updatedTable.DisplayName,
                Description = updatedTable.Description,
                IsActive = updatedTable.IsActive,
                Values = lookupValues.Select(v => new LookupTableValueDto
                {
                    Id = v.Id,
                    LookupTableId = v.LookupTableId,
                    DisplayValue = v.DisplayValue,
                    ValueCode = v.ValueCode,
                    Description = v.Description,
                    SortOrder = v.SortOrder,
                    IsActive = v.IsActive
                }).ToList()
            };
        }

        public async Task<bool> DeleteLookupTableAsync(int id)
        {
            var lookupTable = await _lookupTableRepository.GetByIdAsync(id);
            if (lookupTable == null) return false;

            // Delete all related lookup values first
            var lookupValues = (await _lookupValueRepository.GetByLookupTableIdAsync(id)).ToList();
            foreach (var value in lookupValues)
            {
                await _lookupValueRepository.DeleteAsync(value.Id);
            }

            // Delete any related property lookup mappings
            var mappings = await _lookupMappingRepository.GetAllAsync();
            foreach (var mapping in mappings.Where(m => m.LookupTableId == id))
            {
                await _lookupMappingRepository.DeleteAsync(mapping.Id);
            }

            return await _lookupTableRepository.DeleteAsync(id);
        }

        // Lookup Table Value methods
        public async Task<List<LookupValueResponse>> GetLookupValuesByTableIdAsync(int lookupTableId)
        {
            var values = await _lookupValueRepository.GetByLookupTableIdAsync(lookupTableId);
            return values.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ThenBy(v => v.DisplayValue)
                .Select(v => new LookupValueResponse
                {
                    Id = v.Id,
                    DisplayValue = v.DisplayValue,
                    ValueCode = v.ValueCode,
                    Description = v.Description,
                    SortOrder = v.SortOrder
                }).ToList();
        }

        public async Task<List<LookupValueResponse>> GetLookupValuesByTableNameAsync(string tableName)
        {
            var values = await _lookupValueRepository.GetByLookupTableNameAsync(tableName);
            return values.OrderBy(v => v.SortOrder).ThenBy(v => v.DisplayValue)
                .Select(v => new LookupValueResponse
                {
                    Id = v.Id,
                    DisplayValue = v.DisplayValue,
                    ValueCode = v.ValueCode,
                    Description = v.Description,
                    SortOrder = v.SortOrder
                }).ToList();
        }

        public async Task<LookupTableValueDto?> GetLookupValueByIdAsync(int id)
        {
            var lookupValue = await _lookupValueRepository.GetByIdAsync(id);
            if (lookupValue == null) return null;

            return new LookupTableValueDto
            {
                Id = lookupValue.Id,
                LookupTableId = lookupValue.LookupTableId,
                DisplayValue = lookupValue.DisplayValue,
                ValueCode = lookupValue.ValueCode,
                Description = lookupValue.Description,
                SortOrder = lookupValue.SortOrder,
                IsActive = lookupValue.IsActive
            };
        }

        public async Task<LookupTableValueDto> CreateLookupValueAsync(CreateLookupTableValueRequest request, string? createdBy = null)
        {
            // Check if ValueCode already exists in this table
            var allValues = await _lookupValueRepository.GetByLookupTableIdAsync(request.LookupTableId);
            var existingValue = allValues.FirstOrDefault(v => v.ValueCode == request.ValueCode);

            if (existingValue != null)
            {
                throw new InvalidOperationException($"Value code '{request.ValueCode}' already exists in lookup table with ID {request.LookupTableId}.");
            }

            var lookupValue = new LookupTableValue
            {
                LookupTableId = request.LookupTableId,
                DisplayValue = request.DisplayValue,
                ValueCode = request.ValueCode,
                Description = request.Description,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive,
                CreatedBy = createdBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = createdBy,
                UpdatedOn = DateTime.UtcNow
            };

            var createdId = await _lookupValueRepository.CreateAsync(lookupValue);
            var createdValue = await _lookupValueRepository.GetByIdAsync(createdId);

            if (createdValue == null)
                throw new InvalidOperationException("Failed to create lookup value");

            return new LookupTableValueDto
            {
                Id = createdValue.Id,
                LookupTableId = createdValue.LookupTableId,
                DisplayValue = createdValue.DisplayValue,
                ValueCode = createdValue.ValueCode,
                Description = createdValue.Description,
                SortOrder = createdValue.SortOrder,
                IsActive = createdValue.IsActive
            };
        }

        public async Task<LookupTableValueDto> UpdateLookupValueAsync(UpdateLookupTableValueRequest request, string? updatedBy = null)
        {
            var existingValue = await _lookupValueRepository.GetByIdAsync(request.Id);
            if (existingValue == null)
            {
                throw new InvalidOperationException($"Lookup value with ID {request.Id} not found.");
            }

            // Check if ValueCode is being changed and if the new code already exists in this table
            if (existingValue.ValueCode != request.ValueCode)
            {
                var allValues = await _lookupValueRepository.GetByLookupTableIdAsync(request.LookupTableId);
                var duplicateValue = allValues.FirstOrDefault(v => v.ValueCode == request.ValueCode && v.Id != request.Id);

                if (duplicateValue != null)
                {
                    throw new InvalidOperationException($"Value code '{request.ValueCode}' already exists in lookup table with ID {request.LookupTableId}.");
                }
            }

            existingValue.LookupTableId = request.LookupTableId;
            existingValue.DisplayValue = request.DisplayValue;
            existingValue.ValueCode = request.ValueCode;
            existingValue.Description = request.Description;
            existingValue.SortOrder = request.SortOrder;
            existingValue.IsActive = request.IsActive;
            existingValue.UpdatedBy = updatedBy;
            existingValue.UpdatedOn = DateTime.UtcNow;

            var updated = await _lookupValueRepository.UpdateAsync(existingValue);
            if (!updated)
                throw new InvalidOperationException("Failed to update lookup value");

            // Fetch the updated value
            var updatedValue = await _lookupValueRepository.GetByIdAsync(request.Id);
            if (updatedValue == null)
                throw new InvalidOperationException("Failed to retrieve updated lookup value");

            return new LookupTableValueDto
            {
                Id = updatedValue.Id,
                LookupTableId = updatedValue.LookupTableId,
                DisplayValue = updatedValue.DisplayValue,
                ValueCode = updatedValue.ValueCode,
                Description = updatedValue.Description,
                SortOrder = updatedValue.SortOrder,
                IsActive = updatedValue.IsActive
            };
        }

        public async Task<bool> DeleteLookupValueAsync(int id)
        {
            var lookupValue = await _lookupValueRepository.GetByIdAsync(id);
            if (lookupValue == null) return false;

            return await _lookupValueRepository.DeleteAsync(id);
        }

        // Property Validation Rule methods
        public async Task<List<PropertyValidationRuleDto>> GetAllValidationRulesAsync()
        {
            var rules = await _validationRuleRepository.GetAllAsync();
            return rules.Select(rule => new PropertyValidationRuleDto
            {
                Id = rule.Id,
                PropertyName = rule.PropertyName,
                DisplayName = rule.DisplayName,
                ValidationType = rule.ValidationType,
                ValidationRule = rule.ValidationRule,
                ErrorMessage = rule.ErrorMessage,
                IsActive = rule.IsActive
            }).ToList();
        }

        public async Task<PropertyValidationRuleDto?> GetValidationRuleByIdAsync(int id)
        {
            var rule = await _validationRuleRepository.GetByIdAsync(id);
            if (rule == null) return null;

            return new PropertyValidationRuleDto
            {
                Id = rule.Id,
                PropertyName = rule.PropertyName,
                DisplayName = rule.DisplayName,
                ValidationType = rule.ValidationType,
                ValidationRule = rule.ValidationRule,
                ErrorMessage = rule.ErrorMessage,
                IsActive = rule.IsActive
            };
        }

        public async Task<PropertyValidationRuleDto?> GetValidationRuleByPropertyAsync(string propertyName)
        {
            var rule = await _validationRuleRepository.GetByPropertyAsync(propertyName);
            if (rule == null) return null;

            return new PropertyValidationRuleDto
            {
                Id = rule.Id,
                PropertyName = rule.PropertyName,
                DisplayName = rule.DisplayName,
                ValidationType = rule.ValidationType,
                ValidationRule = rule.ValidationRule,
                ErrorMessage = rule.ErrorMessage,
                IsActive = rule.IsActive
            };
        }

        public async Task<PropertyValidationRuleDto> CreateValidationRuleAsync(CreatePropertyValidationRuleRequest request, string? createdBy = null)
        {
            // Check if validation rule already exists for this property
            var existingRule = await _validationRuleRepository.GetByPropertyAsync(request.PropertyName);
            if (existingRule != null)
            {
                throw new InvalidOperationException($"Validation rule for property '{request.PropertyName}' already exists.");
            }

            // If the validation type is Regex, validate that the pattern is a valid regex
            if (request.ValidationType?.ToUpper() == "REGEX" && !string.IsNullOrEmpty(request.ValidationRule))
            {
                try
                {
                    var testRegex = new System.Text.RegularExpressions.Regex(request.ValidationRule);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException($"Invalid regex pattern: {ex.Message}");
                }
            }

            var rule = new PropertyValidationRule
            {
                PropertyName = request.PropertyName,
                DisplayName = request.DisplayName,
                ValidationType = request.ValidationType,
                ValidationRule = request.ValidationRule,
                ErrorMessage = request.ErrorMessage,
                IsActive = request.IsActive,
                CreatedBy = createdBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = createdBy,
                UpdatedOn = DateTime.UtcNow
            };

            var createdId = await _validationRuleRepository.CreateAsync(rule);
            var createdRule = await _validationRuleRepository.GetByIdAsync(createdId);

            if (createdRule == null)
                throw new InvalidOperationException("Failed to create validation rule");

            return new PropertyValidationRuleDto
            {
                Id = createdRule.Id,
                PropertyName = createdRule.PropertyName,
                DisplayName = createdRule.DisplayName,
                ValidationType = createdRule.ValidationType,
                ValidationRule = createdRule.ValidationRule,
                ErrorMessage = createdRule.ErrorMessage,
                IsActive = createdRule.IsActive
            };
        }

        public async Task<PropertyValidationRuleDto> UpdateValidationRuleAsync(UpdatePropertyValidationRuleRequest request, string? updatedBy = null)
        {
            var existingRule = await _validationRuleRepository.GetByIdAsync(request.Id);
            if (existingRule == null)
            {
                throw new InvalidOperationException($"Validation rule with ID {request.Id} not found.");
            }

            // Check if property name is being changed and if a rule already exists for the new property
            if (existingRule.PropertyName != request.PropertyName)
            {
                var duplicateRule = await _validationRuleRepository.GetByPropertyAsync(request.PropertyName);
                if (duplicateRule != null && duplicateRule.Id != request.Id)
                {
                    throw new InvalidOperationException($"Validation rule for property '{request.PropertyName}' already exists.");
                }
            }

            // If the validation type is Regex, validate that the pattern is a valid regex
            if (request.ValidationType?.ToUpper() == "REGEX" && !string.IsNullOrEmpty(request.ValidationRule))
            {
                try
                {
                    var testRegex = new System.Text.RegularExpressions.Regex(request.ValidationRule);
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException($"Invalid regex pattern: {ex.Message}");
                }
            }

            existingRule.PropertyName = request.PropertyName;
            existingRule.DisplayName = request.DisplayName;
            existingRule.ValidationType = request.ValidationType;
            existingRule.ValidationRule = request.ValidationRule;
            existingRule.ErrorMessage = request.ErrorMessage;
            existingRule.IsActive = request.IsActive;
            existingRule.UpdatedBy = updatedBy;
            existingRule.UpdatedOn = DateTime.UtcNow;

            var updated = await _validationRuleRepository.UpdateAsync(existingRule);
            if (!updated)
                throw new InvalidOperationException("Failed to update validation rule");

            // Fetch the updated rule
            var updatedRule = await _validationRuleRepository.GetByIdAsync(request.Id);
            if (updatedRule == null)
                throw new InvalidOperationException("Failed to retrieve updated validation rule");

            return new PropertyValidationRuleDto
            {
                Id = updatedRule.Id,
                PropertyName = updatedRule.PropertyName,
                DisplayName = updatedRule.DisplayName,
                ValidationType = updatedRule.ValidationType,
                ValidationRule = updatedRule.ValidationRule,
                ErrorMessage = updatedRule.ErrorMessage,
                IsActive = updatedRule.IsActive
            };
        }

        public async Task<bool> DeleteValidationRuleAsync(int id)
        {
            var rule = await _validationRuleRepository.GetByIdAsync(id);
            if (rule == null) return false;

            return await _validationRuleRepository.DeleteAsync(id);
        }

        // Property Lookup Mapping methods
        public async Task<List<PropertyLookupMappingDto>> GetAllPropertyLookupMappingsAsync()
        {
            var mappings = await _lookupMappingRepository.GetAllAsync();
            // #7: Fetch ALL lookup tables once, then join in-memory
            var allLookupTables = (await _lookupTableRepository.GetAllAsync()).ToDictionary(t => t.Id);
            var result = new List<PropertyLookupMappingDto>();

            foreach (var mapping in mappings)
            {
                allLookupTables.TryGetValue(mapping.LookupTableId, out var lookupTable);
                result.Add(new PropertyLookupMappingDto
                {
                    Id = mapping.Id,
                    PropertyName = mapping.PropertyName,
                    LookupTableId = mapping.LookupTableId,
                    LookupTableName = lookupTable?.TableName ?? string.Empty,
                    LookupTableDisplayName = lookupTable?.DisplayName ?? string.Empty,
                    IsRequired = mapping.IsRequired,
                    DefaultValue = mapping.DefaultValue
                });
            }

            return result;
        }

        public async Task<PropertyLookupMappingDto?> GetPropertyLookupMappingByIdAsync(int id)
        {
            var mapping = await _lookupMappingRepository.GetByIdAsync(id);
            if (mapping == null) return null;

            var lookupTable = await _lookupTableRepository.GetByIdAsync(mapping.LookupTableId);

            return new PropertyLookupMappingDto
            {
                Id = mapping.Id,
                PropertyName = mapping.PropertyName,
                LookupTableId = mapping.LookupTableId,
                LookupTableName = lookupTable?.TableName ?? string.Empty,
                LookupTableDisplayName = lookupTable?.DisplayName ?? string.Empty,
                IsRequired = mapping.IsRequired,
                DefaultValue = mapping.DefaultValue
            };
        }

        public async Task<PropertyLookupMappingDto?> GetPropertyLookupMappingByPropertyAsync(string propertyName)
        {
            var mapping = await _lookupMappingRepository.GetByPropertyAsync(propertyName);
            if (mapping == null) return null;

            var lookupTable = await _lookupTableRepository.GetByIdAsync(mapping.LookupTableId);

            return new PropertyLookupMappingDto
            {
                Id = mapping.Id,
                PropertyName = mapping.PropertyName,
                LookupTableId = mapping.LookupTableId,
                LookupTableName = lookupTable?.TableName ?? string.Empty,
                LookupTableDisplayName = lookupTable?.DisplayName ?? string.Empty,
                IsRequired = mapping.IsRequired,
                DefaultValue = mapping.DefaultValue
            };
        }

        public async Task<PropertyLookupMappingDto> CreatePropertyLookupMappingAsync(CreatePropertyLookupMappingRequest request, string? createdBy = null)
        {
            // Check if mapping already exists for this property and lookup table
            var existingMapping = await _lookupMappingRepository.GetByPropertyAndLookupTableAsync(request.PropertyName, request.LookupTableId);
            if (existingMapping != null)
            {
                throw new InvalidOperationException($"Mapping already exists for property '{request.PropertyName}' and lookup table with ID {request.LookupTableId}.");
            }

            var mapping = new PropertyLookupMapping
            {
                PropertyName = request.PropertyName,
                LookupTableId = request.LookupTableId,
                IsRequired = request.IsRequired,
                DefaultValue = request.DefaultValue,
                CreatedBy = createdBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = createdBy,
                UpdatedOn = DateTime.UtcNow
            };

            var createdId = await _lookupMappingRepository.CreateAsync(mapping);
            var createdMapping = await _lookupMappingRepository.GetByIdAsync(createdId);

            if (createdMapping == null)
                throw new InvalidOperationException("Failed to create property lookup mapping");

            // Get the lookup table info to return in the DTO
            var lookupTable = await _lookupTableRepository.GetByIdAsync(createdMapping.LookupTableId);

            return new PropertyLookupMappingDto
            {
                Id = createdMapping.Id,
                PropertyName = createdMapping.PropertyName,
                LookupTableId = createdMapping.LookupTableId,
                LookupTableName = lookupTable?.TableName ?? string.Empty,
                LookupTableDisplayName = lookupTable?.DisplayName ?? string.Empty,
                IsRequired = createdMapping.IsRequired,
                DefaultValue = createdMapping.DefaultValue
            };
        }

        public async Task<PropertyLookupMappingDto> UpdatePropertyLookupMappingAsync(UpdatePropertyLookupMappingRequest request, string? updatedBy = null)
        {
            var existingMapping = await _lookupMappingRepository.GetByIdAsync(request.Id);
            if (existingMapping == null)
            {
                throw new InvalidOperationException($"Property lookup mapping with ID {request.Id} not found.");
            }

            // Check if property name or lookup table ID is being changed and if a mapping already exists
            if (existingMapping.PropertyName != request.PropertyName || existingMapping.LookupTableId != request.LookupTableId)
            {
                var duplicateMapping = await _lookupMappingRepository.GetByPropertyAndLookupTableAsync(request.PropertyName, request.LookupTableId);
                if (duplicateMapping != null && duplicateMapping.Id != request.Id)
                {
                    throw new InvalidOperationException($"Mapping already exists for property '{request.PropertyName}' and lookup table with ID {request.LookupTableId}.");
                }
            }

            existingMapping.PropertyName = request.PropertyName;
            existingMapping.LookupTableId = request.LookupTableId;
            existingMapping.IsRequired = request.IsRequired;
            existingMapping.DefaultValue = request.DefaultValue;
            existingMapping.UpdatedBy = updatedBy;
            existingMapping.UpdatedOn = DateTime.UtcNow;

            var updated = await _lookupMappingRepository.UpdateAsync(existingMapping);
            if (!updated)
                throw new InvalidOperationException("Failed to update property lookup mapping");

            // Fetch the updated mapping
            var updatedMapping = await _lookupMappingRepository.GetByIdAsync(request.Id);
            if (updatedMapping == null)
                throw new InvalidOperationException("Failed to retrieve updated property lookup mapping");

            // Get the lookup table info to return in the DTO
            var lookupTable = await _lookupTableRepository.GetByIdAsync(updatedMapping.LookupTableId);

            return new PropertyLookupMappingDto
            {
                Id = updatedMapping.Id,
                PropertyName = updatedMapping.PropertyName,
                LookupTableId = updatedMapping.LookupTableId,
                LookupTableName = lookupTable?.TableName ?? string.Empty,
                LookupTableDisplayName = lookupTable?.DisplayName ?? string.Empty,
                IsRequired = updatedMapping.IsRequired,
                DefaultValue = updatedMapping.DefaultValue
            };
        }

        public async Task<bool> DeletePropertyLookupMappingAsync(int id)
        {
            var mapping = await _lookupMappingRepository.GetByIdAsync(id);
            if (mapping == null) return false;

            return await _lookupMappingRepository.DeleteAsync(id);
        }

        // Validation methods
        public async Task<ValidationResult> ValidatePropertyValueAsync(string propertyName, string value)
        {
            var rule = await _validationRuleRepository.GetByPropertyAsync(propertyName);

            if (rule == null || !rule.IsActive)
            {
                // No validation rule exists or it's inactive, consider the value valid
                return new ValidationResult { IsValid = true, ErrorMessage = null };
            }

            try
            {
                switch (rule.ValidationType.ToUpper())
                {
                    case "REGEX":
                        if (string.IsNullOrEmpty(rule.ValidationRule))
                        {
                            return new ValidationResult { IsValid = true, ErrorMessage = null };
                        }

                        try
                        {
                            var regex = new System.Text.RegularExpressions.Regex(rule.ValidationRule);
                            var isValid = regex.IsMatch(value);

                            return new ValidationResult
                            {
                                IsValid = isValid,
                                ErrorMessage = isValid ? null : rule.ErrorMessage ?? "Value does not match the required format."
                            };
                        }
                        catch (ArgumentException ex)
                        {
                            // If there's an error with the regex pattern, log it and return a validation error
                            _logger.LogWarning(ex, "Regex validation error for property {PropertyName}", propertyName);
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = rule.ErrorMessage ?? "Invalid regex pattern configured for validation."
                            };
                        }

                    case "LOOKUP":
                        if (string.IsNullOrEmpty(rule.ValidationRule))
                        {
                            return new ValidationResult { IsValid = true, ErrorMessage = null };
                        }

                        // Check if value exists in the specified lookup table
                        var lookupValues = await _lookupValueRepository.GetByLookupTableNameAsync(rule.ValidationRule);
                        var lookupValue = lookupValues.FirstOrDefault(
                            lv => lv.ValueCode == value || lv.DisplayValue == value);

                        var isLookupValid = lookupValue != null;

                        return new ValidationResult
                        {
                            IsValid = isLookupValid,
                            ErrorMessage = isLookupValid ? null : rule.ErrorMessage ?? "Value does not exist in the lookup table."
                        };

                    case "RANGE":
                        // For range validation, the ValidationRule would contain min/max values
                        // Format: "min,max" or "min,max,datatype" where datatype could be "int", "decimal", etc.
                        if (string.IsNullOrEmpty(rule.ValidationRule))
                        {
                            return new ValidationResult { IsValid = true, ErrorMessage = null };
                        }

                        var rangeParts = rule.ValidationRule.Split(',');
                        if (rangeParts.Length < 2)
                        {
                            return new ValidationResult { IsValid = true, ErrorMessage = null };
                        }

                        var isValidRange = true;
                        string? rangeErrorMessage = null;

                        if (decimal.TryParse(rangeParts[0], out var min) && decimal.TryParse(rangeParts[1], out var max))
                        {
                            if (decimal.TryParse(value, out var actualValue))
                            {
                                isValidRange = actualValue >= min && actualValue <= max;
                                rangeErrorMessage = isValidRange ? null : rule.ErrorMessage ?? $"Value must be between {min} and {max}.";
                            }
                            else
                            {
                                isValidRange = false;
                                rangeErrorMessage = rule.ErrorMessage ?? "Value must be a valid number for range validation.";
                            }
                        }

                        return new ValidationResult
                        {
                            IsValid = isValidRange,
                            ErrorMessage = rangeErrorMessage
                        };

                    default:
                        // For other validation types or if type is not recognized, consider valid
                        return new ValidationResult { IsValid = true, ErrorMessage = null };
                }
            }
            catch (Exception ex)
            {
                // If there's an error in validation logic, consider the value invalid
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Validation error: {ex.Message}"
                };
            }
        }

        // Methods to integrate with existing property system
        public async Task<IEnumerable<LookupValueResponse>> GetLookupValuesByPropertyAsync(string propertyName)
        {
            // First, try to find the property in the Property table to get its LookupId
            var property = await GetPropertyByNameAsync(propertyName);
            if (property != null && property.LookupId.HasValue)
            {
                // Get values from the lookup table referenced by the property's LookupId
                var lookupValues = await _lookupValueRepository.GetByLookupTableIdAsync(property.LookupId.Value);
                return lookupValues.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ThenBy(v => v.DisplayValue)
                    .Select(v => new LookupValueResponse
                    {
                        Id = v.Id,
                        DisplayValue = v.DisplayValue,
                        ValueCode = v.ValueCode,
                        Description = v.Description,
                        SortOrder = v.SortOrder
                    }).ToList();
            }

            // If not found in Property table, try to find it in PropertyLookupMappings
            var mapping = await _lookupMappingRepository.GetByPropertyAsync(propertyName);
            if (mapping != null)
            {
                var lookupValues = await _lookupValueRepository.GetByLookupTableIdAsync(mapping.LookupTableId);
                return lookupValues.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ThenBy(v => v.DisplayValue)
                    .Select(v => new LookupValueResponse
                    {
                        Id = v.Id,
                        DisplayValue = v.DisplayValue,
                        ValueCode = v.ValueCode,
                        Description = v.Description,
                        SortOrder = v.SortOrder
                    }).ToList();
            }

            // If no lookup is associated with the property, return empty list
            return new List<LookupValueResponse>();
        }

        // Helper method to get property by name from the existing Property table
        private async Task<PropertyDto?> GetPropertyByNameAsync(string propertyName)
        {
            using var conn = CreateConnection();
            var sql = "SELECT id, propertyname as PropertyName, propertydesc as PropertyDesc, propertytype as PropertyType, propertylength as PropertyLength, lookupid as LookupId FROM property WHERE LOWER(propertyname) = LOWER(@PropertyName)";
            var result = await conn.QueryFirstOrDefaultAsync<PropertyDto>(sql, new { PropertyName = propertyName });
            return result;
        }

        // ================= DATABASE CONNECTION METHODS =================

        public async Task<List<DatabaseConnectionDto>> GetAllDatabaseConnectionsAsync()
        {
            var connections = await _databaseLookupRepository.GetAllConnectionsAsync();
            return connections.Select(c => new DatabaseConnectionDto
            {
                Id = c.Id,
                ConnectionName = c.ConnectionName,
                DbType = c.DbType,
                ConnectionString = c.ConnectionString,
                IsActive = c.IsActive,
                CreatedOn = c.CreatedOn
            }).ToList();
        }

        public async Task<DatabaseConnectionDto?> GetDatabaseConnectionByIdAsync(int id)
        {
            var c = await _databaseLookupRepository.GetConnectionByIdAsync(id);
            if (c == null) return null;

            return new DatabaseConnectionDto
            {
                Id = c.Id,
                ConnectionName = c.ConnectionName,
                DbType = c.DbType,
                ConnectionString = c.ConnectionString,
                IsActive = c.IsActive,
                CreatedOn = c.CreatedOn
            };
        }

        public async Task<DatabaseConnectionDto> CreateDatabaseConnectionAsync(CreateDatabaseConnectionRequest request, string? createdBy = null)
        {
            var entity = new DatabaseConnection
            {
                ConnectionName = request.ConnectionName,
                DbType = request.DbType,
                ConnectionString = request.ConnectionString,
                IsActive = request.IsActive,
                CreatedBy = createdBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = createdBy,
                UpdatedOn = DateTime.UtcNow
            };

            var id = await _databaseLookupRepository.CreateConnectionAsync(entity);
            var result = await _databaseLookupRepository.GetConnectionByIdAsync(id);

            if (result == null) throw new InvalidOperationException("Failed to create database connection");

            return new DatabaseConnectionDto
            {
                Id = result.Id,
                ConnectionName = result.ConnectionName,
                DbType = result.DbType,
                ConnectionString = result.ConnectionString,
                IsActive = result.IsActive,
                CreatedOn = result.CreatedOn
            };
        }

        public async Task<DatabaseConnectionDto> UpdateDatabaseConnectionAsync(UpdateDatabaseConnectionRequest request, string? updatedBy = null)
        {
            var existing = await _databaseLookupRepository.GetConnectionByIdAsync(request.Id);
            if (existing == null) throw new InvalidOperationException("Database connection not found");

            existing.ConnectionName = request.ConnectionName;
            existing.DbType = request.DbType;
            existing.ConnectionString = request.ConnectionString;
            existing.IsActive = request.IsActive;
            existing.UpdatedBy = updatedBy;
            existing.UpdatedOn = DateTime.UtcNow;

            await _databaseLookupRepository.UpdateConnectionAsync(existing);
            
            return new DatabaseConnectionDto
            {
                Id = existing.Id,
                ConnectionName = existing.ConnectionName,
                DbType = existing.DbType,
                ConnectionString = existing.ConnectionString,
                IsActive = existing.IsActive,
                CreatedOn = existing.CreatedOn
            };
        }

        public async Task<bool> DeleteDatabaseConnectionAsync(int id)
        {
            return await _databaseLookupRepository.DeleteConnectionAsync(id);
        }

        // ================= DATABASE LOOKUP MAPPING METHODS =================

        public async Task<List<DatabaseLookupMappingDto>> GetAllDatabaseLookupMappingsAsync()
        {
            var mappings = await _databaseLookupRepository.GetAllMappingsAsync();
            var result = new List<DatabaseLookupMappingDto>();

            foreach (var m in mappings)
            {
                var conn = await _databaseLookupRepository.GetConnectionByIdAsync(m.ConnectionId);
                result.Add(new DatabaseLookupMappingDto
                {
                    Id = m.Id,
                    PropertyName = m.PropertyName,
                    ConnectionId = m.ConnectionId,
                    ConnectionName = conn?.ConnectionName ?? "Unknown",
                    SqlQuery = m.SqlQuery,
                    ColumnMappings = System.Text.Json.JsonSerializer.Deserialize<List<DatabaseColumnMappingDto>>(m.ColumnMappingsJson) ?? new(),
                    IsActive = m.IsActive
                });
            }

            return result;
        }

        public async Task<DatabaseLookupMappingDto?> GetDatabaseLookupMappingByIdAsync(int id)
        {
            var m = await _databaseLookupRepository.GetMappingByIdAsync(id);
            if (m == null) return null;

            var conn = await _databaseLookupRepository.GetConnectionByIdAsync(m.ConnectionId);
            return new DatabaseLookupMappingDto
            {
                Id = m.Id,
                PropertyName = m.PropertyName,
                ConnectionId = m.ConnectionId,
                ConnectionName = conn?.ConnectionName ?? "Unknown",
                SqlQuery = m.SqlQuery,
                ColumnMappings = System.Text.Json.JsonSerializer.Deserialize<List<DatabaseColumnMappingDto>>(m.ColumnMappingsJson) ?? new(),
                IsActive = m.IsActive
            };
        }

        public async Task<DatabaseLookupMappingDto?> GetDatabaseLookupMappingByPropertyAsync(string propertyName)
        {
            var m = await _databaseLookupRepository.GetMappingByPropertyAsync(propertyName);
            if (m == null) return null;

            var conn = await _databaseLookupRepository.GetConnectionByIdAsync(m.ConnectionId);
            return new DatabaseLookupMappingDto
            {
                Id = m.Id,
                PropertyName = m.PropertyName,
                ConnectionId = m.ConnectionId,
                ConnectionName = conn?.ConnectionName ?? "Unknown",
                SqlQuery = m.SqlQuery,
                ColumnMappings = System.Text.Json.JsonSerializer.Deserialize<List<DatabaseColumnMappingDto>>(m.ColumnMappingsJson) ?? new(),
                IsActive = m.IsActive
            };
        }

        public async Task<DatabaseLookupMappingDto> CreateDatabaseLookupMappingAsync(CreateDatabaseLookupMappingRequest request, string? createdBy = null)
        {
            var existing = await _databaseLookupRepository.GetMappingByPropertyAsync(request.PropertyName);
            if (existing != null) throw new InvalidOperationException($"A database mapping for property '{request.PropertyName}' already exists.");

            var entity = new DatabaseLookupMapping
            {
                PropertyName = request.PropertyName,
                ConnectionId = request.ConnectionId,
                SqlQuery = request.SqlQuery,
                ColumnMappingsJson = System.Text.Json.JsonSerializer.Serialize(request.ColumnMappings),
                IsActive = request.IsActive,
                CreatedBy = createdBy,
                CreatedOn = DateTime.UtcNow,
                UpdatedBy = createdBy,
                UpdatedOn = DateTime.UtcNow
            };

            var id = await _databaseLookupRepository.CreateMappingAsync(entity);
            var result = await _databaseLookupRepository.GetMappingByIdAsync(id);

            if (result == null) throw new InvalidOperationException("Failed to create database lookup mapping");

            var conn = await _databaseLookupRepository.GetConnectionByIdAsync(result.ConnectionId);
            return new DatabaseLookupMappingDto
            {
                Id = result.Id,
                PropertyName = result.PropertyName,
                ConnectionId = result.ConnectionId,
                ConnectionName = conn?.ConnectionName ?? "Unknown",
                SqlQuery = result.SqlQuery,
                ColumnMappings = System.Text.Json.JsonSerializer.Deserialize<List<DatabaseColumnMappingDto>>(result.ColumnMappingsJson) ?? new(),
                IsActive = result.IsActive
            };
        }

        public async Task<DatabaseLookupMappingDto> UpdateDatabaseLookupMappingAsync(UpdateDatabaseLookupMappingRequest request, string? updatedBy = null)
        {
            var existing = await _databaseLookupRepository.GetMappingByIdAsync(request.Id);
            if (existing == null) throw new InvalidOperationException("Database lookup mapping not found");

            var existingWithProperty = await _databaseLookupRepository.GetMappingByPropertyAsync(request.PropertyName);
            if (existingWithProperty != null && existingWithProperty.Id != request.Id)
                throw new InvalidOperationException($"A database mapping for property '{request.PropertyName}' already exists.");

            existing.PropertyName = request.PropertyName;
            existing.ConnectionId = request.ConnectionId;
            existing.SqlQuery = request.SqlQuery;
            existing.ColumnMappingsJson = System.Text.Json.JsonSerializer.Serialize(request.ColumnMappings);
            existing.IsActive = request.IsActive;
            existing.UpdatedBy = updatedBy;
            existing.UpdatedOn = DateTime.UtcNow;

            await _databaseLookupRepository.UpdateMappingAsync(existing);
            
            var conn = await _databaseLookupRepository.GetConnectionByIdAsync(existing.ConnectionId);
            return new DatabaseLookupMappingDto
            {
                Id = existing.Id,
                PropertyName = existing.PropertyName,
                ConnectionId = existing.ConnectionId,
                ConnectionName = conn?.ConnectionName ?? "Unknown",
                SqlQuery = existing.SqlQuery,
                ColumnMappings = System.Text.Json.JsonSerializer.Deserialize<List<DatabaseColumnMappingDto>>(existing.ColumnMappingsJson) ?? new(),
                IsActive = existing.IsActive
            };
        }

        public async Task<bool> DeleteDatabaseLookupMappingAsync(int id)
        {
            return await _databaseLookupRepository.DeleteMappingAsync(id);
        }

        public async Task<DatabaseLookupResult> ExecuteDatabaseLookupAsync(DatabaseLookupSearchRequest request)
        {
            var result = new DatabaseLookupResult();
            try
            {
                var mapping = await _databaseLookupRepository.GetMappingByPropertyAsync(request.PropertyName);
                if (mapping == null)
                {
                    result.Success = false;
                    result.Message = $"No lookup mapping found for property '{request.PropertyName}'";
                    return result;
                }

                if (!mapping.IsActive)
                {
                    result.Success = false;
                    result.Message = "Lookup mapping is inactive";
                    return result;
                }

                var connection = await _databaseLookupRepository.GetConnectionByIdAsync(mapping.ConnectionId);
                if (connection == null)
                {
                    result.Success = false;
                    result.Message = "Database connection not found";
                    return result;
                }

                if (!connection.IsActive)
                {
                    result.Success = false;
                    result.Message = "Database connection associated with this mapping is inactive";
                    return result;
                }

                // Replace @Value or {Value} with the search value
                var sql = mapping.SqlQuery.Replace("@Value", request.Value ?? "").Replace("{Value}", request.Value ?? "");

                using (var dbConn = CreateSpecificConnection(connection))
                {
                    await ((dynamic)dbConn).OpenAsync();
                    var rows = await dbConn.QueryAsync(sql);
                    var rowList = rows.ToList();

                    if (rowList.Any())
                    {
                        var firstRow = (IDictionary<string, object>)rowList.First();
                        result.Columns = firstRow.Keys.ToList();
                        result.Rows = rowList.Select(r => ((IDictionary<string, object>)r).ToDictionary(k => k.Key, v => v.Value)).ToList();
                    }
                    else
                    {
                        result.Message = "No results found";
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Lookup failed: {ex.Message}";
                _logger.LogError(ex, "Error executing database lookup for property {PropertyName}", request.PropertyName);
            }
            return result;
        }

        private IDbConnection CreateSpecificConnection(DatabaseConnection connection)
        {
            return connection.DbType switch
            {
                DatabaseType.SqlServer => new SqlConnection(connection.ConnectionString),
                DatabaseType.MySql => new MySqlConnection(connection.ConnectionString),
                DatabaseType.Oracle => new OracleConnection(connection.ConnectionString),
                DatabaseType.PostgreSQL => new NpgsqlConnection(connection.ConnectionString),
                _ => throw new NotSupportedException("Unsupported database type")
            };
        }

        public async Task<(bool Success, string Message)> TestDatabaseConnectionAsync(DatabaseConnectionDto connection)
        {
            if (!connection.IsActive)
            {
                return (false, "Cannot test connection while it is inactive.");
            }
            try
            {
                switch (connection.DbType)
                {
                    case DatabaseType.SqlServer:
                        using (var sqlConn = new SqlConnection(connection.ConnectionString))
                        {
                            await sqlConn.OpenAsync();
                            return (true, "Connected successfully to SQL Server.");
                        }
                    case DatabaseType.MySql:
                        using (var mysqlConn = new MySqlConnection(connection.ConnectionString))
                        {
                            await mysqlConn.OpenAsync();
                            return (true, "Connected successfully to MySQL.");
                        }
                    case DatabaseType.Oracle:
                        using (var oracleConn = new OracleConnection(connection.ConnectionString))
                        {
                            await oracleConn.OpenAsync();
                            return (true, "Connected successfully to Oracle.");
                        }
                    case DatabaseType.PostgreSQL:
                        using (var pgConn = new NpgsqlConnection(connection.ConnectionString))
                        {
                            await pgConn.OpenAsync();
                            return (true, "Connected successfully to PostgreSQL.");
                        }
                    default:
                        return (false, "Unsupported database type.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}");
            }
        }
    }
}
