using Dapper;
using Server.Models;
using Server.Repositories;
using System.Data;
using System.Text.Json;

namespace Server.Services;

// Create a temporary class to handle the raw data from the database
public class OcrConnectorRaw
{
    public int Id { get; set; }
    public int ProviderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string? ConfigDataRaw { get; set; } // This will receive the JSON as string
    public bool IsActive { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
    
    // Navigation property
    public OcrProviderDto? Provider { get; set; }
}

public class OcrConnectorService : BaseRepository, IOcrConnectorService
{

    public OcrConnectorService(string connectionString, string provider) : base(connectionString, provider)
    {
    }

    public async Task<IEnumerable<OcrProviderDto>> GetAllOcrProvidersAsync()
    {
        using var connection = CreateConnection();
        string isActiveFilter = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "true";
        var sql = $@"SELECT id, name, displayname, description, isactive, createdon, modifiedon 
                    FROM ocrproviders 
                    WHERE isactive = {isActiveFilter} 
                    ORDER BY displayname";
        
        var providers = await connection.QueryAsync<OcrProviderDto>(sql);
        return providers;
    }

    public async Task<IEnumerable<OcrConnectorDto>> GetAllOcrConnectorsAsync()
    {
        using var connection = CreateConnection();
        string jsonCast = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "" : "::text";
        string isActiveFilter = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "true";
        var sql = $@"SELECT oc.id, oc.providerid, oc.name, oc.isdefault, oc.configdata{jsonCast} as ConfigDataRaw, oc.isactive, 
                           oc.createdon, oc.modifiedon, 
                           op.id as Id, op.name as Name, op.displayname as DisplayName, 
                           op.description as Description, op.isactive as IsActive, 
                           op.createdon as CreatedOn, op.modifiedon as ModifiedOn
                    FROM ocrconnectors oc
                    LEFT JOIN ocrproviders op ON oc.providerid = op.id
                    WHERE oc.isactive = {isActiveFilter}
                    ORDER BY oc.name";
        
        var rawConnectors = await connection.QueryAsync<OcrConnectorRaw, OcrProviderDto, OcrConnectorRaw>(
            sql,
            (connector, provider) =>
            {
                connector.Provider = provider;
                return connector;
            },
            splitOn: "Id"
        );
        
        // Convert raw connectors to DTOs with proper JSON parsing
        var connectors = rawConnectors.Select(raw => MapToOcrConnectorDto(raw)).ToList();
        
        return connectors;
    }

    public async Task<OcrConnectorDto?> GetDefaultOcrConnectorAsync()
    {
        using var connection = CreateConnection();
        string jsonCast = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "" : "::text";
        string trueVal = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "true";
        var sql = $@"SELECT oc.id, oc.providerid, oc.name, oc.isdefault, oc.configdata{jsonCast} as ConfigDataRaw, oc.isactive, 
                           oc.createdon, oc.modifiedon, 
                           op.id as Id, op.name as Name, op.displayname as DisplayName, 
                           op.description as Description, op.isactive as IsActive, 
                           op.createdon as CreatedOn, op.modifiedon as ModifiedOn
                    FROM ocrconnectors oc
                    LEFT JOIN ocrproviders op ON oc.providerid = op.id
                    WHERE oc.isdefault = {trueVal} AND oc.isactive = {trueVal}";
        
        var rawResult = await connection.QueryAsync<OcrConnectorRaw, OcrProviderDto, OcrConnectorRaw>(
            sql,
            (connector, provider) =>
            {
                connector.Provider = provider;
                return connector;
            },
            splitOn: "Id"
        );
        
        var rawConnector = rawResult.FirstOrDefault();
        return rawConnector != null ? MapToOcrConnectorDto(rawConnector) : null;
    }

