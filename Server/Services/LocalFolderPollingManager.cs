using Server.Repositories;

namespace Server.Services
{
    public class LocalFolderPollingManager : ILocalFolderPollingManager, IDisposable
    {
        private bool _isEnabled;
        private TaskCompletionSource<bool> _tcs;
        private readonly object _lock = new object();
        public SemaphoreSlim PollingLock { get; } = new SemaphoreSlim(1, 1);

        public bool IsEnabled => _isEnabled;

        public LocalFolderPollingManager()
        {
            _isEnabled = false;
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void NotifyConfigurationChanged(bool isEnabled)
        {
            lock (_lock)
            {
                if (_isEnabled == isEnabled) return;

                _isEnabled = isEnabled;

                if (isEnabled)
                {
                    _tcs.TrySetResult(true);
                }
                else
                {
                    if (_tcs.Task.IsCompleted)
                    {
                        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                }
            }
        }

        public async Task WaitForEnabledAsync(CancellationToken cancellationToken)
        {
            Task currentTask;
            lock (_lock)
            {
                if (_isEnabled) return;
                currentTask = _tcs.Task;
            }

            var tcs = new TaskCompletionSource();
            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                await Task.WhenAny(currentTask, tcs.Task);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        public void Dispose()
        {
            PollingLock.Dispose();
        }
    }
}
