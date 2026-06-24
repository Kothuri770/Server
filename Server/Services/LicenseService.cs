using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Server.Models;
using Server.Repositories;

namespace Server.Services;

public interface ILicenseService
{
    Task<LicenseStatusDto> ValidateLicenseAsync();
    Task<LicenseStatusDto> ActivateLicenseAsync(string licenseKeyBase64);
    Task<string?> GetInstallationIdAsync();
    Task<bool> DeactivateLicenseAsync();
    Task UpdateLastCheckedAsync();
}

public class LicenseService : ILicenseService
{
    private readonly ILicenseRepository _licenseRepository;
    private readonly ILogger<LicenseService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    private const string PublicKeyPem = @"-----BEGIN RSA PUBLIC KEY-----
MIIBCgKCAQEA0wbwEfttQP8YzMIMNDUdWy6NITIuXdViwkc5D8cAjHK6DonUvhHC
+Rv+AD2Zd41Wr0Kvd+FZ6O1Hbq9rokPRwWkDJyMtdW82hG6yN8DP6fJ06qoGBAm6
kxnrpunZim2Cn84e7grdtvueLl+QgUMcK3iqFN1YmGL5WvWVdgTjYLJg0M/ZNRqf
eGyYweuLCAfdt7RQCtwESyULLtGudCOff91KHFfafJ0p64VEm1QVtTHzKlHLoiY4
no0azrp/qaTKsb+eQEa/CPXC9RP6v90kvggQnOImhGWqldSHKfUF7mmNTYEQHoOC
eNkAw1cqgce7rYvmf4thHdTYu+DsPGBwNQIDAQAB
-----END RSA PUBLIC KEY-----";

    public LicenseService(string connectionString, string databaseProvider, ILogger<LicenseService> logger, IConfiguration configuration, IMemoryCache cache)
    {
        _licenseRepository = new LicenseRepository(connectionString, databaseProvider);
        _logger = logger;
        _configuration = configuration;
        _cache = cache;
    }

    public async Task<LicenseStatusDto> ValidateLicenseAsync()
    {
        if (_cache.TryGetValue("LicenseStatus", out LicenseStatusDto? cachedStatus) && cachedStatus != null)
        {
            return cachedStatus;
        }

        var status = await PerformValidationAsync();
        
        int cacheSeconds = _configuration.GetValue<int>("License:ValidationCacheSeconds", 60);
        _cache.Set("LicenseStatus", status, TimeSpan.FromSeconds(cacheSeconds));
        
        return status;
    }

    private async Task<LicenseStatusDto> PerformValidationAsync()
    {
        try
        {
            string licenseFilePath = _configuration.GetValue<string>("License:LicenseFilePath") ?? "license.key";
            
            if (!File.Exists(licenseFilePath))
            {
                return new LicenseStatusDto { IsValid = false, Status = "NotActivated", Message = "License not activated." };
            }

            string finalBase64 = await File.ReadAllTextAsync(licenseFilePath);
            string licenseFileJson = Encoding.UTF8.GetString(Convert.FromBase64String(finalBase64));
            var licenseFile = JsonSerializer.Deserialize<LicenseFile>(licenseFileJson);

            if (licenseFile == null)
            {
                return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "Invalid license file format." };
            }

            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);

            byte[] signatureBytes = Convert.FromBase64String(licenseFile.Signature);
            byte[] payloadBytesToVerify = Encoding.UTF8.GetBytes(licenseFile.Payload);

            bool isSignatureValid = rsa.VerifyData(payloadBytesToVerify, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            if (!isSignatureValid)
            {
                return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "License signature verification failed." };
            }

