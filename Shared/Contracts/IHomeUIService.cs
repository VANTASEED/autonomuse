using Autonomuse.Shared.DTOs;

namespace Autonomuse.Shared.Contracts
{
    public interface IHomeUIService
    {
        Task<HomeUISettings> GetSettingsAsync();
        Task SaveSettingsAsync(HomeUISettings settings);
        Task<string> SaveBackgroundAsync(string tabName, string sourcePath);
        Task RemoveBackgroundAsync(string tabName);
        Task<string> GetBackgroundAsBase64Async(string tabName);
        Task<string> GetThumbnailAsBase64Async(string tabName);
        Task ResetAllBackgroundsAsync();
        void Initialize();
    }
}
