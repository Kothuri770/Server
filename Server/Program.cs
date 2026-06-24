using Microsoft.AspNetCore.Authentication;
using TrueCapture.Services;
using Server.Hubs;
using Server.Repositories;
using Server.Services;
using Server.Services.Configuration;
using Server.Services.DMS;
using Server.Services.Scanner;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using System.Threading.RateLimiting;
using System.Data;

// Cached singleton — avoid creating a new handler on every request (#26)
var _cachedJwtHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.



var connectionString = builder.Configuration.GetConnectionString("TrueCaptureDb") 
    ?? throw new InvalidOperationException("Connection string 'TrueCaptureDb' not found.");
var databaseProvider = builder.Configuration.GetSection("ConnectionStrings")["DatabaseProvider"] ?? "PostgreSql";
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not found.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer not found.");
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience not found.");

// Services
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddScoped<IBatchLockService>(sp => new BatchLockService(builder.Configuration, sp.GetRequiredService<ILogger<BatchLockService>>(), sp.GetRequiredService<IHubContext<BatchLockHub>>(), databaseProvider));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

// Response Compression — reduces JSON/HTML payload sizes by 60-80% (#22)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "text/plain",
        "image/svg+xml"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

// Configure FormOptions for large file uploads
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000; // 500MB
});

// Configure Kestrel for large file uploads
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 524288000; // 500MB
});

