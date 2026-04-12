using Autonomuse.Shared.Contracts;
using WindowsFolderPicker = Windows.Storage.Pickers.FolderPicker;

namespace Autonomuse.Platforms.Windows
{
    public class FolderPickerImplementation : IFolderPicker
    {
        public async Task<string?> PickFolderAsync()
        {
            var folderPicker = new WindowsFolderPicker();
            folderPicker.FileTypeFilter.Add("*");

            // Get the current window's HWND
            var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows[0].Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            
            if (mauiWindow == null)
            {
                return null;
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mauiWindow);

            // Initialize the picker with the window handle
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var result = await folderPicker.PickSingleFolderAsync();
            return result?.Path;
        }
    }
}
