using Microsoft.AspNetCore.Http;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Server.Services;

public class LicenseValidationMiddleware
{
    private readonly RequestDelegate _next;

    public LicenseValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILicenseService licenseService)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        // Exempt paths
        if (path.StartsWith("/api/license") ||
            path.StartsWith("/api/auth/login") ||
            path.StartsWith("/api/auth/keycloak-login") ||
            path == "/health" ||
            path == "/api/health" ||
            path.StartsWith("/batchlockhub"))
        {
            await _next(context);
            return;
        }

        var status = await licenseService.ValidateLicenseAsync();

        if (status.Status == "Active" || status.Status == "GracePeriod")
        {
            if (status.Status == "GracePeriod")
            {
                context.Response.Headers.Add("X-License-Warning", "GracePeriod");
            }
            await _next(context);
            return;
        }

        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";

        string errorCode = status.Status == "NotActivated" ? "LICENSE_NOT_ACTIVATED" : "LICENSE_EXPIRED";
        
        var responseObj = new
        {
            error = errorCode,
            message = status.Message
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(responseObj));
    }
}
