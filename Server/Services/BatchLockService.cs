using Npgsql;
using Server.Models;
using Dapper;
using Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Server.Repositories;

namespace Server.Services;

public class BatchLockService : BaseRepository, IBatchLockService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BatchLockService> _logger;
    private readonly IHubContext<BatchLockHub> _hubContext;

    public BatchLockService(IConfiguration configuration, ILogger<BatchLockService> logger, IHubContext<BatchLockHub> hubContext, string provider)
        : base(configuration.GetConnectionString("TrueCaptureDb") ?? throw new InvalidOperationException("Connection string not configured"), provider)
    {
        _configuration = configuration;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<AcquireLockResponse> AcquireLockAsync(int batchId, string userId, string userName, string sessionId)
    {
        try
        {
            var timeoutMinutes = _configuration.GetValue<int>("AppConfig:BatchLockTimeoutMinutes", 30);
            
            using var connection = CreateConnection();

            string sql;
            if (_provider == "SqlServer")
            {
                // Use dbo.AcquireBatchLock to be explicit
                sql = @"EXEC dbo.AcquireBatchLock @BatchId, @UserId, @UserName, @SessionId, @LockTimeoutMinutes";
            }
            else
            {
                sql = @"SELECT Result, ResultExpirationTime AS ExpirationTime, CurrentLockHolder, LockExpiration FROM AcquireBatchLock(@BatchId, @UserId, @UserName, @SessionId, @LockTimeoutMinutes);";
            }

            var result = await connection.QueryFirstOrDefaultAsync<AcquireLockResponse>(sql, new
            {
                BatchId = batchId,
                UserId = userId,
                UserName = userName,
                SessionId = sessionId,
                LockTimeoutMinutes = timeoutMinutes
            });

            // Send notification if lock was acquired
            if (result?.Result == "ACQUIRED" || result?.Result == "RENEWED")
            {
                // Ensure the time is treated as UTC
                var expirationTime = result.ExpirationTime ?? DateTime.UtcNow.AddMinutes(timeoutMinutes);
                if (expirationTime.Kind == DateTimeKind.Unspecified)
                {
                    expirationTime = DateTime.SpecifyKind(expirationTime, DateTimeKind.Utc);
                }
                await _hubContext.Clients.Group("monitor").SendAsync("BatchLocked", batchId, userId, userName, expirationTime);
            }

            return result ?? new AcquireLockResponse { Result = "ERROR" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error acquiring lock for batch {BatchId} by user {UserId}", batchId, userId);
            return new AcquireLockResponse { Result = "ERROR" };
        }
    }

    public async Task<bool> ReleaseLockAsync(int batchId, string userId)
    {
        try
        {
            string sql;
            if (_provider == "SqlServer")
            {
                sql = "EXEC ReleaseBatchLock @BatchId, @UserId";
            }
            else
            {
                sql = "SELECT ReleaseBatchLock(@BatchId, @UserId);";
            }

            using var connection = CreateConnection();
            var rowsAffected = await connection.QuerySingleAsync<int>(sql, new { BatchId = batchId, UserId = userId });

            if (rowsAffected > 0)
            {
                // Send notification that the batch is unlocked
                await _hubContext.Clients.Group("monitor").SendAsync("BatchUnlocked", batchId);
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing lock for batch {BatchId} by user {UserId}", batchId, userId);
            return false;
        }
    }

    public async Task<bool> RefreshLockAsync(int batchId, string userId)
    {
        try
        {
            var timeoutMinutes = _configuration.GetValue<int>("AppConfig:BatchLockTimeoutMinutes", 30);
 
            string sql;
            if (_provider == "SqlServer")
            {
                sql = "EXEC dbo.RefreshBatchLock @BatchId, @UserId, @LockTimeoutMinutes";
            }
            else
            {
                sql = "SELECT * FROM RefreshBatchLock(@BatchId, @UserId, @TimeoutMinutes)";
            }

            using var connection = CreateConnection();
            var result = await connection.QueryFirstOrDefaultAsync<(string Result, DateTime NewExpirationTime)>(sql, new 
            { 
                BatchId = batchId, 
                UserId = userId,
                LockTimeoutMinutes = timeoutMinutes
            });
            
            var success = result.Result != "NOT_FOUND";

            if (success)
            {
                // Ensure the time is treated as UTC
                var expirationTime = result.NewExpirationTime;
                if (expirationTime.Kind == DateTimeKind.Unspecified)
                {
                    expirationTime = DateTime.SpecifyKind(expirationTime, DateTimeKind.Utc);
                }
                // Send notification that the lock was refreshed
                await _hubContext.Clients.Group("monitor").SendAsync("BatchLockRefreshed", batchId, expirationTime);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing lock for batch {BatchId} by user {UserId}", batchId, userId);
            return false;
        }
    }

    public async Task<BatchLockInfo?> GetLockStatusAsync(int batchId)
    {
        try
        {
            var sql = _provider == "SqlServer"
                ? @"SELECT BatchId, UserId, UserName, CAST(LockAcquired AS DATETIME2) AS LockAcquired, CAST(ExpirationTime AS DATETIME2) AS ExpirationTime, Status
                    FROM BatchLocks
                    WHERE BatchId = @BatchId AND Status = 'Active' AND ExpirationTime > SYSDATETIMEOFFSET()"
                : @"SELECT BatchId, UserId, UserName, LockAcquired, ExpirationTime, Status
                    FROM BatchLocks
                    WHERE BatchId = @BatchId AND Status = 'Active' AND ExpirationTime > NOW()";

            using var connection = CreateConnection();
            var lockInfo = await connection.QueryFirstOrDefaultAsync<BatchLockInfo>(sql, new { BatchId = batchId });

            return lockInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting lock status for batch {BatchId}", batchId);
            return null;
        }
    }

    public async Task<List<BatchLockInfo>> GetAllActiveLocksAsync()
    {
        try
        {
            var sql = _provider == "SqlServer"
                ? @"SELECT BatchId, UserId, UserName, CAST(LockAcquired AS DATETIME2) AS LockAcquired, CAST(ExpirationTime AS DATETIME2) AS ExpirationTime, Status
                    FROM BatchLocks
                    WHERE Status = 'Active' AND ExpirationTime > SYSDATETIMEOFFSET()"
                : @"SELECT BatchId, UserId, UserName, LockAcquired, ExpirationTime, Status
                    FROM BatchLocks
                    WHERE Status = 'Active' AND ExpirationTime > NOW()";

            using var connection = CreateConnection();
            var locks = await connection.QueryAsync<BatchLockInfo>(sql);

            return locks.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all active locks");
            return new List<BatchLockInfo>();
        }
    }

    public async Task CleanupExpiredLocksAsync()
    {
        try
        {
            var sql = _provider == "SqlServer" ? "EXEC dbo.CleanExpiredLocks" : "SELECT CleanExpiredLocks();";

            using var connection = CreateConnection();
            await connection.ExecuteAsync(sql);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired locks");
        }
    }
}