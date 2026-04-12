namespace Autonomuse.Shared.Contracts
{
    public interface IFolderPicker
    {
        Task<string?> PickFolderAsync();
    }
}
