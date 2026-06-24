namespace Server.Services
{
    public interface ILocalFolderPollingManager
    {
        bool IsEnabled { get; }
        SemaphoreSlim PollingLock { get; }
        void NotifyConfigurationChanged(bool isEnabled);
        Task WaitForEnabledAsync(CancellationToken cancellationToken);
    }
}
