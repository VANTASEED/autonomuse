using Microsoft.AspNetCore.Components.WebView;

namespace Autonomuse
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private async void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        {
// #if WINDOWS
//             // On Windows, e.WebView is the native Microsoft.UI.Xaml.Controls.WebView2 control
//             var webView2 = e.WebView;
//             await webView2.EnsureCoreWebView2Async();

//             // Disable zoom controls and browser accelerator keys (like Ctrl +/-)
//             webView2.CoreWebView2.Settings.IsZoomControlEnabled = false;
//             webView2.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
// #endif
        }
    }
}
