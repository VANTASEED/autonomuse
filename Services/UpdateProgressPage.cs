using Microsoft.Maui.Controls.Shapes;

namespace Autonomuse.Services;

public class UpdateProgressPage : ContentPage
{
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Button _cancelButton;
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken Token => _cts.Token;
    public bool IsCancelled { get; private set; }

    public UpdateProgressPage()
    {
        BackgroundColor = Color.FromArgb("#0f0f0f");

        var title = new Label
        {
            Text = "Downloading Update",
            TextColor = Color.FromArgb("#e8a30e"),
            FontSize = 20,
            HorizontalOptions = LayoutOptions.Center,
            FontAttributes = FontAttributes.Bold,
        };

        _statusLabel = new Label
        {
            Text = "Starting download...",
            TextColor = Color.FromArgb("#e0e0e0"),
            HorizontalOptions = LayoutOptions.Center,
            FontSize = 14,
        };

        _progressBar = new ProgressBar
        {
            Progress = 0,
            ProgressColor = Color.FromArgb("#e8a30e"),
            HeightRequest = 6,
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            BackgroundColor = Color.FromArgb("#252525"),
            TextColor = Color.FromArgb("#ccc"),
            BorderWidth = 1,
            BorderColor = Color.FromArgb("#333"),
            CornerRadius = 19,
            Padding = new Thickness(16, 10),
            FontSize = 13,
            HeightRequest = 38,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 140,
        };
        _cancelButton.Clicked += (_, _) =>
        {
            IsCancelled = true;
            _cancelButton.IsEnabled = false;
            _cancelButton.Text = "Cancelling...";
            _cts.Cancel();
        };

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#151515"),
            Stroke = Color.FromArgb("#333"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(24, 32),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 380,
            Content = new VerticalStackLayout
            {
                Spacing = 20,
                Children = { title, _statusLabel, _progressBar, _cancelButton }
            }
        };

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb("#0f0f0f"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Children = { card }
        };
    }

    public void SetProgress(double fraction, string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _progressBar.Progress = fraction;
            _statusLabel.Text = text;
        });
    }
}
