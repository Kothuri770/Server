using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace Server.Hubs;

[Authorize]
public class BatchLockHub : Hub
{
    public const string HubUrl = "/batchlockhub";

    public override async Task OnConnectedAsync()
    {
        // #11: All clients auto-join a "monitor" group for dashboard broadcasts
        await Groups.AddToGroupAsync(Context.ConnectionId, "monitor");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "monitor");
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// #11: Clients can join a step-specific group to receive only relevant batch events.
    /// Example: Client viewing Verify step joins "step_3" to only get batches entering that step.
    /// </summary>
    public async Task JoinStepGroup(int stepId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"step_{stepId}");
    }

    public async Task LeaveStepGroup(int stepId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"step_{stepId}");
    }

    /// <summary>
    /// #11: Clients can join a batch-specific group to track a single batch lifecycle.
    /// </summary>
    public async Task JoinBatchGroup(int batchId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"batch_{batchId}");
    }

    public async Task LeaveBatchGroup(int batchId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"batch_{batchId}");
    }

    public async Task NotifyBatchLocked(int batchId, string userId, string userName, DateTime expirationTime)
    {
        await Clients.Group("monitor").SendAsync("BatchLocked", batchId, userId, userName, expirationTime);
    }

    public async Task NotifyBatchUnlocked(int batchId)
    {
        await Clients.Group("monitor").SendAsync("BatchUnlocked", batchId);
    }

    public async Task NotifyBatchLockRefreshed(int batchId, DateTime newExpirationTime)
    {
        await Clients.Group("monitor").SendAsync("BatchLockRefreshed", batchId, newExpirationTime);
    }
}