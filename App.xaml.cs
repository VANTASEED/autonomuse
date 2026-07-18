using Autonomuse.Services;

namespace Autonomuse
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            _ = UpdateService.CheckForUpdateAsync();
            return new Window(new MainPage()) { Title = "Autonomuse" };
        }
    }
}
