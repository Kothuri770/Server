using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;
using Server.Models;

namespace Server.Repositories;

public interface ILicenseRepository
{
    Task<LicensePayload?> GetActiveLicenseAsync();
    Task<bool> SaveLicenseAsync(string licenseId, string customerName, string licenseType, string installationId, DateTime issuedAtUtc, DateTime? expiresAtUtc, string productVersion, string payloadHash);
    Task<bool> UpdateLastCheckedAsync();
    Task<DateTime?> GetLastCheckedAsync();
    Task<string?> GetInstallationIdAsync();
    Task<bool> DeactivateLicenseAsync();
}

public class LicenseRepository : BaseRepository, ILicenseRepository
{
    public LicenseRepository(string connectionString, string provider = "PostgreSql") 
        : base(connectionString, provider)
    {
    }

    public async Task<LicensePayload?> GetActiveLicenseAsync()
    {
        using var connection = CreateConnection();
        string sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
            ? "SELECT TOP 1 license_id AS LicenseId, customer_name AS CustomerName, license_type AS LicenseType, installation_id AS InstallationId, issued_at_utc AS IssuedAtUtc, expires_at_utc AS ExpiresAtUtc, product_version AS ProductVersion FROM license WHERE is_active = 1 ORDER BY id DESC"
            : "SELECT license_id AS LicenseId, customer_name AS CustomerName, license_type AS LicenseType, installation_id AS InstallationId, issued_at_utc AS IssuedAtUtc, expires_at_utc AS ExpiresAtUtc, product_version AS ProductVersion FROM license WHERE is_active = true ORDER BY id DESC LIMIT 1";

        return await connection.QueryFirstOrDefaultAsync<LicensePayload>(sql);
    }

    public async Task<bool> SaveLicenseAsync(string licenseId, string customerName, string licenseType, string installationId, DateTime issuedAtUtc, DateTime? expiresAtUtc, string productVersion, string payloadHash)
    {
        using var connection = CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            string deactivateSql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? "UPDATE license SET is_active = 0"
                : "UPDATE license SET is_active = false";
            
            await connection.ExecuteAsync(deactivateSql, transaction: transaction);

            string insertSql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? @"INSERT INTO license (license_id, customer_name, license_type, installation_id, issued_at_utc, expires_at_utc, product_version, payload_hash, last_checked_utc, activated_at_utc, is_active)
                    VALUES (@LicenseId, @CustomerName, @LicenseType, @InstallationId, @IssuedAtUtc, @ExpiresAtUtc, @ProductVersion, @PayloadHash, GETUTCDATE(), GETUTCDATE(), 1)"
                : @"INSERT INTO license (license_id, customer_name, license_type, installation_id, issued_at_utc, expires_at_utc, product_version, payload_hash, last_checked_utc, activated_at_utc, is_active)
                    VALUES (@LicenseId, @CustomerName, @LicenseType, @InstallationId, @IssuedAtUtc, @ExpiresAtUtc, @ProductVersion, @PayloadHash, (NOW() AT TIME ZONE 'UTC'), (NOW() AT TIME ZONE 'UTC'), true)";

            var affected = await connection.ExecuteAsync(insertSql, new
            {
                LicenseId = licenseId,
                CustomerName = customerName,
                LicenseType = licenseType,
                InstallationId = installationId,
                IssuedAtUtc = issuedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
                ProductVersion = productVersion,
                PayloadHash = payloadHash
            }, transaction: transaction);

            transaction.Commit();
            return affected > 0;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> UpdateLastCheckedAsync()
    {
        using var connection = CreateConnection();
        string sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
            ? "UPDATE license SET last_checked_utc = GETUTCDATE() WHERE is_active = 1"
            : "UPDATE license SET last_checked_utc = (NOW() AT TIME ZONE 'UTC') WHERE is_active = true";

        var affected = await connection.ExecuteAsync(sql);
        return affected > 0;
    }

    public async Task<DateTime?> GetLastCheckedAsync()
    {
        using var connection = CreateConnection();
        string sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
            ? "SELECT TOP 1 last_checked_utc FROM license WHERE is_active = 1 ORDER BY id DESC"
            : "SELECT last_checked_utc FROM license WHERE is_active = true ORDER BY id DESC LIMIT 1";

        return await connection.QueryFirstOrDefaultAsync<DateTime?>(sql);
    }

    public async Task<string?> GetInstallationIdAsync()
    {
        using var connection = CreateConnection();
        string sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
            ? "SELECT TOP 1 installation_id FROM installation_info ORDER BY id ASC"
            : "SELECT installation_id FROM installation_info ORDER BY id ASC LIMIT 1";

        return await connection.QueryFirstOrDefaultAsync<string>(sql);
    }

    public async Task<bool> DeactivateLicenseAsync()
    {
        using var connection = CreateConnection();
        string sql = _provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
            ? "UPDATE license SET is_active = 0"
            : "UPDATE license SET is_active = false";

        var affected = await connection.ExecuteAsync(sql);
        return affected > 0;
    }
}
