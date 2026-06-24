using Server.Models;

namespace Server.Services;

public interface IBatchLockService
{
    Task<AcquireLockResponse> AcquireLockAsync(int batchId, string userId, string userName, string sessionId);
    Task<bool> ReleaseLockAsync(int batchId, string userId);
    Task<bool> RefreshLockAsync(int batchId, string userId);
    Task<BatchLockInfo?> GetLockStatusAsync(int batchId);
    Task<List<BatchLockInfo>> GetAllActiveLocksAsync();
    Task CleanupExpiredLocksAsync();
}