            string payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(licenseFile.Payload));
            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadJson);

            if (payload == null)
            {
                return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "Invalid license payload." };
            }

            string? dbInstallationId = await _licenseRepository.GetInstallationIdAsync();
            if (string.IsNullOrEmpty(dbInstallationId) || payload.InstallationId != dbInstallationId)
            {
                return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "License installation ID mismatch." };
            }

            DateTime utcNow = DateTime.UtcNow;
            DateTime? lastChecked = await _licenseRepository.GetLastCheckedAsync();
            int tamperTolerance = _configuration.GetValue<int>("License:ClockTamperToleranceMinutes", 5);

            if (lastChecked.HasValue && utcNow < lastChecked.Value.AddMinutes(-tamperTolerance))
            {
                return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "System clock tampering detected." };
            }

            int gracePeriodDays = _configuration.GetValue<int>("License:GracePeriodDays", 7);
            
            var result = new LicenseStatusDto
            {
                CustomerName = payload.CustomerName,
                LicenseType = payload.LicenseType,
                ExpiresAtUtc = payload.ExpiresAtUtc,
                InstallationId = payload.InstallationId
            };

            if (payload.ExpiresAtUtc.HasValue)
            {
                double daysRemaining = (payload.ExpiresAtUtc.Value - utcNow).TotalDays;
                result.DaysRemaining = (int)Math.Ceiling(daysRemaining);

                if (daysRemaining > 0)
                {
                    result.IsValid = true;
                    result.Status = "Active";
                    result.Message = "License is active.";
                }
                else if (daysRemaining >= -gracePeriodDays)
                {
                    result.IsValid = true;
                    result.Status = "GracePeriod";
                    result.Message = $"License expired. Grace period ends in {(int)Math.Ceiling(gracePeriodDays + daysRemaining)} days.";
                }
                else
                {
                    result.IsValid = false;
                    result.Status = "Expired";
                    result.Message = "License expired.";
                }
            }
            else
            {
                result.IsValid = true;
                result.Status = "Active";
                result.Message = "Permanent license.";
                result.DaysRemaining = null;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating license");
            return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "Error validating license." };
        }
    }

    public async Task<LicenseStatusDto> ActivateLicenseAsync(string licenseKeyBase64)
    {
        try
        {
            string licenseFileJson = Encoding.UTF8.GetString(Convert.FromBase64String(licenseKeyBase64));
            var licenseFile = JsonSerializer.Deserialize<LicenseFile>(licenseFileJson);

            if (licenseFile == null)
            {
                return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "Invalid license file format." };
            }

            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);

            byte[] signatureBytes = Convert.FromBase64String(licenseFile.Signature);
            byte[] payloadBytesToVerify = Encoding.UTF8.GetBytes(licenseFile.Payload);

            bool isSignatureValid = rsa.VerifyData(payloadBytesToVerify, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            if (!isSignatureValid)
            {
                return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "License signature verification failed." };
            }

            string payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(licenseFile.Payload));
            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadJson);

            if (payload == null)
            {
                return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "Invalid license payload." };
            }

            string? dbInstallationId = await _licenseRepository.GetInstallationIdAsync();
            if (string.IsNullOrEmpty(dbInstallationId) || payload.InstallationId != dbInstallationId)
            {
                return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = "License installation ID mismatch." };
            }

            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(payloadBytesToVerify);
            string payloadHash = Convert.ToBase64String(hashBytes);

            await _licenseRepository.SaveLicenseAsync(
                payload.LicenseId,
                payload.CustomerName,
                payload.LicenseType,
                payload.InstallationId,
                payload.IssuedAtUtc,
                payload.ExpiresAtUtc,
                payload.ProductVersion,
                payloadHash);

            string licenseFilePath = _configuration.GetValue<string>("License:LicenseFilePath") ?? "license.key";
            await File.WriteAllTextAsync(licenseFilePath, licenseKeyBase64);

            _cache.Remove("LicenseStatus");

            return await ValidateLicenseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating license");
            return new LicenseStatusDto { IsValid = false, Status = "Invalid", Message = $"Error activating license: {ex.Message}" };
        }
    }

    public async Task<string?> GetInstallationIdAsync()
    {
        return await _licenseRepository.GetInstallationIdAsync();
    }

    public async Task<bool> DeactivateLicenseAsync()
    {
        try
        {
            await _licenseRepository.DeactivateLicenseAsync();
            string licenseFilePath = _configuration.GetValue<string>("License:LicenseFilePath") ?? "license.key";
            if (File.Exists(licenseFilePath))
            {
                File.Delete(licenseFilePath);
            }
            _cache.Remove("LicenseStatus");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating license");
            return false;
        }
    }

    public async Task UpdateLastCheckedAsync()
    {
        try
        {
            await _licenseRepository.UpdateLastCheckedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating last checked timestamp");
        }
    }
}
