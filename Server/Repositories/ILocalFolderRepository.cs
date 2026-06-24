using Server.Models;

namespace Server.Repositories
{
    public interface ILocalFolderRepository
    {
        Task<LocalFolderConfiguration?> GetConfigurationAsync();
        Task SaveConfigurationAsync(LocalFolderConfiguration config);
        Task UpdateLastCheckedAsync(int id, DateTime lastChecked);
    }
}
