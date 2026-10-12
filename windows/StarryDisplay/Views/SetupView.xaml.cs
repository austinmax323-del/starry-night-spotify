using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StarryDisplay.Services;

namespace StarryDisplay.Views;

/// <summary>First-run / signed-out card: paste a Client ID and connect with PKCE.</summary>
public partial class SetupView : UserControl
{
    /// <summary>Raised with the Client ID when the user clicks Connect. The host runs the sign-in.</summary>
    public event Func<string, Task>? ConnectRequested;

    public SetupView()
    {
        InitializeComponent();
        Stars.Fill = Anim.CreateStarField(seed: 11);
        RedirectText.Text = SpotifyService.RedirectUri;
        SizeChanged += (_, _) => Card.Clip = new RectangleGeometry(new Rect(RenderSize), 22, 22);
    }

    public string ClientId
    {
        get => ClientIdBox.Text.Trim();
        set => ClientIdBox.Text = value;
    }

    public void SetStatus(string text) => StatusText.Text = text;

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        if (ConnectRequested is null) return;
        ConnectButton.IsEnabled = false;
        StatusText.Text = "Finish signing in to Spotify in your browser…";
        try
        {
            await ConnectRequested(ClientId);
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            Log.Write("Spotify connect failed.", ex);
            StatusText.Text = ex.Message;
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void OnOpenDashboard(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://developer.spotify.com/dashboard") { UseShellExecute = true });

    private void OnCopyRedirect(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(SpotifyService.RedirectUri); StatusText.Text = "Redirect URI copied."; }
        catch { /* clipboard busy; harmless */ }
    }
}
