using Server.Controllers;
using Server.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Server.Services.Configuration
{
    public interface IConfigurationService
    {
        Task<IEnumerable<ObjectTypeDto>> GetObjectTypesAsync(string? type = null);
        Task<ObjectTypeDto?> GetObjectTypeByIdAsync(int id);
        Task<ObjectTypeDto> CreateObjectTypeAsync(ObjectTypeDto objType);
        Task UpdateObjectTypeAsync(ObjectTypeDto objType);
        Task DeleteObjectTypeAsync(int id);
        Task<IEnumerable<ObjectTypeDto>> GetChildObjectsAsync(int parentId);
        Task CreateObjectRelationAsync(int parentId, int childId);
        Task<IEnumerable<PropertyDto>> GetPropertiesAsync();
        Task<int> CreatePropertyAsync(PropertyDto property);
        Task SavePropertyAsync(PropertyDto property);
        Task DeletePropertyAsync(int propertyId);
        Task<IEnumerable<DocTypePropertyDto>> GetDocTypePropertiesAsync(int objectId);
        Task SaveDocTypePropertyAsync(DocTypePropertyDto prop);
        Task DeleteDocTypePropertyAsync(int mappingId, bool isBatchProperty);
        Task<IEnumerable<ZoneDto>> GetZonesForDocTypeAsync(int docTypeId);
        Task<int> SaveZoneAsync(ZoneDto zone);
        Task<IEnumerable<IdentificationRuleDto>> GetIdentificationRulesAsync(int docTypeId);
        Task SaveIdentificationRuleAsync(IdentificationRuleDto rule);
        Task DeleteIdentificationRuleAsync(int ruleId);
        Task<IEnumerable<LookupConfigDto>> GetLookupConfigurationsAsync();
        Task<int> SaveLookupConfigurationAsync(LookupConfigDto lookup);
        Task<IEnumerable<DmsConfigDto>> GetDmsConfigurationsAsync();
        Task<DmsConfigDto?> GetDmsConfigurationAsync(int configId);
        Task<int> SaveDmsConfigurationAsync(DmsConfigDto config);
        Task DeleteDmsConfigurationAsync(int configId);
        Task<bool> TestDmsConnectionAsync(int configId);
        Task<string?> GetSampleImageUrlAsync(int docTypeId);
        Task<FormIdSettings?> GetFormIdSettingsAsync(int docTypeId);
        Task SaveFormIdSettingsAsync(FormIdSettings settings);
        Task CommitObjectTypeAsync(int objectTypeId, bool isBatch);

        // NEW: Validation methods
        Task<bool> IsPropertyInUseAsync(int propertyId);
        Task<bool> IsObjectTypeInUseAsync(int objectTypeId);
        Task<IEnumerable<ConfigurationDtos>> GetAllConfigurationsAsync();
        Task<string?> GetConfigurationsValue(string configName);
        Task SaveAllSettingsAsync(ConfigurationSettings settings);
        Task<PathValidationResult> ValidateAndCreatePathAsync(string path, bool createIfNotExists);
        Task<bool> UpdateStepStatusForSeparationModeAsync(string separationMode);
        
        // Zone Configuration Methods
        Task<List<ZoneConfig>> GetAllZonesAsync();
        Task<List<ZoneConfig>> GetZonesByDocumentTypeAsync(int documentTypeId);
        Task<ZoneConfig?> GetZoneByIdAsync(int zoneId);
        Task<bool> CreateZoneAsync(CreateZoneRequest request);
        Task<bool> UpdateZoneAsync(UpdateZoneRequest request);
        Task<bool> DeleteZoneAsync(int zoneId);
        Task<bool> MapPropertyToZoneAsync(int propertyId, int? zoneId, int docTypeId);
        Task<bool> RemoveZoneMappingsAsync(int zoneId);
        
        // DMS Configuration Methods
        Task<IEnumerable<DmsConfigDto>> GetDmsConfigsForDocTypeAsync(int docTypeId);
        Task<IEnumerable<DmsProviderDto>> GetDmsProvidersAsync();
        Task<IEnumerable<DmsOutputFormatDto>> GetDmsOutputFormatsAsync();
        // Property Mapping Methods
        Task<bool> SavePropertyMappingAsync(PropertyMappingRequest request);
        Task<PropertyMappingDto?> GetPropertyMappingAsync(string providerName, int docTypeId);


        // User Role and Permission Methods
        Task<IEnumerable<UserRoleDto>> GetUserRolesAsync();
        Task<IEnumerable<PermissionDto>> GetPermissionsAsync();
        Task AssignRolePermissionsAsync(int roleId, List<int> permissionIds);
        Task<IEnumerable<int>> GetUserRoleAssignmentsAsync(string userName);
        Task AssignUserRolesAsync(string userName, List<int> roleIds);
    }
}