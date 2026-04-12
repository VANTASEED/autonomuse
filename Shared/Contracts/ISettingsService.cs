using Autonomuse.Domain.Entities;

namespace Autonomuse.Shared.Contracts
{
    public interface ISettingsService
    {
        Task<string?> GetSettingAsync(string parameter);
        Task SaveSettingAsync(string parameter, string value);
        
        Task<List<string>> GetPathHistoryAsync();
        Task AddPathHistoryAsync(string path);
        Task RemovePathHistoryAsync(string path);
    }
}
