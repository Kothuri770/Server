using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Server.Controllers;
using Server.Models;
using Server.Repositories;
using Server.Services.DMS;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Server.Services.Configuration
{
    public partial class ConfigurationService : BaseRepository, IConfigurationService
    {
        [GeneratedRegex(@"^[a-zA-Z\s]+$")]
        private static partial Regex AlphaSpacesRegex();

        private readonly DmsConnectorManager _dmsConnectorManager;
        private readonly IMemoryCache _cache;
        
        public ConfigurationService(string connectionString, string provider, DmsConnectorManager dmsConnectorManager, IMemoryCache cache) : base(connectionString, provider) 
        { 
            _dmsConnectorManager = dmsConnectorManager;
            _cache = cache;
        }

        private async Task<T> ExecuteWithTransactionAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> operation, string errorMessage)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();
            try
            {
                var result = await operation(conn, transaction);
                transaction.Commit();
                return result;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new InvalidOperationException($"{errorMessage}: {ex.Message}", ex);
            }
        }

        public async Task<IEnumerable<ObjectTypeDto>> GetObjectTypesAsync(string? type = null)
        {
            string cacheKey = $"ObjectTypes_{type ?? "All"}";
            if (_cache.TryGetValue(cacheKey, out IEnumerable<ObjectTypeDto>? cachedTypes) && cachedTypes != null)
                return cachedTypes;

            using var conn = CreateConnection();
            const string sql = @"
                SELECT id, type, name as ObjectName, IsActive, OcrConnectorId, OcrMode, SeparationMode
                FROM objecttypes 
                WHERE (@Type IS NULL OR type = @Type) 
                ORDER BY type, name";
            var result = await conn.QueryAsync<ObjectTypeDto>(sql, new { Type = type });
            
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
            return result;
        }

        public async Task<ObjectTypeDto?> GetObjectTypeByIdAsync(int id)
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT id, type, name as ObjectName, IsActive, OcrConnectorId, OcrMode, SeparationMode
                FROM objecttypes 
                WHERE id = @Id";
            return await conn.QuerySingleOrDefaultAsync<ObjectTypeDto>(sql, new { Id = id });
        }

        public Task<ObjectTypeDto> CreateObjectTypeAsync(ObjectTypeDto objType)
        {
            if (string.IsNullOrWhiteSpace(objType.ObjectName))
                throw new ArgumentException("Object name is required", nameof(objType));
            
            // Validate that the name contains only letters and spaces, no numbers or special characters
            if (!AlphaSpacesRegex().IsMatch(objType.ObjectName))
                throw new ArgumentException("Object name can only contain letters and spaces, no numbers or special characters allowed", nameof(objType));

            return CreateObjectTypeInternalAsync(objType);
        }

        private async Task<ObjectTypeDto> CreateObjectTypeInternalAsync(ObjectTypeDto objType)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                INSERT INTO objecttypes (type, name, IsActive, OcrConnectorId, OcrMode, SeparationMode) 
                OUTPUT INSERTED.id
                VALUES (@Type, @ObjectName, @IsActive, @OcrConnectorId, @OcrMode, @SeparationMode)";
            }
            else
            {
                sql = @"
                INSERT INTO objecttypes (type, name, IsActive, OcrConnectorId, OcrMode, SeparationMode) 
                VALUES (@Type, @ObjectName, @IsActive, @OcrConnectorId, @OcrMode, @SeparationMode) 
                RETURNING id";
            }

            objType.Id = await conn.ExecuteScalarAsync<int>(sql, objType);
            _cache.Remove($"ObjectTypes_{objType.Type}");
            _cache.Remove("ObjectTypes_All");
            return objType;
        }

        public async Task UpdateObjectTypeAsync(ObjectTypeDto objType)
        {
            using var conn = CreateConnection();
            const string sql = "UPDATE objecttypes SET name = @ObjectName, OcrConnectorId = @OcrConnectorId, OcrMode = @OcrMode, SeparationMode = @SeparationMode WHERE id = @Id";
            await conn.ExecuteAsync(sql, objType);
            _cache.Remove($"ObjectTypes_{objType.Type}");
            _cache.Remove("ObjectTypes_All");
        }

        public async Task DeleteObjectTypeAsync(int id)
        {
            // Check if object type is blocked from deletion
            var blockReason = await GetDeletionBlockReasonAsync(id);
            if (blockReason != null)
                throw new InvalidOperationException(blockReason);

            using var conn = CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                await DeleteObjectTypeInternalAsync(id, conn, transaction);
                transaction.Commit();
                _cache.Remove("ObjectTypes_B");
                _cache.Remove("ObjectTypes_D");
                _cache.Remove("ObjectTypes_All");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                throw new InvalidOperationException($"Failed to delete object type {id}: {ex.Message}", ex);
            }
        }

        private async Task DeleteObjectTypeInternalAsync(int id, IDbConnection conn, IDbTransaction transaction)
        {
            // 1. Get object type info
            var objType = await conn.QuerySingleOrDefaultAsync<ObjectTypeDto>(
                "SELECT * FROM objecttypes WHERE id = @Id", new { Id = id }, transaction);

            if (objType == null) return;

            // 2. Delete identification rules FIRST (before objectrelation is deleted)
            //    parentobjectid in identification directly holds the doc/batch type ID
            await conn.ExecuteAsync(
                "DELETE FROM identification WHERE parentobjectid = @Id",
                new { Id = id }, transaction);

            // 3. Delete all relationships
            await conn.ExecuteAsync(
                "DELETE FROM objectrelation WHERE parentobjectid = @Id OR childobjectid = @Id",
                new { Id = id }, transaction);

            // 4. Delete type-specific details
            if (objType.Type == "B")
            {
                await conn.ExecuteAsync(
                    "DELETE FROM batchclassdetail WHERE batchtypeid = @Id",
                    new { Id = id }, transaction);
            }
            else
            {
                // Document type comprehensive cleanup
                await conn.ExecuteAsync(
                    "DELETE FROM documentclassdetail WHERE doctypeid = @Id",
                    new { Id = id }, transaction);

                await conn.ExecuteAsync(
                    "DELETE FROM documentsample WHERE doctypeid = @Id",
                    new { Id = id }, transaction);

                await conn.ExecuteAsync(
                    "DELETE FROM dmsclassmapping WHERE doctypeid = @Id",
                    new { Id = id }, transaction);

                // Delete character zones before zones (to avoid FK issues if present)
                await conn.ExecuteAsync(
                    "DELETE FROM characterzones WHERE zoneid IN (SELECT id FROM zones WHERE doctypeid = @Id)",
                    new { Id = id }, transaction);

                await conn.ExecuteAsync(
                    "DELETE FROM zones WHERE doctypeid = @Id",
                    new { Id = id }, transaction);

                // Note: 'document' table is merged into BatchDetail in SQL Server
                await conn.ExecuteAsync(
                    "DELETE FROM ocrresults WHERE docid IN (SELECT bd.id FROM BatchDetail bd WHERE bd.doctypeid = @Id)",
                    new { Id = id }, transaction);
            }

            // 5. Finally delete the object type
            await conn.ExecuteAsync(
                "DELETE FROM objecttypes WHERE id = @Id",
                new { Id = id }, transaction);
        }
        public async Task<IEnumerable<ObjectTypeDto>> GetChildObjectsAsync(int parentId)
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT o.id, o.type, o.name as ObjectName, o.IsActive, o.SeparationMode
                FROM objecttypes o
                INNER JOIN objectrelation r ON o.id = r.childobjectid
                WHERE r.parentobjectid = @ParentId
                ORDER BY o.name";
            return await conn.QueryAsync<ObjectTypeDto>(sql, new { ParentId = parentId });
        }
        public async Task CreateObjectRelationAsync(int parentId, int childId)
        {

            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    IF NOT EXISTS (SELECT 1 FROM objectrelation WHERE parentobjectid = @ParentId AND childobjectid = @ChildId)
                    BEGIN
                        INSERT INTO objectrelation (parentobjectid, childobjectid) 
                        VALUES (@ParentId, @ChildId)
                    END";
            }
            else
            {
                sql = @"
                    INSERT INTO objectrelation (parentobjectid, childobjectid) 
                    VALUES (@ParentId, @ChildId)
                    ON CONFLICT (parentobjectid, childobjectid) DO NOTHING";
            }
            await conn.ExecuteAsync(sql, new { ParentId = parentId, ChildId = childId });
        }

        public async Task<IEnumerable<PropertyDto>> GetPropertiesAsync()
        {
            const string cacheKey = "AllProperties";
            if (_cache.TryGetValue(cacheKey, out IEnumerable<PropertyDto>? cachedProps) && cachedProps != null)
                return cachedProps;

            using var conn = CreateConnection();
            var result = await conn.QueryAsync<PropertyDto>("SELECT id, propertyname as PropertyName, propertydesc as PropertyDesc, propertytype as PropertyType, propertylength as PropertyLength, lookupid as LookupId FROM property ORDER BY propertyname");
            
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
            return result;
        }

        public Task<int> CreatePropertyAsync(PropertyDto property)
        {
            // Validate that the property name contains only letters and spaces, no numbers or special characters
            if (!string.IsNullOrWhiteSpace(property.PropertyName) && !AlphaSpacesRegex().IsMatch(property.PropertyName))
                throw new ArgumentException("Property name can only contain letters and spaces, no numbers or special characters allowed", nameof(property));
            
            return CreatePropertyInternalAsync(property);
        }

        private async Task<int> CreatePropertyInternalAsync(PropertyDto property)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    INSERT INTO property (propertyname, propertydesc, propertytype, propertylength, lookupid)
                    OUTPUT INSERTED.id
                    VALUES (@PropertyName, @PropertyDesc, @PropertyType, @PropertyLength, @LookupId)";
            }
            else
            {
                sql = @"
                    INSERT INTO property (propertyname, propertydesc, propertytype, propertylength, lookupid)
                    VALUES (@PropertyName, @PropertyDesc, @PropertyType, @PropertyLength, @LookupId)
                    RETURNING id";
            }
            var id = await conn.ExecuteScalarAsync<int>(sql, property);
            _cache.Remove("AllProperties");
            return id;
        }

        public Task SavePropertyAsync(PropertyDto property)
        {
            // Validate that the property name contains only letters and spaces, no numbers or special characters
            if (!string.IsNullOrWhiteSpace(property.PropertyName) && !AlphaSpacesRegex().IsMatch(property.PropertyName))
                throw new ArgumentException("Property name can only contain letters and spaces, no numbers or special characters allowed", nameof(property));
            
            return SavePropertyInternalAsync(property);
        }

        private async Task SavePropertyInternalAsync(PropertyDto property)
        {
            using var conn = CreateConnection();
            const string sql = @"
                UPDATE property SET propertyname = @PropertyName, propertydesc = @PropertyDesc, 
                propertytype = @PropertyType, propertylength = @PropertyLength, lookupid = @LookupId
                WHERE id = @Id";
            await conn.ExecuteAsync(sql, property);
            _cache.Remove("AllProperties");
        }

        public async Task DeletePropertyAsync(int propertyId)
        {
            if (await IsPropertyInUseAsync(propertyId))
                throw new InvalidOperationException("Cannot delete property: Property is currently mapped to one or more batch/document types.");

            using var conn = CreateConnection();
            await conn.ExecuteAsync("DELETE FROM property WHERE id = @Id", new { Id = propertyId });
            _cache.Remove("AllProperties");
        }

        public async Task<IEnumerable<DocTypePropertyDto>> GetDocTypePropertiesAsync(int objectId)
        {
            using var conn = CreateConnection();
            var type = await conn.QuerySingleOrDefaultAsync<string>("SELECT type FROM objecttypes WHERE id = @Id", new { Id = objectId });

            if (type == "B")
            {
                const string batchSql = @"
                    SELECT bcd.id as Id, bcd.batchtypeid as DocTypeId, bcd.propertyid as PropertyId, p.propertyname as PropertyName, p.propertytype as PropertyType,
                           bcd.isenabled as IsEnabled, bcd.isrequired as IsRequired, bcd.zoneid as ZoneId, bcd.length as Length, bcd.lookupid as LookupId,
                           1 as IsBatchProperty, bcd.propertyorder as PropertyOrder, p.propertydesc as PropertyDesc, bcd.defaultvalue as DefaultValue
                    FROM batchclassdetail bcd
                    JOIN property p ON bcd.propertyid = p.id
                    WHERE bcd.batchtypeid = @Id
                    ORDER BY bcd.propertyorder";
                return await conn.QueryAsync<DocTypePropertyDto>(batchSql, new { Id = objectId });
            }
            else
            {
                const string docSql = @"
                    SELECT dcd.id as Id, dcd.doctypeid as DocTypeId, dcd.propertyid as PropertyId, p.propertyname as PropertyName, p.propertytype as PropertyType,
                           dcd.isenabled as IsEnabled, dcd.isrequired as IsRequired, dcd.zoneid as ZoneId, dcd.length as Length, dcd.lookupid as LookupId,
                           dcd.isbatchproperty as IsBatchProperty, dcd.propertyorder as PropertyOrder, p.propertydesc as PropertyDesc, dcd.defaultvalue as DefaultValue
                    FROM documentclassdetail dcd
                    JOIN property p ON dcd.propertyid = p.id
                    WHERE dcd.doctypeid = @Id
                    ORDER BY dcd.propertyorder";
                return await conn.QueryAsync<DocTypePropertyDto>(docSql, new { Id = objectId });
            }
        }

        public Task SaveDocTypePropertyAsync(DocTypePropertyDto prop)
        {
            if (prop.DocTypeId <= 0 || prop.PropertyId <= 0)
                throw new ArgumentException("Valid DocTypeId and PropertyId are required");

            return SaveDocTypePropertyInternalAsync(prop);
        }

        private async Task SaveDocTypePropertyInternalAsync(DocTypePropertyDto prop)
        {
            using var conn = CreateConnection();
            var existingId = await GetMappingIdAsync(conn, prop);

            if (existingId.HasValue)
            {
                await UpdatePropertyMappingAsync(conn, prop, existingId.Value);
            }
            else
            {
                await InsertPropertyMappingAsync(conn, prop);
            }

            await UpdateDynamicTableForPropertyChange(prop.DocTypeId, prop.PropertyId, prop.IsBatchProperty, true);
        }

        private async Task<int?> GetMappingIdAsync(IDbConnection conn, DocTypePropertyDto prop)
        {
            string checkSql = prop.IsBatchProperty
                ? "SELECT id FROM batchclassdetail WHERE batchtypeid = @DocTypeId AND propertyid = @PropertyId"
                : "SELECT id FROM documentclassdetail WHERE doctypeid = @DocTypeId AND propertyid = @PropertyId";

            return await conn.QuerySingleOrDefaultAsync<int?>(checkSql, new { DocTypeId = prop.DocTypeId, PropertyId = prop.PropertyId });
        }

        private async Task UpdatePropertyMappingAsync(IDbConnection conn, DocTypePropertyDto prop, int id)
        {
            string updateSql = prop.IsBatchProperty
                ? @"UPDATE batchclassdetail SET propertyorder = @PropertyOrder, isrequired = @IsRequired, zoneid = @ZoneId, length = @Length, lookupid = @LookupId, isenabled = @IsEnabled, defaultvalue = @DefaultValue WHERE id = @Id"
                : @"UPDATE documentclassdetail SET propertyorder = @PropertyOrder, isenabled = @IsEnabled, isrequired = @IsRequired, zoneid = @ZoneId, length = @Length, lookupid = @LookupId, isbatchproperty = @IsBatchProperty, defaultvalue = @DefaultValue WHERE id = @Id";

            await conn.ExecuteAsync(updateSql, new
            {
                prop.PropertyOrder,
                prop.IsEnabled,
                prop.IsRequired,
                prop.ZoneId,
                prop.Length,
                prop.LookupId,
                prop.IsBatchProperty,
                prop.DefaultValue,
                Id = id
            });
        }

        private async Task InsertPropertyMappingAsync(IDbConnection conn, DocTypePropertyDto prop)
        {
            string insertSql = prop.IsBatchProperty
                ? @"INSERT INTO batchclassdetail (batchtypeid, propertyid, propertyorder, ispreindex, isrequired, zoneid, length, lookupid, isenabled, defaultvalue) VALUES (@DocTypeId, @PropertyId, @PropertyOrder, @IsPreIndex, @IsRequired, @ZoneId, @Length, @LookupId, @IsEnabled, @DefaultValue)"
                : @"INSERT INTO documentclassdetail (doctypeid, propertyid, propertyorder, isenabled, isrequired, zoneid, length, lookupid, isbatchproperty, defaultvalue) VALUES (@DocTypeId, @PropertyId, @PropertyOrder, @IsEnabled, @IsRequired, @ZoneId, @Length, @LookupId, @IsBatchProperty, @DefaultValue)";

            await conn.ExecuteAsync(insertSql, new
            {
                prop.DocTypeId,
                prop.PropertyId,
                prop.PropertyOrder,
                prop.IsEnabled,
                prop.IsRequired,
                prop.ZoneId,
                prop.Length,
                prop.LookupId,
                prop.IsPreIndex,
                prop.IsBatchProperty,
                prop.DefaultValue
            });
        }

        public async Task DeleteDocTypePropertyAsync(int mappingId, bool isBatchProperty)
        {
            // First get the property details before deleting
            using var conn = CreateConnection();
            
            string selectSql = isBatchProperty
                ? "SELECT batchtypeid as DocTypeId, propertyid as PropertyId FROM batchclassdetail WHERE id = @Id"
                : "SELECT doctypeid as DocTypeId, propertyid as PropertyId FROM documentclassdetail WHERE id = @Id";
                
            var propertyDetails = await conn.QuerySingleOrDefaultAsync<(int DocTypeId, int PropertyId)>(selectSql, new { Id = mappingId });
            
            if (propertyDetails == default)
                throw new KeyNotFoundException($"Property mapping with ID {mappingId} not found");

            string sql = isBatchProperty
                ? "DELETE FROM batchclassdetail WHERE id = @Id"
                : "DELETE FROM documentclassdetail WHERE id = @Id";

            var affected = await conn.ExecuteAsync(sql, new { Id = mappingId });
            if (affected == 0)
                throw new KeyNotFoundException($"Property mapping with ID {mappingId} not found");
                
            // Automatically update dynamic tables when properties are removed
            await UpdateDynamicTableForPropertyChange(propertyDetails.DocTypeId, propertyDetails.PropertyId, isBatchProperty, false);
        }

        public async Task<IEnumerable<ZoneDto>> GetZonesForDocTypeAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            return await conn.QueryAsync<ZoneDto>("SELECT * FROM zones WHERE doctypeid = @DocTypeId ORDER BY pageno, name", new { DocTypeId = docTypeId });
        }

        public async Task<int> SaveZoneAsync(ZoneDto zone)
        {
            using var conn = CreateConnection();
            if (zone.ID > 0)
            {
                 string sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) 
                    ? @"UPDATE zones SET name=@Name, leftx=@LeftX, topy=@TopY, rightx=@RightX, bottomy=@BottomY, 
                        pageno=@PageNo, type=@Type, startposition=@StartPosition, length=@Length, 
                        DisplayedWidth=@DisplayedWidth, DisplayedHeight=@DisplayedHeight 
                        OUTPUT INSERTED.id
                        WHERE id=@ID"
                    : @"UPDATE zones SET name=@Name, leftx=@LeftX, topy=@TopY, rightx=@RightX, bottomy=@BottomY, 
                        pageno=@PageNo, type=@Type, startposition=@StartPosition, length=@Length, 
                        DisplayedWidth=@DisplayedWidth, DisplayedHeight=@DisplayedHeight 
                        WHERE id=@ID RETURNING id";
                return await conn.ExecuteScalarAsync<int>(sql, zone);
            }
            
            string sqlInsert;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sqlInsert = @"INSERT INTO zones (name, leftx, topy, rightx, bottomy, doctypeid, pageno, type, startposition, length, DisplayedWidth, DisplayedHeight) 
                    OUTPUT INSERTED.id
                    VALUES (@Name, @LeftX, @TopY, @RightX, @BottomY, @DocTypeID, @PageNo, @Type, @StartPosition, @Length, @DisplayedWidth, @DisplayedHeight)";
            }
            else
            {
                sqlInsert = @"INSERT INTO zones (name, leftx, topy, rightx, bottomy, doctypeid, pageno, type, startposition, length, DisplayedWidth, DisplayedHeight) 
                    VALUES (@Name, @LeftX, @TopY, @RightX, @BottomY, @DocTypeID, @PageNo, @Type, @StartPosition, @Length, @DisplayedWidth, @DisplayedHeight)
                    RETURNING id";
            }
            return await conn.ExecuteScalarAsync<int>(sqlInsert, zone);
        }

        public async Task<IEnumerable<IdentificationRuleDto>> GetIdentificationRulesAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT i.id as ID, i.idtype as RuleType, i.idmethod as Method, i.idvalue as Value, 
                       i.parentobjectid as DocTypeId, i.zoneid as ZoneId, i.discardpage as DiscardPage, z.name as ZoneName 
                FROM identification i 
                LEFT JOIN zones z ON i.zoneid = z.id 
                WHERE i.parentobjectid = @DocTypeId
                ORDER BY i.idmethod, i.idvalue";
            return await conn.QueryAsync<IdentificationRuleDto>(sql, new { DocTypeId = docTypeId });
        }

        public async Task SaveIdentificationRuleAsync(IdentificationRuleDto rule)
        {
            using var conn = CreateConnection();
            if (rule.ID > 0)
            {
                const string sql = @"UPDATE identification SET idtype=@RuleType, idmethod=@Method, idvalue=@Value, 
                    zoneid=@ZoneId, discardpage=@DiscardPage WHERE id=@ID";
                await conn.ExecuteAsync(sql, rule);
            }
            else
            {
                const string sql = @"INSERT INTO identification (idtype, idmethod, idvalue, parentobjectid, zoneid, discardpage) 
                    VALUES (@RuleType, @Method, @Value, @DocTypeId, @ZoneId, @DiscardPage)";
                await conn.ExecuteAsync(sql, rule);
            }
        }

        public async Task DeleteIdentificationRuleAsync(int ruleId)
        {
            using var conn = CreateConnection();
            await conn.ExecuteAsync("DELETE FROM identification WHERE id = @Id", new { Id = ruleId });
        }

        public async Task<IEnumerable<LookupConfigDto>> GetLookupConfigurationsAsync() =>
            await CreateConnection().QueryAsync<LookupConfigDto>("SELECT id, lookupstr as LookupString, lookuptype as LookupType, connectstr as ConnectionString FROM lookup");

        public async Task<int> SaveLookupConfigurationAsync(LookupConfigDto lookup)
        {
            using var conn = CreateConnection();
            if (lookup.Id > 0)
            {
                string sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? @"UPDATE lookup SET lookupstr=@LookupString, lookuptype=@LookupType, connectstr=@ConnectionString OUTPUT INSERTED.id WHERE id=@Id"
                    : @"UPDATE lookup SET lookupstr=@LookupString, lookuptype=@LookupType, connectstr=@ConnectionString WHERE id=@Id RETURNING id";
                return await conn.ExecuteScalarAsync<int>(sql, lookup);
            }
            
            string sqlInsert;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sqlInsert = @"INSERT INTO lookup (lookupstr, lookuptype, connectstr) OUTPUT INSERTED.id VALUES (@LookupString, @LookupType, @ConnectionString)";
            }
            else
            {
                sqlInsert = @"INSERT INTO lookup (lookupstr, lookuptype, connectstr) VALUES (@LookupString, @LookupType, @ConnectionString) RETURNING id";
            }
            return await conn.ExecuteScalarAsync<int>(sqlInsert, lookup);
        }

        public async Task<IEnumerable<DmsConfigDto>> GetDmsConfigurationsAsync()
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT 
                    dc.id as ConfigId,
                    dc.doctypeid as DocTypeID,
                    dc.providerid as ProviderId,
                    dc.outputformatid as OutputFormatId,
                    dc.dmsclassname as DMSClassName,
                    dc.dmscabinetname as DMSCabinetName,
                    dc.releasefolder as ReleaseFolder,
                    dc.nameexpression as NameExpression,
                    dc.url as Url,
                    dc.username as Username,
                    dc.password as Password,
                    dc.additionalconfig as AdditionalConfig,
                    dc.isactive as IsActive,
                    dp.name as ProviderName,
                    dp.displayname as ProviderDisplayName,
                    dof.formatcode as OutputFormatCode,
                    dof.formatname as OutputFormatName
                FROM dmsconnectors dc
                LEFT JOIN dmsproviders dp ON dc.providerid = dp.id
                LEFT JOIN dmsoutputformats dof ON dc.outputformatid = dof.id
                ORDER BY dc.id";
            return await conn.QueryAsync<DmsConfigDto>(sql);
        }

        public async Task<IEnumerable<DmsConfigDto>> GetDmsConfigsForDocTypeAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            var isActiveFilter = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "true";
            var sql = $@"
                SELECT 
                    dc.id as ConfigId,
                    dc.doctypeid as DocTypeID,
                    dc.providerid as ProviderId,
                    dc.outputformatid as OutputFormatId,
                    dc.dmsclassname as DMSClassName,
                    dc.dmscabinetname as DMSCabinetName,
                    dc.releasefolder as ReleaseFolder,
                    dc.nameexpression as NameExpression,
                    dc.url as Url,
                    dc.username as Username,
                    dc.password as Password,
                    dc.additionalconfig as AdditionalConfig,
                    dc.isactive as IsActive,
                    dp.name as ProviderName,
                    dp.displayname as ProviderDisplayName,
                    dof.formatcode as OutputFormatCode,
                    dof.formatname as OutputFormatName
                FROM dmsconnectors dc
                LEFT JOIN dmsproviders dp ON dc.providerid = dp.id
                LEFT JOIN dmsoutputformats dof ON dc.outputformatid = dof.id
                WHERE dc.doctypeid = @DocTypeId AND dc.isactive = {isActiveFilter}
                ORDER BY dc.id";
            return await conn.QueryAsync<DmsConfigDto>(sql, new { DocTypeId = docTypeId });
        }

        public async Task<DmsConfigDto?> GetDmsConfigurationAsync(int configId)
        {
            using var conn = CreateConnection();
            var sql = @"
                SELECT 
                    dc.id as ConfigId,
                    dc.doctypeid as DocTypeID,
                    dc.providerid as ProviderId,
                    dc.outputformatid as OutputFormatId,
                    dc.dmsclassname as DMSClassName,
                    dc.dmscabinetname as DMSCabinetName,
                    dc.releasefolder as ReleaseFolder,
                    dc.nameexpression as NameExpression,
                    dc.url as Url,
                    dc.username as Username,
                    dc.password as Password,
                    dc.additionalconfig as AdditionalConfig,
                    dc.isactive as IsActive,
                    dp.name as ProviderName,
                    dp.displayname as ProviderDisplayName,
                    dof.formatcode as OutputFormatCode,
                    dof.formatname as OutputFormatName
                FROM dmsconnectors dc
                LEFT JOIN dmsproviders dp ON dc.providerid = dp.id
                LEFT JOIN dmsoutputformats dof ON dc.outputformatid = dof.id
                WHERE dc.id = @Id";
            return await conn.QuerySingleOrDefaultAsync<DmsConfigDto>(sql, new { Id = configId });
        }

        public async Task<int> SaveDmsConfigurationAsync(DmsConfigDto config)
        {
            try
            {
                using var conn = CreateConnection();
                await HandleExistingDmsConfigAsync(conn, config);
                        
                if (config.ConfigId > 0)
                {
                    return await UpdateDmsConfigInternalAsync(conn, config);
                }
                
                return await InsertDmsConfigInternalAsync(conn, config);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async Task HandleExistingDmsConfigAsync(IDbConnection conn, DmsConfigDto config)
        {
            var isActiveVal = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "TRUE";
            string checkExistingSql = $"SELECT id FROM dmsconnectors WHERE doctypeid = @DocTypeID AND isactive = {isActiveVal} AND id != @ConfigId";
            var existingConfig = await conn.QuerySingleOrDefaultAsync<int?>(checkExistingSql, new { config.DocTypeID, config.ConfigId });
                    
            if (existingConfig.HasValue)
            {
                var isInactiveVal = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "0" : "FALSE";
                string deactivateSql = $"UPDATE dmsconnectors SET isactive = {isInactiveVal} WHERE id = @ExistingConfigId";
                await conn.ExecuteAsync(deactivateSql, new { ExistingConfigId = existingConfig.Value });
            }
        }

        private async Task<int> UpdateDmsConfigInternalAsync(IDbConnection conn, DmsConfigDto config)
        {
            const string checkSql = "SELECT COUNT(*) FROM dmsconnectors WHERE id = @ConfigId";
            var exists = await conn.QuerySingleAsync<int>(checkSql, new { ConfigId = config.ConfigId }) > 0;
                    
            if (!exists)
            {
                throw new KeyNotFoundException($"DMS Configuration with ID {config.ConfigId} not found");
            }
                    
            string jsonType = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "NVARCHAR(MAX)" : "jsonb";
            string sql = $@"UPDATE dmsconnectors SET doctypeid=@DocTypeID, providerid=@ProviderId, outputformatid=@OutputFormatId, dmsclassname=@DMSClassName, dmscabinetname=@DMSCabinetName, releasefolder=@ReleaseFolder, nameexpression=@NameExpression, url=@Url, username=@Username, password=@Password, additionalconfig=CAST(@AdditionalConfig AS {jsonType}), isactive=@IsActive WHERE id=@ConfigId";
            var rowsAffected = await conn.ExecuteAsync(sql, config);
                    
            if (rowsAffected == 0)
            {
                throw new InvalidOperationException($"Failed to update DMS Configuration with ID {config.ConfigId}");
            }
                    
            return config.ConfigId;
        }

        private async Task<int> InsertDmsConfigInternalAsync(IDbConnection conn, DmsConfigDto config)
        {
            string sqlInsert;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sqlInsert = @"INSERT INTO dmsconnectors (doctypeid, providerid, outputformatid, dmsclassname, dmscabinetname, releasefolder, nameexpression, url, username, password, additionalconfig, isactive) 
                    OUTPUT INSERTED.id
                    VALUES (@DocTypeID, @ProviderId, @OutputFormatId, @DMSClassName, @DMSCabinetName, @ReleaseFolder, @NameExpression, @Url, @Username, @Password, @AdditionalConfig, @IsActive)";
            }
            else
            {
                sqlInsert = @"INSERT INTO dmsconnectors (doctypeid, providerid, outputformatid, dmsclassname, dmscabinetname, releasefolder, nameexpression, url, username, password, additionalconfig, isactive) 
                    VALUES (@DocTypeID, @ProviderId, @OutputFormatId, @DMSClassName, @DMSCabinetName, @ReleaseFolder, @NameExpression, @Url, @Username, @Password, CAST(@AdditionalConfig AS jsonb), @IsActive) 
                    RETURNING id";
            }
            return await conn.ExecuteScalarAsync<int>(sqlInsert, config);
        }

        public async Task DeleteDmsConfigurationAsync(int configId) =>
            await CreateConnection().ExecuteAsync("DELETE FROM dmsconnectors WHERE id = @Id", new { Id = configId });

        public async Task<bool> TestDmsConnectionAsync(int configId)
        {
            try
            {
                // Get the DMS configuration by ID
                var config = await GetDmsConfigurationAsync(configId);
                if (config == null)
                {
                    throw new ArgumentException($"DMS configuration with ID {configId} not found");
                }
                
                // Get the provider details to determine which connector to use
                var provider = await GetDmsProviderByIdAsync(config.ProviderId);
                if (provider == null)
                {
                    throw new ArgumentException($"DMS provider with ID {config.ProviderId} not found");
                }
                
                // Get the appropriate connector based on provider type
                var connector = _dmsConnectorManager.GetConnector(provider.Name);
                if (connector == null)
                {
                    throw new ArgumentException($"No connector found for provider type: {provider.Name}");
                }
                
                // Test the connection using the connector
                return await connector.TestConnectionAsync(config);
            }
            catch (Exception)
            {
                
                return false;
            }
        }

        public async Task<string?> GetSampleImageUrlAsync(int docTypeId) =>
            await CreateConnection().QuerySingleOrDefaultAsync<string?>("SELECT samplefile as SampleFile FROM documentsample WHERE doctypeid = @DocTypeId", new { DocTypeId = docTypeId });

        public async Task<FormIdSettings?> GetFormIdSettingsAsync(int docTypeId)
        {
            using var conn = CreateConnection();
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    SELECT TOP 1 dt.id as DocTypeId, p.id as PropertyId, 
                           CASE WHEN p.id IS NOT NULL THEN 1 ELSE 0 END as IsEnabled
                    FROM objecttypes dt
                    LEFT JOIN property p ON dt.formidpropertyid = p.id
                    WHERE dt.id = @DocTypeId";
            }
            else
            {
                sql = @"
                    SELECT dt.id as DocTypeId, p.id as PropertyId, 
                           CASE WHEN p.id IS NOT NULL THEN 1 ELSE 0 END as IsEnabled
                    FROM objecttypes dt
                    LEFT JOIN property p ON dt.formidpropertyid = p.id
                    WHERE dt.id = @DocTypeId
                    LIMIT 1";
            }
            return await conn.QuerySingleOrDefaultAsync<FormIdSettings>(sql, new { DocTypeId = docTypeId });
        }

        public async Task SaveFormIdSettingsAsync(FormIdSettings settings)
        {
            using var conn = CreateConnection();
            const string sql = @"
                UPDATE objecttypes 
                SET formidpropertyid = @PropertyId 
                WHERE id = @DocTypeId";

            var affected = await conn.ExecuteAsync(sql, settings);
            if (affected == 0)
                throw new KeyNotFoundException($"Document type {settings.DocTypeId} not found");
        }

        public async Task CommitObjectTypeAsync(int objectTypeId, bool isBatch)
        {
            await ExecuteWithTransactionAsync(async (conn, transaction) =>
            {
                // 1. Mark as committed
                await conn.ExecuteAsync(
                    "UPDATE objecttypes SET Committed = true WHERE id = @Id",
                    new { Id = objectTypeId }, transaction);

                // 2. Get properties
                var properties = await GetDocTypePropertiesAsync(objectTypeId);

                // 3. Create dynamic table
                await CreateDynamicTableAsync(isBatch ? "batchtable" : "doctable", objectTypeId, properties, conn, transaction);

                // 4. If document type, ensure it inherits batch properties if relation exists
                if (!isBatch)
                {
                    var batchId = await conn.QuerySingleOrDefaultAsync<int?>(
                        "SELECT parentobjectid FROM objectrelation WHERE childobjectid = @DocTypeId",
                        new { DocTypeId = objectTypeId }, transaction);

                    if (batchId.HasValue)
                    {
                        await InheritBatchPropertiesToDocTypeAsync(batchId.Value, objectTypeId, conn, transaction);
                    }
                }

                return true;
            }, $"Failed to commit object type {objectTypeId}");
        }

        private async Task CreateDynamicTableAsync(string prefix, int id, IEnumerable<DocTypePropertyDto> properties, IDbConnection conn, IDbTransaction transaction)
        {
            var tableName = $"{prefix}_{id}";
            var propertyList = properties.ToList();
            var pkColumn = prefix == "batchtable" ? "batchid" : "docid";

            var columns = new List<string> { $"{pkColumn} INTEGER PRIMARY KEY" };

            foreach (var prop in propertyList)
            {
                var colName = $"column_{prop.PropertyId}";
                var dataType = GetSqlDataType(prop.PropertyType, prop.Length);
                columns.Add($"{colName} {dataType}");
            }

            // Drop existing table if exists
            var dropSql = $"DROP TABLE IF EXISTS {tableName}";
            await conn.ExecuteAsync(dropSql, transaction: transaction);

            // Create new table
            var createSql = $"CREATE TABLE {tableName} ({string.Join(", ", columns)})";
            await conn.ExecuteAsync(createSql, transaction: transaction);

            // Create index on primary key for better performance
            var indexSql = $"CREATE INDEX idx_{tableName}_{pkColumn} ON {tableName}({pkColumn})";
            await conn.ExecuteAsync(indexSql, transaction: transaction);
        }

        private async Task InheritBatchPropertiesToDocTypeAsync(int batchTypeId, int docTypeId, IDbConnection conn, IDbTransaction transaction)
        {
            // Get batch properties that are not already mapped to document
            const string sql = @"
                INSERT INTO documentclassdetail (doctypeid, propertyid, propertyorder, isenabled, isrequired, 
                    zoneid, length, lookupid, isbatchproperty)
                SELECT @DocTypeId, bcd.propertyid, bcd.propertyorder, true, bcd.isrequired, 
                       null, bcd.length, bcd.lookupid, true
                FROM batchclassdetail bcd
                WHERE bcd.batchtypeid = @BatchTypeId
                AND NOT EXISTS (
                    SELECT 1 FROM documentclassdetail dcd 
                    WHERE dcd.doctypeid = @DocTypeId AND dcd.propertyid = bcd.propertyid
                )";

            await conn.ExecuteAsync(sql, new { BatchTypeId = batchTypeId, DocTypeId = docTypeId }, transaction);
        }

        private async Task UpdateDynamicTableForPropertyChange(int objectTypeId, int propertyId, bool isBatchProperty, bool isAdding)
        {
            try
            {
                using var conn = CreateConnection();
                
                // Get the table name
                var tableName = isBatchProperty ? $"batchtable_{objectTypeId}" : $"doctable_{objectTypeId}";
                
                // Check if table exists
                string checkTableSql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? "SELECT CAST(COUNT(*) AS BIT) FROM sys.tables WHERE name = @TableName"
                    : "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = @TableName)";
                
                var tableExists = await conn.QuerySingleOrDefaultAsync<bool>(checkTableSql, new { TableName = tableName });
                    
                if (!tableExists)
                {
                    await CreateDynamicTableWithCurrentProperties(conn, objectTypeId, isBatchProperty, tableName);
                }
                else
                {
                    if (isAdding)
                    {
                        await AddColumnToDynamicTableAsync(conn, tableName, propertyId);
                    }
                    else
                    {
                        await DropColumnFromDynamicTableAsync(conn, tableName, propertyId);
                    }
                }
            }
            catch
            {
                // Error is suppressed — dynamic table updates are non-critical
            }
        }

        private async Task AddColumnToDynamicTableAsync(IDbConnection conn, string tableName, int propertyId)
        {
            var property = await conn.QuerySingleOrDefaultAsync<PropertyDto>(
                "SELECT * FROM property WHERE id = @PropertyId",
                new { PropertyId = propertyId });

            if (property == null) return;

            var columnName = $"column_{propertyId}";
            var dataType = GetSqlDataType(property.PropertyType, property.PropertyLength ?? 0);
            string alterSql;

            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                alterSql = $@"IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('{tableName}') AND name = '{columnName}')
                           BEGIN
                               ALTER TABLE {tableName} ADD {columnName} {dataType}
                           END";
            }
            else
            {
                alterSql = $"ALTER TABLE {tableName} ADD COLUMN IF NOT EXISTS {columnName} {dataType}";
            }

            await conn.ExecuteAsync(alterSql);
        }

        private async Task DropColumnFromDynamicTableAsync(IDbConnection conn, string tableName, int propertyId)
        {
            var columnName = $"column_{propertyId}";
            string alterSql;

            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                alterSql = $@"IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('{tableName}') AND name = '{columnName}')
                           BEGIN
                               ALTER TABLE {tableName} DROP COLUMN {columnName}
                           END";
            }
            else
            {
                alterSql = $"ALTER TABLE {tableName} DROP COLUMN IF EXISTS {columnName}";
            }

            await conn.ExecuteAsync(alterSql);
        }

        private async Task CreateDynamicTableWithCurrentProperties(IDbConnection conn, int objectTypeId, bool isBatchProperty, string tableName)
        {
            try
            {
                // Get all current properties for this object type
                string propertySql = isBatchProperty
                    ? @"SELECT p.*, bcd.propertyorder FROM property p 
                       JOIN batchclassdetail bcd ON p.id = bcd.propertyid 
                       WHERE bcd.batchtypeid = @ObjectTypeId 
                       ORDER BY bcd.propertyorder"
                    : @"SELECT p.*, dcd.propertyorder FROM property p 
                       JOIN documentclassdetail dcd ON p.id = dcd.propertyid 
                       WHERE dcd.doctypeid = @ObjectTypeId 
                       ORDER BY dcd.propertyorder";

                var properties = await conn.QueryAsync<PropertyDto>(propertySql, new { ObjectTypeId = objectTypeId });

                // Create the table with all current properties
                var pkColumn = isBatchProperty ? "batchid" : "docid";
                var columns = new List<string> { $"{pkColumn} INTEGER PRIMARY KEY" };

                foreach (var prop in properties)
                {
                    var colName = $"column_{prop.Id}";
                    var dataType = GetSqlDataType(prop.PropertyType, prop.PropertyLength ?? 0);
                    columns.Add($"{colName} {dataType}");
                }

                // Create the table
                var createSql = $"CREATE TABLE {tableName} ({string.Join(", ", columns)})";
                await conn.ExecuteAsync(createSql);

                // Create index on primary key for better performance
                var indexSql = $"CREATE INDEX idx_{tableName}_{pkColumn} ON {tableName}({pkColumn})";
                await conn.ExecuteAsync(indexSql);
            }
            catch
            {
                // Error is suppressed — dynamic table creation is non-critical
            }
        }

        private string GetSqlDataType(string propertyType, int length)
        {
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                return propertyType?.ToUpper() switch
                {
                    "STRING" => length > 0 ? $"NVARCHAR({length})" : "NVARCHAR(MAX)",
                    "INTEGER" => "INT",
                    "DECIMAL" => "DECIMAL(18,2)",
                    "DATETIME" => "DATETIME",
                    "BOOLEAN" => "BIT",
                    _ => "NVARCHAR(MAX)"
                };
            }
            return propertyType?.ToUpper() switch
            {
                "STRING" => length > 0 ? $"VARCHAR({length})" : "VARCHAR(100)",
                "INTEGER" => "INTEGER",
                "DECIMAL" => "DECIMAL(18,2)",
                "DATETIME" => "TIMESTAMP",
                "BOOLEAN" => "BOOLEAN",
                _ => "VARCHAR(100)"
            };
        }

        // Validation methods
        public async Task<bool> IsPropertyInUseAsync(int propertyId)
        {
            using var conn = CreateConnection();

            var batchCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM batchclassdetail WHERE propertyid = @PropertyId",
                new { PropertyId = propertyId });

            if (batchCount > 0) return true;

            var docCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM documentclassdetail WHERE propertyid = @PropertyId",
                new { PropertyId = propertyId });

            return docCount > 0;
        }

        public async Task<bool> IsObjectTypeInUseAsync(int objectTypeId)
            => await GetDeletionBlockReasonAsync(objectTypeId) != null;

        /// <summary>
        /// Returns a human-readable reason string if deletion should be blocked, or null if deletion is allowed.
        /// Blocks deletion when:
        ///   - Batch type: has actual Batch records OR has child document types still linked (objectrelation)
        ///   - Document type: has actual BatchDetail records (processed pages) with this doc type
        /// </summary>
        private async Task<string?> GetDeletionBlockReasonAsync(int objectTypeId)
        {
            using var conn = CreateConnection();

            var objType = await conn.QuerySingleOrDefaultAsync<ObjectTypeDto>(
                "SELECT id, type, name as ObjectName FROM objecttypes WHERE id = @Id",
                new { Id = objectTypeId });

            if (objType == null) return null;

            if (objType.Type == "B")
            {
                // Batch type: block if actual batches exist
                var batchCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM batch WHERE batchtypeid = @Id",
                    new { Id = objectTypeId });
                if (batchCount > 0)
                    return $"Cannot delete application '{objType.ObjectName}': It has {batchCount} existing batch(es). Please delete all batches first.";

                // Batch type: block if it still has child document types linked
                var childCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM objectrelation WHERE parentobjectid = @Id",
                    new { Id = objectTypeId });
                if (childCount > 0)
                    return $"Cannot delete application '{objType.ObjectName}': It still has {childCount} linked document type(s). Please remove all document types from this application first.";
            }
            else
            {
                // Block if the document type has properties configured (documentclassdetail)
                var propCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM documentclassdetail WHERE doctypeid = @Id",
                    new { Id = objectTypeId });
                if (propCount > 0)
                    return $"Cannot delete document type '{objType.ObjectName}': It has {propCount} configured property/properties. Please remove all properties first.";

                // Block if actual processed pages reference this document type.
                // Note: 'document' table is merged into BatchDetail in SQL Server.
                var docCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM BatchDetail WHERE doctypeid = @Id",
                    new { Id = objectTypeId });
                if (docCount > 0)
                    return $"Cannot delete document type '{objType.ObjectName}': It is referenced by {docCount} processed page(s) in existing batches.";
            }

            return null;
        }

        public async Task<IEnumerable<ConfigurationDtos>> GetAllConfigurationsAsync()
        {
            using var conn = CreateConnection();
            // Use DISTINCT ON / ROW_NUMBER to return only one row per ConfigName
            // to prevent duplicate key issues on the client side
            var sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? @"SELECT id as ID, configname as ConfigName, configvalue as ConfigValue
                    FROM (
                        SELECT id, configname, configvalue, ROW_NUMBER() OVER (PARTITION BY configname ORDER BY id) as rn
                        FROM Configuration
                    ) t WHERE rn = 1 ORDER BY configname"
                : @"SELECT DISTINCT ON (configname) id as ID, configname as ConfigName, configvalue as ConfigValue 
                    FROM Configuration ORDER BY configname, id";
            return await conn.QueryAsync<ConfigurationDtos>(sql);
        }

        public async Task SaveAllSettingsAsync(ConfigurationSettings settings)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();

            try
            {
                await SaveConfigValueAsync(conn, transaction, "Batch Folder", settings.BatchFolder);
                await SaveConfigValueAsync(conn, transaction, "Document Folder", settings.DocumentFolder);
                await SaveConfigValueAsync(conn, transaction, "Temp Folder", settings.TempFolder);
                await SaveConfigValueAsync(conn, transaction, "Samples Folder", settings.SamplesFolder);
                await SaveConfigValueAsync(conn, transaction, "Templates Folder", settings.TemplatesFolder);
                await SaveConfigValueAsync(conn, transaction, "Batch Prefix", settings.BatchPrefix);
                await SaveConfigValueAsync(conn, transaction, "Location Name", settings.LocationName);
                await SaveConfigValueAsync(conn, transaction, "Batch Type", settings.BatchType.ToString());
                await SaveConfigValueAsync(conn, transaction, "Current Profile", settings.ScanProfile.ToString());
                await SaveConfigValueAsync(conn, transaction, "Batches Displayed", settings.MaxBatchesDisplayed.ToString());
                await SaveConfigValueAsync(conn, transaction, "Show Batch Window", settings.ShowBatchWindow ? "YES" : "NO");
                await SaveConfigValueAsync(conn, transaction, "Tesseract Dictionary", settings.OcrLanguage);
                await SaveConfigValueAsync(conn, transaction, "Tesseract Path", settings.TessDataPath);
                await SaveConfigValueAsync(conn, transaction, "Separation Mode", settings.SeparationMode);

                // Update step status based on separation mode
                await UpdateStepStatusForSeparationModeAsync(settings.SeparationMode);

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<bool> UpdateStepStatusForSeparationModeAsync(string separationMode)
        {
            using var conn = CreateConnection();
            
            // Determine status based on separation mode
            var autoStatus = separationMode?.Trim().ToLower() == "auto" ? "A" : "I";

            try
            {
                // Update Auto Separation step (ID=3)
                var updateAutoSql = "UPDATE Steps SET Status = @Status WHERE ID = 3";
                await conn.ExecuteAsync(updateAutoSql, new { Status = autoStatus });


                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task SaveConfigValueAsync(IDbConnection conn, IDbTransaction transaction, string name, string value)
        {
            const string checkSql = "SELECT COUNT(*) FROM Configuration WHERE configname = @Name";
            var exists = await conn.ExecuteScalarAsync<int>(checkSql, new { Name = name }, transaction) > 0;

            if (exists)
            {
                const string updateSql = "UPDATE Configuration SET configvalue = @Value WHERE configname = @Name";
                await conn.ExecuteAsync(updateSql, new { Name = name, Value = value ?? "" }, transaction);
            }
            else
            {
                const string insertSql = "INSERT INTO Configuration (configname, configvalue) VALUES (@Name, @Value)";
                await conn.ExecuteAsync(insertSql, new { Name = name, Value = value ?? "" }, transaction);
            }
        }

        public Task<PathValidationResult> ValidateAndCreatePathAsync(string path, bool createIfNotExists)
        {
            var result = new PathValidationResult { FullPath = Path.GetFullPath(path) };
            try
            {
                if (Directory.Exists(result.FullPath))
                {
                    result.IsValid = true; result.Message = "Path exists and is accessible"; return Task.FromResult(result);
                }
                if (!createIfNotExists)
                {
                    result.IsValid = false; result.Message = "Path does not exist"; return Task.FromResult(result);
                }
                var info = Directory.CreateDirectory(result.FullPath);
                result.IsValid = true; result.Message = "Path created successfully"; return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                result.IsValid = false; result.Message = $"Error validating/creating path: {ex.Message}"; return Task.FromResult(result);
            }
        }

        public async Task<string?> GetConfigurationsValue(string configName)
        {
            using var conn = CreateConnection();
            var sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? "SELECT TOP 1 configvalue as ConfigValue FROM Configuration WHERE configname = @configName"
                : "SELECT configvalue as ConfigValue FROM Configuration WHERE configname = @configName LIMIT 1";
            
            return await conn.QueryFirstOrDefaultAsync<string?>(sql, new { configName = configName });
        }

        public async Task<List<ZoneConfig>> GetAllZonesAsync()
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT ID, Name, LeftX, TopY, RightX, BottomY, 
                       DocTypeID, PageNo, Type, StartPosition, Length
                FROM Zones
                ORDER BY DocTypeID, Name";
            
            var zones = await conn.QueryAsync<ZoneConfig>(sql);
            return zones.ToList();
        }

        public async Task<List<ZoneConfig>> GetZonesByDocumentTypeAsync(int documentTypeId)
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT ID, Name, LeftX, TopY, RightX, BottomY, 
                       DocTypeID, PageNo, Type, StartPosition, Length,DisplayedWidth, DisplayedHeight
                FROM Zones
                WHERE DocTypeID = @DocumentTypeId
                ORDER BY Name";
            
            var zones = await conn.QueryAsync<ZoneConfig>(sql, new { DocumentTypeId = documentTypeId });
            return zones.ToList();
        }

        public async Task<ZoneConfig?> GetZoneByIdAsync(int zoneId)
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT ID, Name, LeftX, TopY, RightX, BottomY, 
                       DocTypeID, PageNo, Type, StartPosition, Length
                FROM Zones
                WHERE ID = @ZoneId";
            var zone = await conn.QueryFirstOrDefaultAsync<ZoneConfig?>(sql, new { ZoneId = zoneId });
            return zone;
        }

        public async Task<bool> CreateZoneAsync(CreateZoneRequest request)
        {
            using var conn = CreateConnection();
            const string sql = @"
                INSERT INTO Zones 
                (Name, LeftX, TopY, RightX, BottomY, 
                 DocTypeID, PageNo, Type, StartPosition, Length, DisplayedWidth, DisplayedHeight) 
                VALUES 
                (@Name, @LeftX, @TopY, @RightX, @BottomY, 
                 @DocTypeID, @PageNo, @Type, @StartPosition, @Length, @DisplayedWidth, @DisplayedHeight)";
            

            var result = await conn.ExecuteAsync(sql, new
            {
                request.Name,
                request.LeftX,
                request.TopY,
                request.RightX,
                request.BottomY,
                request.DocTypeID,
                request.PageNo,
                request.Type,
                request.StartPosition,
                request.Length,
                request.DisplayedWidth,
                request.DisplayedHeight
            });
            
            return result > 0;
        }
       
        public async Task<bool> UpdateZoneAsync(UpdateZoneRequest request)
        {
            using var conn = CreateConnection();
            const string sql = @"
                UPDATE Zones 
                SET Name = @Name, LeftX = @LeftX, TopY = @TopY, RightX = @RightX, BottomY = @BottomY, 
                    DocTypeID = @DocTypeID, PageNo = @PageNo, Type = @Type, StartPosition = @StartPosition, Length = @Length,
                    DisplayedWidth = @DisplayedWidth, DisplayedHeight = @DisplayedHeight
                WHERE ID = @ID";
            
            var result = await conn.ExecuteAsync(sql, new
            {
                request.ID,
                request.Name,
                request.LeftX,
                request.TopY,
                request.RightX,
                request.BottomY,
                request.DocTypeID,
                request.PageNo,
                request.Type,
                request.StartPosition,
                request.Length,
                request.DisplayedWidth,
                request.DisplayedHeight
            });
            
            return result > 0;
        }

        public async Task<bool> DeleteZoneAsync(int zoneId)
        {
            using var conn = CreateConnection();
            
            // First, remove any property mappings to this zone
            await RemoveZoneMappingsAsync(zoneId);
            
            const string sql = "DELETE FROM Zones WHERE ID = @ZoneId";
            
            var result = await conn.ExecuteAsync(sql, new { ZoneId = zoneId });
            
            return result > 0;
        }

        public async Task<bool> MapPropertyToZoneAsync(int propertyId, int? zoneId, int docTypeId)
        {

    using var conn = CreateConnection();

    // Check if the property mapping already exists
    var existingMapping = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT Id FROM DocumentClassDetail WHERE DocTypeId = @DocTypeId AND PropertyId = @PropertyId",
                new { DocTypeId = docTypeId, PropertyId = propertyId });
            
            if (existingMapping.HasValue)
            {
                // Update existing mapping
                var updateSql = "UPDATE DocumentClassDetail SET ZoneId = @ZoneId WHERE Id = @Id";
                var result = await conn.ExecuteAsync(updateSql, new { ZoneId = zoneId, Id = existingMapping.Value });
                return result > 0;
            }
            else
            {
                // Insert new mapping
                var isTrue = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "true";
                var isFalse = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "0" : "false";
                var insertSql = $@"
                    INSERT INTO DocumentClassDetail (DocTypeId, PropertyId, PropertyOrder, IsEnabled, IsRequired, ZoneId, Length, LookupId, IsBatchProperty)
                    VALUES (@DocTypeId, @PropertyId, 0, {isTrue}, {isFalse}, @ZoneId, 0, NULL, {isFalse})";
                var result = await conn.ExecuteAsync(insertSql, new { DocTypeId = docTypeId, PropertyId = propertyId, ZoneId = zoneId });
                return result > 0;
            }
        }

        public async Task<bool> RemoveZoneMappingsAsync(int zoneId)
        {
            using var conn = CreateConnection();

            // Remove zone mapping from DocumentClassDetail table
            var updateSql = "UPDATE DocumentClassDetail SET ZoneId = NULL WHERE ZoneId = @ZoneId";
            var result = await conn.ExecuteAsync(updateSql, new { ZoneId = zoneId });
            
            return result > 0;
        }

        public async Task<IEnumerable<DmsProviderDto>> GetDmsProvidersAsync()
        {
            using var conn = CreateConnection();
            var isActiveFilter = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "true";
            var sql = $@"
                SELECT id, name, displayname, description, isactive, createdon, modifiedon
                FROM dmsproviders
                WHERE isactive = {isActiveFilter}";

            var providers = await conn.QueryAsync<DmsProviderDto>(sql);
            return providers;
        }

        public async Task<DmsProviderDto?> GetDmsProviderByIdAsync(int providerId)
        {
            using var conn = CreateConnection();
            var isActiveFilter = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "true";
            var sql = $@"
                SELECT id, name, displayname, description, isactive, createdon, modifiedon
                FROM dmsproviders
                WHERE id = @ProviderId AND isactive = {isActiveFilter}";

            var provider = await conn.QuerySingleOrDefaultAsync<DmsProviderDto>(sql, new { ProviderId = providerId });
            return provider;
        }

        public async Task<IEnumerable<DmsOutputFormatDto>> GetDmsOutputFormatsAsync()
        {
            using var conn = CreateConnection();
            var isActiveFilter = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "true";
            var sql = $@"
                SELECT id, formatcode, formatname, description, isactive, createdon, modifiedon
                FROM dmsoutputformats
                WHERE isactive = {isActiveFilter}";

            var outputFormats = await conn.QueryAsync<DmsOutputFormatDto>(sql);
            return outputFormats;
        }

        // Property Mapping Implementation
        public async Task<bool> SavePropertyMappingAsync(PropertyMappingRequest request)
        {
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"
                    UPDATE propertymapping SET jsonproperty = @JsonProperty, modifiedon = CURRENT_TIMESTAMP 
                    WHERE providername = @ProviderName AND doctypeid = @DoctypeId;
                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO propertymapping (providername, doctypeid, jsonproperty)
                        VALUES (@ProviderName, @DoctypeId, @JsonProperty)
                    END";
            }
            else
            {
                sql = @"
                    INSERT INTO propertymapping (providername, doctypeid, jsonproperty)
                    VALUES (@ProviderName, @DoctypeId, @JsonProperty::jsonb)
                    ON CONFLICT (providername, doctypeid) 
                    DO UPDATE SET jsonproperty = EXCLUDED.jsonproperty, modifiedon = CURRENT_TIMESTAMP;";
            }

            using var connection = CreateConnection();
            return await connection.ExecuteAsync(sql, request) > 0;
        }

        public async Task<PropertyMappingDto?> GetPropertyMappingAsync(string providerName, int docTypeId)
        {
            const string sql = "SELECT * FROM propertymapping WHERE providername = @providerName AND doctypeid = @docTypeId";
            using var connection = CreateConnection();
            return await connection.QueryFirstOrDefaultAsync<PropertyMappingDto>(sql, new { providerName, docTypeId });
        }


        // User Role and Permission Methods
        public async Task<IEnumerable<UserRoleDto>> GetUserRolesAsync()
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT RoleId as Id, RoleName as Name, RoleDescription as Description
                FROM UserRoles
                ORDER BY RoleName";
            return await conn.QueryAsync<UserRoleDto>(sql);
        }

        public async Task<IEnumerable<PermissionDto>> GetPermissionsAsync()
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT PermissionId as Id, PermissionName as Name, PermissionDescription as Description
                FROM Permissions
                ORDER BY PermissionName";
            return await conn.QueryAsync<PermissionDto>(sql);
        }

        public async Task AssignRolePermissionsAsync(int roleId, List<int> permissionIds)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();
            
            try
            {
                // First, remove existing permission assignments for this role
                await conn.ExecuteAsync(
                    "DELETE FROM RolePermissionAssignments WHERE RoleId = @RoleId",
                    new { RoleId = roleId },
                    transaction);

                // Add new permission assignments
                foreach (var permissionId in permissionIds)
                {
                    string sql;
                    if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                    {
                        sql = @"
                            UPDATE RolePermissionAssignments SET AssignedOn = CURRENT_TIMESTAMP 
                            WHERE RoleId = @RoleId AND PermissionId = @PermissionId;
                            IF @@ROWCOUNT = 0
                            BEGIN
                                INSERT INTO RolePermissionAssignments (RoleId, PermissionId, AssignedOn) 
                                VALUES (@RoleId, @PermissionId, CURRENT_TIMESTAMP)
                            END";
                    }
                    else
                    {
                        sql = @"INSERT INTO RolePermissionAssignments (RoleId, PermissionId, AssignedOn) VALUES (@RoleId, @PermissionId, CURRENT_TIMESTAMP) 
                                ON CONFLICT (RoleId, PermissionId) DO UPDATE SET AssignedOn = CURRENT_TIMESTAMP";
                    }
                    await conn.ExecuteAsync(sql, new { RoleId = roleId, PermissionId = permissionId }, transaction);
                }
                
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        
        public async Task<IEnumerable<int>> GetUserRoleAssignmentsAsync(string userName)
        {
            using var conn = CreateConnection();
            const string sql = @"
                SELECT RoleId
                FROM UserRoleAssignments
                WHERE UserName = @UserName";
            var roleIds = await conn.QueryAsync<int>(sql, new { UserName = userName });
            return roleIds;
        }
        
        public async Task AssignUserRolesAsync(string userName, List<int> roleIds)
        {
            using var conn = CreateConnection();
            conn.Open();
            using var transaction = conn.BeginTransaction();
            
            try
            {
                // First, remove existing role assignments for this user
                await conn.ExecuteAsync(
                    "DELETE FROM UserRoleAssignments WHERE UserName = @UserName",
                    new { UserName = userName },
                    transaction);

                // Add new role assignments
                foreach (var roleId in roleIds)
                {
                    string sql;
                    if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                    {
                        sql = @"
                            UPDATE UserRoleAssignments SET AssignedOn = CURRENT_TIMESTAMP 
                            WHERE UserName = @UserName AND RoleId = @RoleId;
                            IF @@ROWCOUNT = 0
                            BEGIN
                                INSERT INTO UserRoleAssignments (UserName, RoleId, AssignedOn) 
                                VALUES (@UserName, @RoleId, CURRENT_TIMESTAMP)
                            END";
                    }
                    else
                    {
                        sql = @"INSERT INTO UserRoleAssignments (UserName, RoleId, AssignedOn) VALUES (@UserName, @RoleId, CURRENT_TIMESTAMP) 
                                ON CONFLICT (UserName, RoleId) DO UPDATE SET AssignedOn = CURRENT_TIMESTAMP";
                    }
                    await conn.ExecuteAsync(sql, new { UserName = userName, RoleId = roleId }, transaction);
                }
                
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}