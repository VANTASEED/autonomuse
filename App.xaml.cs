using Autonomuse.Services;

namespace Autonomuse
{
    public partial class App : Application
    {
        private readonly Shared.Contracts.IDownloadNotificationService _notifications;
        private readonly Services.Orchestration.ToolAutoUpdateService _toolAutoUpdate;

        public App(Shared.Contracts.IDownloadNotificationService notifications, Services.Orchestration.ToolAutoUpdateService toolAutoUpdate)
        {
            _notifications = notifications;
            _toolAutoUpdate = toolAutoUpdate;
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            _ = UpdateService.CheckForUpdateAsync();
            _ = Task.Run(_toolAutoUpdate.RunIfDueAsync);
            var window = new Window(new MainPage()) { Title = "Autonomuse" };
            window.Destroying += (_, _) => (_notifications as IDisposable)?.Dispose();
            return window;
        }
    }
}
