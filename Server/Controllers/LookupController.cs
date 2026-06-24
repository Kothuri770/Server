using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;
using System.ComponentModel.DataAnnotations;
using ValidationResult = Server.Models.ValidationResult;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LookupController : ControllerBase
    {
        private readonly ILookupService _lookupService;
        private readonly ILogger<LookupController> _logger;

        public LookupController(ILookupService lookupService, ILogger<LookupController> logger)
        {
            _lookupService = lookupService;
            _logger = logger;
        }

        #region Lookup Table Endpoints

        [HttpGet("tables")]
        public async Task<ActionResult<List<LookupTableDto>>> GetAllLookupTables()
        {
            try
            {
                var lookupTables = await _lookupService.GetAllLookupTablesAsync();
                return Ok(lookupTables);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all lookup tables");
                return StatusCode(500, "An error occurred while retrieving lookup tables");
            }
        }

        [HttpGet("tables/{id}")]
        public async Task<ActionResult<LookupTableDto>> GetLookupTableById(int id)
        {
            try
            {
                var lookupTable = await _lookupService.GetLookupTableByIdAsync(id);
                if (lookupTable == null)
                {
                    return NotFound($"Lookup table with ID {id} not found");
                }

                return Ok(lookupTable);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving lookup table with ID {LookupTableId}", id);
                return StatusCode(500, "An error occurred while retrieving the lookup table");
            }
        }

        [HttpGet("tables/name/{tableName}")]
        public async Task<ActionResult<LookupTableDto>> GetLookupTableByName(string tableName)
        {
            try
            {
                var lookupTable = await _lookupService.GetLookupTableByNameAsync(tableName);
                if (lookupTable == null)
                {
                    return NotFound($"Lookup table with name '{tableName}' not found");
                }

                return Ok(lookupTable);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving lookup table with name '{TableName}'", tableName);
                return StatusCode(500, "An error occurred while retrieving the lookup table");
            }
        }

        [HttpPost("tables")]
        public async Task<ActionResult<LookupTableDto>> CreateLookupTable([FromBody] CreateLookupTableRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var user = User.Identity?.Name ?? "System";
                var createdTable = await _lookupService.CreateLookupTableAsync(request, user);
                
                return CreatedAtAction(nameof(GetLookupTableById), new { id = createdTable.Id }, createdTable);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation when creating lookup table");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating lookup table");
                return StatusCode(500, "An error occurred while creating the lookup table");
            }
        }

        [HttpPut("tables")]
        public async Task<ActionResult<LookupTableDto>> UpdateLookupTable([FromBody] UpdateLookupTableRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var user = User.Identity?.Name ?? "System";
                var updatedTable = await _lookupService.UpdateLookupTableAsync(request, user);
                
                return Ok(updatedTable);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation when updating lookup table");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating lookup table with ID {LookupTableId}", request.Id);
                return StatusCode(500, "An error occurred while updating the lookup table");
            }
        }

        [HttpDelete("tables/{id}")]
        public async Task<ActionResult<bool>> DeleteLookupTable(int id)
        {
            try
            {
                var result = await _lookupService.DeleteLookupTableAsync(id);
                if (!result)
                {
                    return NotFound($"Lookup table with ID {id} not found");
                }

                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting lookup table with ID {LookupTableId}", id);
                return StatusCode(500, "An error occurred while deleting the lookup table");
            }
        }

        #endregion

        #region Lookup Table Value Endpoints

        [HttpGet("tables/{lookupTableId}/values")]
        public async Task<ActionResult<List<LookupValueResponse>>> GetLookupValuesByTableId(int lookupTableId)
        {
            try
            {
                var lookupValues = await _lookupService.GetLookupValuesByTableIdAsync(lookupTableId);
                return Ok(lookupValues);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving lookup values for table ID {LookupTableId}", lookupTableId);
                return StatusCode(500, "An error occurred while retrieving lookup values");
            }
        }

        [HttpGet("tables/name/{tableName}/values")]
        public async Task<ActionResult<List<LookupValueResponse>>> GetLookupValuesByTableName(string tableName)
        {
            try
            {
                var lookupValues = await _lookupService.GetLookupValuesByTableNameAsync(tableName);
                return Ok(lookupValues);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving lookup values for table name '{TableName}'", tableName);
                return StatusCode(500, "An error occurred while retrieving lookup values");
            }
        }

        [HttpGet("values/{id}")]
        public async Task<ActionResult<LookupTableValueDto>> GetLookupValueById(int id)
        {
            try
            {
                var lookupValue = await _lookupService.GetLookupValueByIdAsync(id);
                if (lookupValue == null)
                {
                    return NotFound($"Lookup value with ID {id} not found");
                }

                return Ok(lookupValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving lookup value with ID {LookupValueId}", id);
                return StatusCode(500, "An error occurred while retrieving the lookup value");
            }
        }

        [HttpPost("values")]
        public async Task<ActionResult<LookupTableValueDto>> CreateLookupValue([FromBody] CreateLookupTableValueRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var user = User.Identity?.Name ?? "System";
                var createdValue = await _lookupService.CreateLookupValueAsync(request, user);
                
                return CreatedAtAction(nameof(GetLookupValueById), new { id = createdValue.Id }, createdValue);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation when creating lookup value");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating lookup value");
                return StatusCode(500, "An error occurred while creating the lookup value");
            }
        }

        [HttpPut("values")]
        public async Task<ActionResult<LookupTableValueDto>> UpdateLookupValue([FromBody] UpdateLookupTableValueRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var user = User.Identity?.Name ?? "System";
                var updatedValue = await _lookupService.UpdateLookupValueAsync(request, user);
                
                return Ok(updatedValue);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation when updating lookup value");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating lookup value with ID {LookupValueId}", request.Id);
                return StatusCode(500, "An error occurred while updating the lookup value");
            }
        }

        [HttpDelete("values/{id}")]
        public async Task<ActionResult<bool>> DeleteLookupValue(int id)
        {
            try
            {
                var result = await _lookupService.DeleteLookupValueAsync(id);
                if (!result)
                {
                    return NotFound($"Lookup value with ID {id} not found");
                }

                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting lookup value with ID {LookupValueId}", id);
                return StatusCode(500, "An error occurred while deleting the lookup value");
            }
        }

        #endregion

        #region Property Validation Rule Endpoints

        [HttpGet("validation-rules")]
        public async Task<ActionResult<List<PropertyValidationRuleDto>>> GetAllValidationRules()
        {
            try
            {
                var validationRules = await _lookupService.GetAllValidationRulesAsync();
                return Ok(validationRules);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all validation rules");
                return StatusCode(500, "An error occurred while retrieving validation rules");
            }
        }

        [HttpGet("validation-rules/{id}")]
        public async Task<ActionResult<PropertyValidationRuleDto>> GetValidationRuleById(int id)
        {
            try
            {
                var validationRule = await _lookupService.GetValidationRuleByIdAsync(id);
                if (validationRule == null)
                {
                    return NotFound($"Validation rule with ID {id} not found");
                }

                return Ok(validationRule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving validation rule with ID {ValidationRuleId}", id);
                return StatusCode(500, "An error occurred while retrieving the validation rule");
            }
        }

        [HttpGet("validation-rules/property/{propertyName}")]
        public async Task<ActionResult<PropertyValidationRuleDto>> GetValidationRuleByProperty(string propertyName)
        {
            try
            {
                var validationRule = await _lookupService.GetValidationRuleByPropertyAsync(propertyName);
                if (validationRule == null)
                {
                    return NotFound($"Validation rule for property '{propertyName}' not found");
                }

                return Ok(validationRule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving validation rule for property '{PropertyName}'", propertyName);
                return StatusCode(500, "An error occurred while retrieving the validation rule");
            }
        }

        [HttpPost("validation-rules")]
        public async Task<ActionResult<PropertyValidationRuleDto>> CreateValidationRule([FromBody] CreatePropertyValidationRuleRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var user = User.Identity?.Name ?? "System";
                var createdRule = await _lookupService.CreateValidationRuleAsync(request, user);
                
                return CreatedAtAction(nameof(GetValidationRuleById), new { id = createdRule.Id }, createdRule);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation when creating validation rule");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating validation rule");
                return StatusCode(500, "An error occurred while creating the validation rule");
            }
        }

        [HttpPut("validation-rules")]
        public async Task<ActionResult<PropertyValidationRuleDto>> UpdateValidationRule([FromBody] UpdatePropertyValidationRuleRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var user = User.Identity?.Name ?? "System";
                var updatedRule = await _lookupService.UpdateValidationRuleAsync(request, user);
                
                return Ok(updatedRule);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation when updating validation rule");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating validation rule with ID {ValidationRuleId}", request.Id);
                return StatusCode(500, "An error occurred while updating the validation rule");
            }
        }

        [HttpDelete("validation-rules/{id}")]
        public async Task<ActionResult<bool>> DeleteValidationRule(int id)
        {
            try
            {
                var result = await _lookupService.DeleteValidationRuleAsync(id);
                if (!result)
                {
                    return NotFound($"Validation rule with ID {id} not found");
                }

                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting validation rule with ID {ValidationRuleId}", id);
                return StatusCode(500, "An error occurred while deleting the validation rule");
            }
        }

        #endregion

        #region Property Lookup Mapping Endpoints

        [HttpGet("property-mappings")]
        public async Task<ActionResult<List<PropertyLookupMappingDto>>> GetAllPropertyLookupMappings()
        {
            try
            {
                var mappings = await _lookupService.GetAllPropertyLookupMappingsAsync();
                return Ok(mappings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all property lookup mappings");
                return StatusCode(500, "An error occurred while retrieving property lookup mappings");
            }
        }

        [HttpGet("property-mappings/{id}")]
        public async Task<ActionResult<PropertyLookupMappingDto>> GetPropertyLookupMappingById(int id)
        {
            try
            {
                var mapping = await _lookupService.GetPropertyLookupMappingByIdAsync(id);
                if (mapping == null)
                {
                    return NotFound($"Property lookup mapping with ID {id} not found");
                }

                return Ok(mapping);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving property lookup mapping with ID {MappingId}", id);
                return StatusCode(500, "An error occurred while retrieving the property lookup mapping");
            }
        }

        [HttpGet("property-mappings/property/{propertyName}")]
        public async Task<ActionResult<PropertyLookupMappingDto>> GetPropertyLookupMappingByProperty(string propertyName)
        {
            try
            {
                var mapping = await _lookupService.GetPropertyLookupMappingByPropertyAsync(propertyName);
                if (mapping == null)
                {
                    return NotFound($"Property lookup mapping for property '{propertyName}' not found");
                }

                return Ok(mapping);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving property lookup mapping for property '{PropertyName}'", propertyName);
                return StatusCode(500, "An error occurred while retrieving the property lookup mapping");
            }
        }

        [HttpPost("property-mappings")]
        public async Task<ActionResult<PropertyLookupMappingDto>> CreatePropertyLookupMapping([FromBody] CreatePropertyLookupMappingRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var user = User.Identity?.Name ?? "System";
                var createdMapping = await _lookupService.CreatePropertyLookupMappingAsync(request, user);
                
                return CreatedAtAction(nameof(GetPropertyLookupMappingById), new { id = createdMapping.Id }, createdMapping);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation when creating property lookup mapping");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating property lookup mapping");
                return StatusCode(500, "An error occurred while creating the property lookup mapping");
            }
        }

        [HttpPut("property-mappings")]
        public async Task<ActionResult<PropertyLookupMappingDto>> UpdatePropertyLookupMapping([FromBody] UpdatePropertyLookupMappingRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var user = User.Identity?.Name ?? "System";
                var updatedMapping = await _lookupService.UpdatePropertyLookupMappingAsync(request, user);
                
                return Ok(updatedMapping);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid operation when updating property lookup mapping");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating property lookup mapping with ID {MappingId}", request.Id);
                return StatusCode(500, "An error occurred while updating the property lookup mapping");
            }
        }

        [HttpDelete("property-mappings/{id}")]
        public async Task<ActionResult<bool>> DeletePropertyLookupMapping(int id)
        {
            try
            {
                var result = await _lookupService.DeletePropertyLookupMappingAsync(id);
                if (!result)
                {
                    return NotFound($"Property lookup mapping with ID {id} not found");
                }

                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting property lookup mapping with ID {MappingId}", id);
                return StatusCode(500, "An error occurred while deleting the property lookup mapping");
            }
        }

        #endregion

        #region Property System Integration Endpoints

        [HttpGet("property-values/{propertyName}")]
        public async Task<ActionResult<List<LookupValueResponse>>> GetLookupValuesByProperty(string propertyName)
        {
            try
            {
                var values = await _lookupService.GetLookupValuesByPropertyAsync(propertyName);
                return Ok(values.ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving lookup values for property '{PropertyName}'", propertyName);
                return StatusCode(500, "An error occurred while retrieving lookup values");
            }
        }

        #endregion

        #region Validation Endpoints

        [HttpPost("validate")]
        public async Task<ActionResult<ValidationResult>> ValidatePropertyValue([FromBody] ValidatePropertyRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _lookupService.ValidatePropertyValueAsync(request.PropertyName, request.Value);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating property value for property '{PropertyName}'", request.PropertyName);
                return StatusCode(500, "An error occurred while validating the property value");
            }
        }

        #endregion
        
        #region Database Connection Endpoints

        [HttpGet("db-connections")]
        public async Task<ActionResult<List<DatabaseConnectionDto>>> GetAllDatabaseConnections()
        {
            try
            {
                var connections = await _lookupService.GetAllDatabaseConnectionsAsync();
                return Ok(connections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all database connections");
                return StatusCode(500, "An error occurred while retrieving database connections");
            }
        }

        [HttpGet("db-connections/{id}")]
        public async Task<ActionResult<DatabaseConnectionDto>> GetDatabaseConnectionById(int id)
        {
            try
            {
                var connection = await _lookupService.GetDatabaseConnectionByIdAsync(id);
                if (connection == null)
                {
                    return NotFound($"Database connection with ID {id} not found");
                }
                return Ok(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving database connection with ID {ConnectionId}", id);
                return StatusCode(500, "An error occurred while retrieving the database connection");
            }
        }

        [HttpPost("db-connections")]
        public async Task<ActionResult<DatabaseConnectionDto>> CreateDatabaseConnection([FromBody] CreateDatabaseConnectionRequest request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                var user = User.Identity?.Name ?? "System";
                var created = await _lookupService.CreateDatabaseConnectionAsync(request, user);
                return CreatedAtAction(nameof(GetDatabaseConnectionById), new { id = created.Id }, created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating database connection");
                return StatusCode(500, "An error occurred while creating the database connection");
            }
        }

        [HttpPut("db-connections")]
        public async Task<ActionResult<DatabaseConnectionDto>> UpdateDatabaseConnection([FromBody] UpdateDatabaseConnectionRequest request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                var user = User.Identity?.Name ?? "System";
                var updated = await _lookupService.UpdateDatabaseConnectionAsync(request, user);
                return Ok(updated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating database connection with ID {ConnectionId}", request.Id);
                return StatusCode(500, "An error occurred while updating the database connection");
            }
        }

        [HttpDelete("db-connections/{id}")]
        public async Task<ActionResult<bool>> DeleteDatabaseConnection(int id)
        {
            try
            {
                var result = await _lookupService.DeleteDatabaseConnectionAsync(id);
                if (!result) return NotFound($"Database connection with ID {id} not found");
                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting database connection with ID {ConnectionId}", id);
                return StatusCode(500, "An error occurred while deleting the database connection");
            }
        }

        [HttpPost("db-connections/test")]
        public async Task<ActionResult<object>> TestDatabaseConnection([FromBody] DatabaseConnectionDto request)
        {
            try
            {
                var result = await _lookupService.TestDatabaseConnectionAsync(request);
                return Ok(new { success = result.Success, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing database connection");
                return StatusCode(500, new { success = false, message = $"An unexpected error occurred: {ex.Message}" });
            }
        }

        #endregion

        #region Database Lookup Mapping Endpoints

        [HttpGet("db-lookup-mappings")]
        public async Task<ActionResult<List<DatabaseLookupMappingDto>>> GetAllDatabaseLookupMappings()
        {
            try
            {
                var mappings = await _lookupService.GetAllDatabaseLookupMappingsAsync();
                return Ok(mappings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all database lookup mappings");
                return StatusCode(500, "An error occurred while retrieving database lookup mappings");
            }
        }

        [HttpGet("db-lookup-mappings/{id}")]
        public async Task<ActionResult<DatabaseLookupMappingDto>> GetDatabaseLookupMappingById(int id)
        {
            try
            {
                var mapping = await _lookupService.GetDatabaseLookupMappingByIdAsync(id);
                if (mapping == null)
                {
                    return NotFound($"Database lookup mapping with ID {id} not found");
                }
                return Ok(mapping);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving database lookup mapping with ID {MappingId}", id);
                return StatusCode(500, "An error occurred while retrieving the database lookup mapping");
            }
        }

        [HttpGet("db-lookup-mappings/property/{propertyName}")]
        public async Task<ActionResult<DatabaseLookupMappingDto>> GetDatabaseLookupMappingByProperty(string propertyName)
        {
            try
            {
                var mapping = await _lookupService.GetDatabaseLookupMappingByPropertyAsync(propertyName);
                if (mapping == null)
                {
                    return NotFound($"Database lookup mapping for property '{propertyName}' not found");
                }
                return Ok(mapping);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving database lookup mapping for property '{PropertyName}'", propertyName);
                return StatusCode(500, "An error occurred while retrieving the database lookup mapping");
            }
        }

        [HttpPost("db-lookup-mappings")]
        public async Task<ActionResult<DatabaseLookupMappingDto>> CreateDatabaseLookupMapping([FromBody] CreateDatabaseLookupMappingRequest request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                var user = User.Identity?.Name ?? "System";
                var created = await _lookupService.CreateDatabaseLookupMappingAsync(request, user);
                return CreatedAtAction(nameof(GetDatabaseLookupMappingById), new { id = created.Id }, created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating database lookup mapping");
                return StatusCode(500, $"Error creating mapping: {ex.Message}");
            }
        }

        [HttpPut("db-lookup-mappings")]
        public async Task<ActionResult<DatabaseLookupMappingDto>> UpdateDatabaseLookupMapping([FromBody] UpdateDatabaseLookupMappingRequest request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                var user = User.Identity?.Name ?? "System";
                var updated = await _lookupService.UpdateDatabaseLookupMappingAsync(request, user);
                return Ok(updated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating database lookup mapping with ID {MappingId}", request.Id);
                return StatusCode(500, $"Error updating mapping: {ex.Message}");
            }
        }

        [HttpDelete("db-lookup-mappings/{id}")]
        public async Task<ActionResult<bool>> DeleteDatabaseLookupMapping(int id)
        {
            try
            {
                var result = await _lookupService.DeleteDatabaseLookupMappingAsync(id);
                if (!result) return NotFound($"Database lookup mapping with ID {id} not found");
                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting database lookup mapping with ID {MappingId}", id);
                return StatusCode(500, "An error occurred while deleting the database lookup mapping");
            }
        }

        [HttpPost("db-lookup-mappings/execute")]
        public async Task<ActionResult<DatabaseLookupResult>> ExecuteLookup([FromBody] DatabaseLookupSearchRequest request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                var result = await _lookupService.ExecuteDatabaseLookupAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing database lookup for property {PropertyName}", request.PropertyName);
                return StatusCode(500, "An error occurred while executing the database lookup");
            }
        }

        #endregion
    }

    public class ValidatePropertyRequest
    {
        [Required]
        public string PropertyName { get; set; } = string.Empty;
        
        [Required]
        public string Value { get; set; } = string.Empty;
    }
}