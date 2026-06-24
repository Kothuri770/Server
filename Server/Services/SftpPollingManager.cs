using System.Threading;

namespace Server.Services
{
    public interface ISftpPollingManager
    {
        bool IsEnabled { get; }
        SemaphoreSlim PollingLock { get; }
        void NotifyConfigurationChanged(bool enabled);
        Task WaitForEnabledAsync(CancellationToken cancellationToken);
    }
 
    public class SftpPollingManager : ISftpPollingManager
    {
        private bool _isEnabled = false;
        private TaskCompletionSource<bool> _isEnabledTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsEnabled => _isEnabled;
        public SemaphoreSlim PollingLock { get; } = new SemaphoreSlim(1, 1);

        public void NotifyConfigurationChanged(bool enabled)
        {
            _isEnabled = enabled;
            if (enabled)
            {
                _isEnabledTcs.TrySetResult(true);
            }
            else
            {
                // Reset TCS for next time it becomes enabled
                if (_isEnabledTcs.Task.IsCompleted)
                {
                    _isEnabledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        public async Task WaitForEnabledAsync(CancellationToken cancellationToken)
        {
            while (!_isEnabled)
            {
                using var registration = cancellationToken.Register(() => _isEnabledTcs.TrySetCanceled());
                try
                {
                    await _isEnabledTcs.Task;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // If task was reset or failed, just loop again
                }
            }
        }
    }
}
