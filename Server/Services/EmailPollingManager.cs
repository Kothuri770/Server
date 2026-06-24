using System.Threading;
using System.Threading.Tasks;

namespace Server.Services
{
    public interface IEmailPollingManager
    {
        bool IsEnabled { get; }
        void NotifyConfigurationChanged(bool isEnabled);
        Task WaitForEnabledAsync(CancellationToken cancellationToken);
        SemaphoreSlim PollingLock { get; }
    }

    public class EmailPollingManager : IEmailPollingManager
    {
        private bool _isEnabled;
        private TaskCompletionSource _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _pollingLock = new SemaphoreSlim(1, 1);

        public SemaphoreSlim PollingLock => _pollingLock;

        public bool IsEnabled 
        {
            get
            {
                lock (_lock) return _isEnabled;
            }
        }

        public void NotifyConfigurationChanged(bool isEnabled)
        {
            lock (_lock)
            {
                _isEnabled = isEnabled;
                if (isEnabled)
                {
                    _tcs.TrySetResult();
                }
                else
                {
                    if (_tcs.Task.IsCompleted)
                    {
                        _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                }
            }
        }

        public async Task WaitForEnabledAsync(CancellationToken cancellationToken)
        {
            if (IsEnabled) return;

            Task taskToAwait;
            lock (_lock)
            {
                taskToAwait = _tcs.Task;
            }

            using var registration = cancellationToken.Register(() => 
            {
                lock (_lock)
                {
                    _tcs.TrySetCanceled();
                }
            });

            try
            {
                await taskToAwait;
            }
            catch (TaskCanceledException)
            {
                // Ignore cancellation if it's from app shutdown
            }
        }
    }
}