// Dapper & Database
builder.Services.AddScoped<IUserRepository>(sp => new UserRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IBatchRepository>(sp => new BatchRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IVerifyRepository>(sp => new VerifyRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IMonitorRepository>(sp => new MonitorRepository(connectionString, databaseProvider));
builder.Services.AddScoped<ITestRepository>(sp => new TestRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IConfigurationService>(sp => new ConfigurationService(connectionString, databaseProvider, sp.GetRequiredService<DmsConnectorManager>(), sp.GetRequiredService<IMemoryCache>()));
builder.Services.AddScoped<IDocumentSampleRepository>(sp => new DocumentSampleRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IOcrConnectorService>(sp => new OcrConnectorService(connectionString, databaseProvider));

// Business Services
builder.Services.AddScoped<ILookupService>(sp => new LookupService(
    sp.GetRequiredService<ILookupTableRepository>(),
    sp.GetRequiredService<ILookupTableValueRepository>(),
    sp.GetRequiredService<IPropertyValidationRuleRepository>(),
    sp.GetRequiredService<IPropertyLookupMappingRepository>(),
    sp.GetRequiredService<IDatabaseLookupRepository>(),
    builder.Configuration,
    sp.GetRequiredService<ILogger<LookupService>>(),
    databaseProvider));

// Register repositories with connection string
builder.Services.AddScoped<ILookupTableRepository>(sp => new LookupTableRepository(connectionString, databaseProvider));
builder.Services.AddScoped<ILookupTableValueRepository>(sp => new LookupTableValueRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IPropertyValidationRuleRepository>(sp => new PropertyValidationRuleRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IPropertyLookupMappingRepository>(sp => new PropertyLookupMappingRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IDatabaseLookupRepository>(sp => new DatabaseLookupRepository(connectionString, databaseProvider));

builder.Services.AddScoped<IReportRepository>(sp => new ReportRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IReportService, ReportService>();

// Database Initialization Service
builder.Services.AddSingleton<IDatabaseInitializerService>(sp => new DatabaseInitializerService(connectionString, databaseProvider, sp.GetRequiredService<ILogger<DatabaseInitializerService>>()));

// DMS Connectors
builder.Services.AddTransient<IDmsConnector, AzureBlobConnector>();
builder.Services.AddTransient<IDmsConnector, AwsS3Connector>();
builder.Services.AddTransient<IDmsConnector, AlfrescoConnector>();
builder.Services.AddTransient<IDmsConnector, FileNetConnector>();
builder.Services.AddSingleton<DmsConnectorManager>();


// Service Registration
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IOcrService, OcrService>();
builder.Services.AddScoped<ISeparationService, SeparationService>();
builder.Services.AddScoped<IImageTransformationService, ImageTransformationService>();
builder.Services.AddScoped<IOcrEngineService, OcrEngineService>();
builder.Services.AddScoped<ILocalFolderRepository>(sp => new LocalFolderRepository(connectionString, databaseProvider));
builder.Services.AddScoped<IScannerService>(sp => new ScannerService(
    sp.GetRequiredService<IConfigurationService>(),
    sp.GetRequiredService<ILogger<ScannerService>>(),
    sp.GetRequiredService<IBatchRepository>(),
    sp.GetRequiredService<IFileStorageService>(),
    builder.Configuration,
    databaseProvider));
builder.Services.AddScoped<IFileBasedBatchLogService, FileBasedBatchLogService>();
builder.Services.AddScoped<IBatchLogService>(sp => new BatchLogService(connectionString, databaseProvider, sp.GetRequiredService<IConfigurationService>(), sp.GetRequiredService<ILogger<BatchLogService>>()));
builder.Services.AddScoped<IImageToTiffService, ImageToTiffService>();
builder.Services.AddScoped<IImageToPdfService, ImageToPdfService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<Server.Services.KeycloakAuthService>();

builder.Services.AddSingleton<IPurgeConfigService, PurgeConfigService>();

// License Services
builder.Services.AddSingleton<ILicenseService>(sp => new LicenseService(
    connectionString, databaseProvider,
    sp.GetRequiredService<ILogger<LicenseService>>(),
    builder.Configuration,
    sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
builder.Services.AddScoped<ILicenseRepository>(sp => new LicenseRepository(connectionString, databaseProvider));
builder.Services.AddHostedService<Server.Services.LicenseValidationBackgroundService>();

// Background Services
builder.Services.AddHostedService<Server.Services.ReleaseDocumentsService>(provider =>
    new Server.Services.ReleaseDocumentsService(
        provider.GetRequiredService<ILogger<Server.Services.ReleaseDocumentsService>>(),
        provider,
        provider.GetRequiredService<IConfiguration>(),
        provider.GetRequiredService<DmsConnectorManager>(),
        provider.GetRequiredService<IHubContext<BatchLockHub>>()
    ));
builder.Services.AddHostedService<Server.Services.OcrProcessingService>(provider =>
    new Server.Services.OcrProcessingService(
        provider.GetRequiredService<ILogger<Server.Services.OcrProcessingService>>(),
        provider,
        provider.GetRequiredService<IConfiguration>(),
        provider.GetRequiredService<IHubContext<BatchLockHub>>()
    ));
builder.Services.AddHostedService<Server.Services.CleanupExpiredBatchLocksService>();
builder.Services.AddHostedService<Server.Services.AutoSeparationService>();
builder.Services.AddHostedService<PurgingBackgroundService>();

// Database Initialization Service
builder.Services.AddHostedService<DatabaseInitializationHostedService>();

// Email Services
builder.Services.AddScoped<IEmailRepository>(sp => new EmailRepository(connectionString, databaseProvider));
builder.Services.AddScoped<ISftpRepository>(sp => new SftpRepository(connectionString, databaseProvider));
builder.Services.AddSingleton<IEmailPollingManager, EmailPollingManager>();
builder.Services.AddSingleton<ILocalFolderPollingManager, LocalFolderPollingManager>();
builder.Services.AddSingleton<ISftpPollingManager, SftpPollingManager>();

builder.Services.AddHostedService<EmailPollingService>();
builder.Services.AddHostedService<LocalFolderPollingService>();
builder.Services.AddHostedService<SftpPollingService>();

// Keep-Alive Service — prevents IIS from killing the worker process
// due to idle timeout (root cause of WAS Event 5186)
builder.Services.AddHostedService<Server.Services.KeepAliveService>();



// CORS for Blazor WASM and Swagger — restrict to configured origins when available (#9)
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClient", policy =>
    {
        if (corsOrigins != null && corsOrigins.Length > 0)
        {
            // When specific origins are configured, allow credentials (needed for SignalR auth).
            policy.WithOrigins(corsOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // IMPORTANT: AllowAnyOrigin() and AllowCredentials() cannot be combined —
            // this throws a runtime exception. Use SetIsOriginAllowed for wildcard + credentials.
            // This fallback allows all origins with credentials for development/unconfigured environments.
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// #12: Fixed-window rate limiting — 100 requests per minute per IP (built-in .NET 8)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Exempt SignalR and health endpoints from rate limiting
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/batchlockhub", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("exempt");
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10
        });
    });
});

// Map claims using custom transformation
builder.Services.AddTransient<IClaimsTransformation, KeycloakClaimsTransformation>();
builder.Services.AddSingleton<ISystemPerformanceService, SystemPerformanceService>();

// JWT Authentication Configuration - Dual Schemes (Local DB + Keycloak)
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "SmartBearer";
    options.DefaultChallengeScheme = "SmartBearer";
})
.AddPolicyScheme("SmartBearer", "Bearer", options =>
{
    // Forward the request to the correct scheme based on the token issuer
    options.ForwardDefaultSelector = context =>
    {
        string? authorization = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer "))
        {
            var token = authorization.Substring("Bearer ".Length).Trim();
            // Use cached handler singleton instead of creating one per request (#26)
            if (_cachedJwtHandler.CanReadToken(token))
            {
                var jwtToken = _cachedJwtHandler.ReadJwtToken(token);
                var kRealm = builder.Configuration["Keycloak:Realm"] ?? "master";
                if (jwtToken.Issuer.Contains("Keycloak", StringComparison.OrdinalIgnoreCase) || jwtToken.Issuer.Contains(kRealm, StringComparison.OrdinalIgnoreCase))
                {
                    return "Keycloak";
                }
            }
        }
        return "Local"; // Default fallback
    };
})
.AddJwtBearer("Local", options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey ?? "DefaultSecretKeyPlaceholderForRemediation"))
    };
})
.AddJwtBearer("Keycloak", options =>
{
    var keycloakServerUrl = builder.Configuration["Keycloak:ServerUrl"]?.TrimEnd('/');
    var keycloakRealm = builder.Configuration["Keycloak:Realm"];
    options.Authority = $"{keycloakServerUrl}/realms/{keycloakRealm}";
    options.RequireHttpsMetadata = false; // Disable if running on http:// locally
    
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = $"{keycloakServerUrl}/realms/{keycloakRealm}",
        ValidateAudience = false, // Relax audience validation for Keycloak tokens to prevent mismatches
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
    options.AddPolicy("CanConfigure", policy => policy.RequireClaim("CanConfigure", "true"));
});

var app = builder.Build();


// Restrict Swagger to Development environment (#27)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Response compression must be before other middleware that reads responses (#22)
app.UseResponseCompression();

app.UseWebSockets();
app.UseCors("BlazorClient");
app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// License validation — blocks requests when license is expired/not activated
app.UseMiddleware<Server.Services.LicenseValidationMiddleware>();

// #12: Rate limiter middleware — must be after auth/cors, before endpoints
app.UseRateLimiter();

app.MapControllers();
app.MapHub<BatchLockHub>(BatchLockHub.HubUrl);

// #28: Health check endpoint with database connectivity probe
app.MapGet("/health", async (ITestRepository repo, ILicenseService licenseService) =>
{
    var dbStatus = "Unknown";
    try
    {
        // Probe DB with a lightweight connection open/close
        var canConnect = await repo.TestDatabaseConnectionAsync();
        dbStatus = canConnect ? "Connected" : "Unreachable";
    }
    catch (Exception ex)
    {
        dbStatus = $"Error: {ex.Message}";
    }

    var isHealthy = dbStatus == "Connected";
    var licenseStatus = await licenseService.ValidateLicenseAsync();

    var result = new
    {
        Status = isHealthy ? "Healthy" : "Degraded",
        Service = "TrueCapture API",
        Timestamp = DateTime.UtcNow,
        Environment = app.Environment.EnvironmentName,
        Database = dbStatus,
        LicenseStatus = licenseStatus.Status
    };

    return isHealthy ? Results.Ok(result) : Results.Json(result, statusCode: 503);
}).AllowAnonymous();

app.Run();
