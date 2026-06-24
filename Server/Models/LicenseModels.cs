using System;

namespace Server.Models;

public class LicensePayload
{
    public string LicenseId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string LicenseType { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string ProductVersion { get; set; } = "1.0";
}

public class LicenseFile
{
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class LicenseStatusDto
{
    public bool IsValid { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string LicenseType { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
    public int? DaysRemaining { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
}

public class ActivateLicenseRequest
{
    public string LicenseKey { get; set; } = string.Empty;
}
