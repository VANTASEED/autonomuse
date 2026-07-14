using Autonomuse.Domain.Entities;

namespace Autonomuse.Shared.Contracts
{
    public interface ISettingsService
    {
        bool IsDebugMode { get; }
        Task InitializeCoreSettingsAsync();
        
        Task<string?> GetSettingAsync(string parameter);
        Task SaveSettingAsync(string parameter, string value);
        
        Task<List<string>> GetPathHistoryAsync();
        Task AddPathHistoryAsync(string path);
        Task RemovePathHistoryAsync(string path);

        Task SaveToolVersionAsync(string toolName, string version);
    }
}
