using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Server.Models;
using Server.Services.Configuration;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Server.Controllers
{
    [ApiController]
    [Route("api/configuration")]
    [Authorize]
    public partial class ConfigurationController : ControllerBase
    {
        [GeneratedRegex(@"^[a-zA-Z\s]+$")]
        private static partial Regex AlphaSpacesRegex();

        private readonly IConfigurationService _configService;

        public ConfigurationController(IConfigurationService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        [HttpGet("object-types")]
        public async Task<ActionResult<IEnumerable<ObjectTypeDto>>> GetObjectTypes([FromQuery] string? type = null)
        {
            try
            {
                return Ok(await _configService.GetObjectTypesAsync(type));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving object types: {ex.Message}");
            }
        }

        [HttpGet("object-types/{id}")]
        public async Task<ActionResult<ObjectTypeDto>> GetObjectType(int id)
        {
            try
            {
                var objectType = await _configService.GetObjectTypeByIdAsync(id);
                if (objectType == null)
                {
                    return NotFound($"Object type with ID {id} not found");
                }
                return Ok(objectType);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving object type: {ex.Message}");
            }
        }

        [HttpPost("object-types")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<ObjectTypeDto>> CreateObjectType([FromBody] ObjectTypeDto objType)
        {
            if (objType == null || string.IsNullOrWhiteSpace(objType.ObjectName))
                return BadRequest("Name is required");
            
            // Validate that the name contains only letters and spaces, no numbers or special characters
            if (!AlphaSpacesRegex().IsMatch(objType.ObjectName))
                return BadRequest("Name can only contain letters and spaces, no numbers or special characters allowed");

            try
            {
                return Ok(await _configService.CreateObjectTypeAsync(objType));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error creating object type: {ex.Message}");
            }
        }

        [HttpPut("object-types/{id}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> UpdateObjectType(int id, [FromBody] ObjectTypeDto objType)
        {
            if (id != objType.Id) return BadRequest("ID mismatch");
            
            // Validate that the name contains only letters and spaces, no numbers or special characters
            if (!string.IsNullOrWhiteSpace(objType.ObjectName) && !AlphaSpacesRegex().IsMatch(objType.ObjectName))
                return BadRequest("Name can only contain letters and spaces, no numbers or special characters allowed");

            try
            {
                await _configService.UpdateObjectTypeAsync(objType);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating object type: {ex.Message}");
            }
        }

        [HttpDelete("object-types/{id}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> DeleteObjectType(int id)
        {
            try
            {
                await _configService.DeleteObjectTypeAsync(id);
                return Ok(new { message = "Object type deleted successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error deleting object type: {ex.Message}" });
            }
        }

        [HttpGet("relations/{parentId}")]
        public async Task<ActionResult<IEnumerable<ObjectTypeDto>>> GetChildObjects(int parentId)
        {
            try
            {
                return Ok(await _configService.GetChildObjectsAsync(parentId));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving child objects: {ex.Message}");
            }
        }

        [HttpPost("relations")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> CreateObjectRelation([FromBody] ObjectRelationDto relation)
        {
            try
            {
                await _configService.CreateObjectRelationAsync(relation.ParentObjectId, relation.ChildObjectId);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error creating relation: {ex.Message}");
            }
        }



        [HttpGet("properties")]
        public async Task<ActionResult<IEnumerable<PropertyDto>>> GetProperties()
        {
            try
            {
                return Ok(await _configService.GetPropertiesAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving properties: {ex.Message}");
            }
        }

        [HttpPost("properties")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<int>> CreateProperty([FromBody] PropertyDto property)
        {
            // Validate that the property name contains only letters and spaces, no numbers or special characters
            if (property != null && !string.IsNullOrWhiteSpace(property.PropertyName) && !AlphaSpacesRegex().IsMatch(property.PropertyName))
                return BadRequest("Property name can only contain letters and spaces, no numbers or special characters allowed");
            
            if (property == null) return BadRequest("Property data is required");

            try
            {
                return Ok(await _configService.CreatePropertyAsync(property));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error creating property: {ex.Message}");
            }
        }

        [HttpPut("properties/{id}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> UpdateProperty(int id, [FromBody] PropertyDto property)
        {
            // Validate that the property name contains only letters and spaces, no numbers or special characters
            if (property != null && !string.IsNullOrWhiteSpace(property.PropertyName) && !AlphaSpacesRegex().IsMatch(property.PropertyName))
                return BadRequest("Property name can only contain letters and spaces, no numbers or special characters allowed");
            
            if (property == null) return BadRequest("Property data is required");

            try
            {
                await _configService.SavePropertyAsync(property);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error updating property: {ex.Message}");
            }
        }

        [HttpDelete("properties/{propertyId}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> DeleteProperty(int propertyId)
        {
            try
            {
                await _configService.DeletePropertyAsync(propertyId);
                return Ok(new { message = "Property deleted successfully" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Error deleting property: {ex.Message}" });
            }
        }

        [HttpGet("doctype-properties/{docTypeId}")]
        public async Task<ActionResult<IEnumerable<DocTypePropertyDto>>> GetDocTypeProperties(int docTypeId)
        {
            try
            {
                var properties = await _configService.GetDocTypePropertiesAsync(docTypeId);
                return Ok(properties);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving document type properties: {ex.Message}");
            }
        }

        [HttpPost("doctype-properties")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> SaveDocTypeProperty([FromBody] DocTypePropertyDto prop)
        {
            if (prop == null || prop.PropertyId <= 0)
                return BadRequest("Property ID is required");

            try
            {
                await _configService.SaveDocTypePropertyAsync(prop);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error saving property mapping: {ex.Message}");
            }
        }

        [HttpDelete("doctype-properties/{mappingId}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> DeleteDocTypeProperty(int mappingId, [FromQuery] bool isBatchProperty = false)
        {
            try
            {
                await _configService.DeleteDocTypePropertyAsync(mappingId, isBatchProperty);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting property mapping: {ex.Message}");
            }
        }

        [HttpGet("zones/{docTypeId}")]
        public async Task<ActionResult<IEnumerable<ZoneDto>>> GetZones(int docTypeId)
        {
            try
            {
                return Ok(await _configService.GetZonesForDocTypeAsync(docTypeId));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving zones: {ex.Message}");
            }
        }

        [HttpPost("zones")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<int>> SaveZone([FromBody] ZoneDto zone)
        {
            try
            {
                return Ok(await _configService.SaveZoneAsync(zone));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error saving zone: {ex.Message}");
            }
        }

        [HttpDelete("zones/{zoneId}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> DeleteZone(int zoneId)
        {
            try
            {
                await _configService.DeleteZoneAsync(zoneId);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting zone: {ex.Message}");
            }
        }

        [HttpGet("identification/{docTypeId}")]
        public async Task<ActionResult<IEnumerable<IdentificationRuleDto>>> GetIdentificationRules(int docTypeId)
        {
            try
            {
                return Ok(await _configService.GetIdentificationRulesAsync(docTypeId));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving identification rules: {ex.Message}");
            }
        }

        [HttpPost("identification")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> SaveIdentificationRule([FromBody] IdentificationRuleDto rule)
        {
            try
            {
                await _configService.SaveIdentificationRuleAsync(rule);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error saving identification rule: {ex.Message}");
            }
        }

        [HttpDelete("identification/{ruleId}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> DeleteIdentificationRule(int ruleId)
        {
            try
            {
                await _configService.DeleteIdentificationRuleAsync(ruleId);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting identification rule: {ex.Message}");
            }
        }

        [HttpGet("lookups")]
        public async Task<ActionResult<IEnumerable<LookupConfigDto>>> GetLookups()
        {
            try
            {
                return Ok(await _configService.GetLookupConfigurationsAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving lookups: {ex.Message}");
            }
        }

        [HttpPost("lookups")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<int>> SaveLookup([FromBody] LookupConfigDto lookup)
        {
            try
            {
                return Ok(await _configService.SaveLookupConfigurationAsync(lookup));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error saving lookup: {ex.Message}");
            }
        }

        [HttpGet("dms-configs")] // Plural endpoint for client
        public async Task<ActionResult<IEnumerable<DmsConfigDto>>> GetDmsConfigs()
        {
            try
            {
                return Ok(await _configService.GetDmsConfigurationsAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving DMS configurations: {ex.Message}");
            }
        }
        
        [HttpGet("dms-configs/doctype/{docTypeId}")]
        public async Task<ActionResult<IEnumerable<DmsConfigDto>>> GetDmsConfigsForDocType(int docTypeId)
        {
            try
            {
                return Ok(await _configService.GetDmsConfigsForDocTypeAsync(docTypeId));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving DMS configurations for document type: {ex.Message}");
            }
        }
                
        [HttpGet("dms-config")]
        public async Task<ActionResult<IEnumerable<DmsConfigDto>>> GetDmsConfigsOld()
        {
            try
            {
                return Ok(await _configService.GetDmsConfigurationsAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving DMS configurations: {ex.Message}");
            }
        }

        [HttpGet("dms-config/{configId}")]
        public async Task<ActionResult<DmsConfigDto>> GetDmsConfig(int configId)
        {
            try
            {
                return Ok(await _configService.GetDmsConfigurationAsync(configId));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving DMS configuration: {ex.Message}");
            }
        }

        [HttpPost("dms-config")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<int>> SaveDmsConfig([FromBody] DmsConfigDto config)
        {
            try
            {
                return Ok(await _configService.SaveDmsConfigurationAsync(config));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error saving DMS configuration: {ex.Message}");
            }
        }

        [HttpDelete("dms-config/{configId}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> DeleteDmsConfig(int configId)
        {
            try
            {
                await _configService.DeleteDmsConfigurationAsync(configId);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting DMS configuration: {ex.Message}");
            }
        }

        [HttpGet("dms-providers")]
        public async Task<ActionResult<IEnumerable<DmsProviderDto>>> GetDmsProviders()
        {
            try
            {
                return Ok(await _configService.GetDmsProvidersAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving DMS providers: {ex.Message}");
            }
        }

        [HttpGet("dms-output-formats")]
        public async Task<ActionResult<IEnumerable<DmsOutputFormatDto>>> GetDmsOutputFormats()
        {
            try
            {
                return Ok(await _configService.GetDmsOutputFormatsAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving DMS output formats: {ex.Message}");
            }
        }

        [HttpGet("formid/{docTypeId}")]
        public async Task<ActionResult<FormIdSettings>> GetFormIdSettings(int docTypeId)
        {
            try
            {
                var settings = await _configService.GetFormIdSettingsAsync(docTypeId);
                return Ok(settings ?? new FormIdSettings { DocTypeId = docTypeId, IsEnabled = false });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving FormId settings: {ex.Message}");
            }
        }

        [HttpPost("formid")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> SaveFormIdSettings([FromBody] FormIdSettings settings)
        {
            if (settings == null)
                return BadRequest("Settings cannot be null");

            try
            {
                await _configService.SaveFormIdSettingsAsync(settings);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error saving FormId settings: {ex.Message}");
            }
        }

        [HttpGet("samples/{docTypeId}")]
        public async Task<IActionResult> GetSampleImageUrl(int docTypeId)
        {
            try
            {
                var url = await _configService.GetSampleImageUrlAsync(docTypeId);
                return Ok(new { SampleFile = url });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving sample image: {ex.Message}");
            }
        }

        [HttpGet("settings")]
        public async Task<ActionResult<IEnumerable<ConfigurationDtos>>> GetAllSettings()
        {
            try { return Ok(await _configService.GetAllConfigurationsAsync()); }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("settings-all")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> SaveAllSettings([FromBody] ConfigurationSettings dto)
        {
            if (dto == null) return BadRequest();
            try { await _configService.SaveAllSettingsAsync(dto); return Ok(); }
            catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("validate-path")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<PathValidationResult>> ValidatePath([FromBody] PathValidationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Path)) return BadRequest();
            try { var result = await _configService.ValidateAndCreatePathAsync(request.Path, request.CreateIfNotExists); return Ok(result); }
           catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
        }

        [HttpPost("update-separation-mode")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> UpdateSeparationMode([FromBody] UpdateOcrModeRequest request)
        {
            if (string.IsNullOrEmpty(request?.OcrMode))
            {
                return BadRequest("Separation mode is required");
            }

            try
            {
                var result = await _configService.UpdateStepStatusForSeparationModeAsync(request.OcrMode);
                return Ok(new { success = result, message = "Separation mode updated successfully" });
            }
            catch (Exception)
            {
               
                return StatusCode(500, new { error = "An error occurred while updating separation mode", success = false });
            }
        }

        [HttpPost("map-property-zone")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> MapPropertyToZone([FromBody] PropertyZoneMappingRequest request)
        {
            try
            {
                var result = await _configService.MapPropertyToZoneAsync(request.PropertyId, request.ZoneId, request.DocTypeId);
                if (result)
                {
                    return Ok(new { success = true, message = "Property successfully mapped to zone" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Failed to map property to zone" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpPost("test-dms/{configId}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<bool>> TestDmsConnection(int configId)
        {
            try
            {
                var result = await _configService.TestDmsConnectionAsync(configId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, success = false });
            }
        }
        
        [HttpGet("user-roles")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<IEnumerable<UserRoleDto>>> GetUserRoles()
        {
            try
            {
                return Ok(await _configService.GetUserRolesAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving user roles: {ex.Message}");
            }
        }

        [HttpGet("permissions")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<IEnumerable<PermissionDto>>> GetPermissions()
        {
            try
            {
                return Ok(await _configService.GetPermissionsAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving permissions: {ex.Message}");
            }
        }


        [HttpGet("user-roles/assignments/{userName}")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<ActionResult<IEnumerable<int>>> GetUserRoleAssignments(string userName)
        {
            try
            {
                var roleIds = await _configService.GetUserRoleAssignmentsAsync(userName);
                return Ok(roleIds);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving user role assignments: {ex.Message}");
            }
        }

        [HttpPost("user-roles/assign")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> AssignUserRoles([FromBody] UserAssignmentRequest request)
        {
            try
            {
                await _configService.AssignUserRolesAsync(request.UserName, request.RoleIds);
                return Ok(new { message = "User roles assigned successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error assigning user roles: {ex.Message}");
            }
        }
        
        [HttpPost("role-permissions/assign")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> AssignRolePermissions([FromBody] RolePermissionAssignmentRequest request)
        {
            if (request == null || request.PermissionIds == null)
                return BadRequest("Role ID and Permission IDs are required");

            try
            {
                await _configService.AssignRolePermissionsAsync(request.RoleId, request.PermissionIds);
                return Ok(new { message = "Role permissions assigned successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error assigning role permissions: {ex.Message}");
            }
        }

        [HttpPost("property-mapping")]
        [Authorize(Roles = "admin,configeditor")]
        public async Task<IActionResult> SavePropertyMapping([FromBody] PropertyMappingRequest request)
        {
            try
            {
                var result = await _configService.SavePropertyMappingAsync(request);
                return result ? Ok() : BadRequest("Failed to save mapping");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("property-mapping/{provider}/{docTypeId}")]
        public async Task<IActionResult> GetPropertyMapping(string provider, int docTypeId)
        {
            try
            {
                var mapping = await _configService.GetPropertyMappingAsync(provider, docTypeId);
                if (mapping == null) return NotFound();
                return Ok(mapping);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

    }
}