    public async Task<OcrConnectorDto?> GetOcrConnectorByApplicationIdAsync(int applicationId)
    {
        using var connection = CreateConnection();
        string jsonCast = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "" : "::text";
        string trueVal = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "1" : "true";
        
        var sql = $@"
            SELECT c.id, c.providerid, c.name, c.isdefault, c.configdata{jsonCast} as ConfigDataRaw, c.isactive, 
                   c.createdon, c.modifiedon, 
                   p.id as Id, p.name as Name, p.displayname as DisplayName, 
                   p.description as Description, p.isactive as IsActive, 
                   p.createdon as CreatedOn, p.modifiedon as ModifiedOn
            FROM ocrconnectors c
            JOIN ocrproviders p ON c.providerid = p.id
            JOIN objecttypes o ON o.OcrConnectorId = c.id
            WHERE o.id = @ApplicationId AND c.isactive = {trueVal}";

        try
        {
            var result = await connection.QueryAsync<OcrConnectorRaw, OcrProviderDto, OcrConnectorRaw>(
                sql,
                (connector, provider) =>
                {
                    connector.Provider = provider;
                    return connector;
                },
                new { ApplicationId = applicationId },
                splitOn: "Id"
            );

            var rawConnector = result.FirstOrDefault();
            return rawConnector != null ? MapToOcrConnectorDto(rawConnector) : null;
        }
        catch (Exception ex) when (ex.Message.Contains("OcrConnectorId", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("column", StringComparison.OrdinalIgnoreCase))
        {
            // Column doesn't exist yet, fallback to default behavior
            return null;
        }
    }

    public async Task<OcrConnectorDto?> GetOcrConnectorByIdAsync(int id)
    {
        using var connection = CreateConnection();
        string jsonCast = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "" : "::text";
        var sql = $@"SELECT oc.id, oc.providerid, oc.name, oc.isdefault, oc.configdata{jsonCast} as ConfigDataRaw, oc.isactive, 
                           oc.createdon, oc.modifiedon, 
                           op.id as Id, op.name as Name, op.displayname as DisplayName, 
                           op.description as Description, op.isactive as IsActive, 
                           op.createdon as CreatedOn, op.modifiedon as ModifiedOn
                    FROM ocrconnectors oc
                    LEFT JOIN ocrproviders op ON oc.providerid = op.id
                    WHERE oc.id = @Id ";
        
        var rawResult = await connection.QueryAsync<OcrConnectorRaw, OcrProviderDto, OcrConnectorRaw>(
            sql,
            (connector, provider) =>
            {
                connector.Provider = provider;
                return connector;
            },
            new { Id = id },
            splitOn: "Id"
        );
        
        var rawConnector = rawResult.FirstOrDefault();
        return rawConnector != null ? MapToOcrConnectorDto(rawConnector) : null;
    }

    public async Task<OcrConnectorDto> CreateOcrConnectorAsync(OcrConnectorDto connectorDto)
    {
        using var connection = CreateConnection();
        connection.Open(); // Open connection to use transactions
        using var transaction = BeginTransaction(connection);
        
        try
        {
            // If setting as default, unset other defaults first
            if (connectorDto.IsDefault)
            {
                await UnsetOtherDefaults(transaction);
            }

            var configDataJson = connectorDto.ConfigData != null 
                ? System.Text.Json.JsonSerializer.Serialize(connectorDto.ConfigData) 
                : "{}";

            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive, createdon, modifiedon)
                        OUTPUT INSERTED.id
                        VALUES (@ProviderId, @Name, @IsDefault, @ConfigData, @IsActive, @CreatedOn, @ModifiedOn)";
            }
            else
            {
                sql = @"INSERT INTO ocrconnectors (providerid, name, isdefault, configdata, isactive, createdon, modifiedon)
                        VALUES (@ProviderId, @Name, @IsDefault, @ConfigData::jsonb, @IsActive, @CreatedOn, @ModifiedOn)
                        RETURNING id";
            }
            
            var id = connection.QuerySingle<int>(sql, new
            {
                ProviderId = connectorDto.ProviderId,
                Name = connectorDto.Name,
                IsDefault = connectorDto.IsDefault,
                ConfigData = configDataJson,
                IsActive = connectorDto.IsActive,
                CreatedOn = DateTime.Now,
                ModifiedOn = DateTime.Now
            }, transaction);

            transaction.Commit();

            // Return the created connector
            return await GetOcrConnectorByIdAsync(id) ?? throw new Exception("Failed to retrieve created OCR connector");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<OcrConnectorDto> UpdateOcrConnectorAsync(int id, OcrConnectorDto connectorDto)
    {
        using var connection = CreateConnection();
        connection.Open(); // Open connection to use transactions
        using var transaction = BeginTransaction(connection);
        
        try
        {
            // If setting as default, unset other defaults first
            if (connectorDto.IsDefault)
            {
                await UnsetOtherDefaults(transaction);
            }

            var configDataJson = connectorDto.ConfigData != null 
                ? System.Text.Json.JsonSerializer.Serialize(connectorDto.ConfigData) 
                : "{}";

            string jsonCast = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ? "" : "::jsonb";
            string sql;
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                sql = @"UPDATE ocrconnectors 
                        SET providerid = @ProviderId, name = @Name, isdefault = @IsDefault, 
                            configdata = @ConfigData, isactive = @IsActive, modifiedon = @ModifiedOn
                        WHERE id = @Id";
            }
            else
            {
                sql = @"UPDATE ocrconnectors 
                        SET providerid = @ProviderId, name = @Name, isdefault = @IsDefault, 
                            configdata = @ConfigData::jsonb, isactive = @IsActive, modifiedon = @ModifiedOn
                        WHERE id = @Id";
            }
            
            connection.Execute(sql, new
            {
                Id = id,
                ProviderId = connectorDto.ProviderId,
                Name = connectorDto.Name,
                IsDefault = connectorDto.IsDefault,
                ConfigData = configDataJson,
                IsActive = connectorDto.IsActive,
                ModifiedOn = DateTime.Now
            }, transaction);

            transaction.Commit();

            // Return the updated connector
            return await GetOcrConnectorByIdAsync(id) ?? throw new Exception("Failed to retrieve updated OCR connector");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> DeleteOcrConnectorAsync(int id)
    {
        using var connection = CreateConnection();
        var sql = @"UPDATE ocrconnectors SET isactive = @IsActive, modifiedon = @ModifiedOn WHERE id = @Id";
        
        var result = await connection.ExecuteAsync(sql, new { IsActive = false, Id = id, ModifiedOn = DateTime.Now });
        return result > 0;
    }

    public async Task<bool> SetDefaultOcrConnectorAsync(int id)
    {
        using var connection = CreateConnection();
        connection.Open(); // Open connection to use transactions
        using var transaction = connection.BeginTransaction();
        
        try
        {
            // Unset all other defaults
            await UnsetOtherDefaults(transaction);
            
            // Set the specified connector as default
            string sql = @"UPDATE ocrconnectors SET isdefault = @IsDefault, modifiedon = @ModifiedOn WHERE id = @Id";
            var result = connection.Execute(sql, new { IsDefault = true, Id = id, ModifiedOn = DateTime.Now }, transaction);
            
            transaction.Commit();
            
            return result > 0;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<OcrConfigurationDto?> GetOcrConfigurationAsync(string configName)
    {
        using var connection = CreateConnection();
        var sql = @"SELECT id, configname, configvalue, description, createdon, modifiedon 
                    FROM ocrconfiguration 
                    WHERE configname = @ConfigName";
        
        var config = await connection.QueryFirstOrDefaultAsync<OcrConfigurationDto>(sql, new { ConfigName = configName });
        return config;
    }

    public async Task<bool> UpdateOcrConfigurationAsync(string configName, string configValue)
    {
        using var connection = CreateConnection();
        var sql = @"UPDATE ocrconfiguration SET configvalue = @ConfigValue, modifiedon = @ModifiedOn 
                    WHERE configname = @ConfigName";
        
        var result = await connection.ExecuteAsync(sql, new { 
            ConfigName = configName, 
            ConfigValue = configValue, 
            ModifiedOn = DateTime.Now 
        });
        
        return result > 0;
    }

    public async Task<bool> UpdateStepStatusForOcrModeAsync(string ocrMode)
    {
        using var connection = CreateConnection();
        var status = ocrMode.ToLower() == "automatic" ? "A" : "I"; // A = Active, I = Inactive
        
        // First, check if a record with StepOrder = 4 exists
        var checkSql = "SELECT COUNT(*) FROM Steps WHERE StepOrder = 4";
        var exists = await connection.QuerySingleAsync<int>(checkSql) > 0;
        
        int result;
        if (exists)
        {
            // Update existing record
            var updateSql = @"UPDATE Steps SET Status = @Status WHERE StepOrder = 4";
            result = await connection.ExecuteAsync(updateSql, new { 
                Status = status, 
                ModifiedOn = DateTime.Now 
            });
        }
        else
        {
            // Insert new record if it doesn't exist
            var insertSql = @"INSERT INTO Steps (ID, StepName, Status, StepOrder) 
                              VALUES (4, 'OCR', @Status, 4)";
            result = await connection.ExecuteAsync(insertSql, new { 
                Status = status, 
                ModifiedOn = DateTime.Now 
            });
        }
        
        return result > 0;
    }

    private async Task UnsetOtherDefaults(IDbTransaction transaction)
    {
        var sql = @"UPDATE ocrconnectors SET isdefault = @NewDefault, modifiedon = @ModifiedOn WHERE isdefault = @OldDefault";
        transaction.Connection!.Execute(sql, new { NewDefault = false, OldDefault = true, ModifiedOn = DateTime.Now }, transaction);
    }
    
    private OcrConnectorDto MapToOcrConnectorDto(OcrConnectorRaw raw)
    {
        var dto = new OcrConnectorDto
        {
            Id = raw.Id,
            ProviderId = raw.ProviderId,
            Name = raw.Name,
            IsDefault = raw.IsDefault,
            IsActive = raw.IsActive,
            CreatedOn = raw.CreatedOn,
            ModifiedOn = raw.ModifiedOn,
            Provider = raw.Provider
        };

        // Parse the configdata JSON string if it's not null or empty
        if (!string.IsNullOrEmpty(raw.ConfigDataRaw))
        {
            try
            {
                var configDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    raw.ConfigDataRaw, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Create a case-insensitive dictionary from the deserialized one
                dto.ConfigData = configDict != null 
                    ? new Dictionary<string, object>(configDict, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                dto.ConfigData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
        }
        else
        {
            dto.ConfigData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        return dto;
    }
}