using Autonomuse.Services;

namespace Autonomuse
{
    public partial class App : Application
    {
        private readonly Shared.Contracts.IDownloadNotificationService _notifications;

        public App(Shared.Contracts.IDownloadNotificationService notifications)
        {
            _notifications = notifications;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            _ = UpdateService.CheckForUpdateAsync();
            var window = new Window(new MainPage()) { Title = "Autonomuse" };
            window.Destroying += (_, _) => (_notifications as IDisposable)?.Dispose();
            return window;
        }
    }
}
