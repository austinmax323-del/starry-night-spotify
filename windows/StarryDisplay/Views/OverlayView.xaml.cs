using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using StarryDisplay.Core;
using StarryDisplay.Lyrics;

namespace StarryDisplay.Views;

/// <summary>
/// The gaming overlay's content: a small card with (per density) artwork, the track, the current lyric line,
/// the next line and a progress bar. Same view model and lyric engine as the desktop display, so it adds no
/// polling of its own. Updates are event-driven: lyrics move on a one-shot timer, and the progress bar
/// (Full density only) ticks once a second while playing.
/// </summary>
public partial class OverlayView : UserControl, IComposition
{
    /// <summary>Transitions run at 30 fps: every frame of the transparent overlay window is read back from the GPU.</summary>
    private const int FrameRate = 30;
    private readonly LyricsView _lyrics = new(LyricsLayout.Overlay);
    private readonly SolidColorBrush _accent = new(Services.ArtworkService.DefaultAccent);
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private NowPlayingViewModel? _vm;
    private OverlaySettings _overlay = new();
    private bool _interactive;

    public OverlayView()
    {
        InitializeComponent();
        LyricsHost.Child = _lyrics;
        _lyrics.FrameRate = FrameRate;
        ProgressFill.Background = _accent;
        _progressTimer.Tick += (_, _) => UpdateProgress();
        IsVisibleChanged += (_, _) => UpdateProgressTimer();
        MouseEnter += (_, _) => UpdateControls();
        MouseLeave += (_, _) => UpdateControls();
    }

    /// <summary>Same command strings as the toolbar and tray.</summary>
    public event Action<string>? CommandRequested;

    public string Id => Presentations.Overlay;
    public bool IsOpaque => false;
    public Size DefaultSize => BaseSize(_overlay.Density);
    public FrameworkElement View => this;

    /// <summary>Card size in DIPs at scale 1. Fixed per density, so lyric changes never resize (or re-place) the window.</summary>
    public static Size BaseSize(string density) => density switch
    {
        OverlayDensities.Minimal => new Size(420, 76),
        OverlayDensities.Full => new Size(460, 132),
        _ => new Size(440, 96)
    };

    public void Attach(NowPlayingViewModel vm, AppSettings settings)
    {
        _vm = vm;
        vm.ItemChanged += OnItemChanged;
        vm.ArtworkChanged += OnArtworkChanged;
        vm.LyricsChanged += OnLyricsChanged;
        vm.StateChanged += OnStateChanged;
        vm.Clock.Changed += OnClockChanged;
        _lyrics.Attach(vm.Clock);
        ApplySettings(settings);
        OnArtworkChanged(null, EventArgs.Empty);
        ApplyMeta();
        ShowLyrics();
        UpdateProgress();
    }

    public void Detach()
    {
        if (_vm is null) return;
        _vm.ItemChanged -= OnItemChanged;
        _vm.ArtworkChanged -= OnArtworkChanged;
        _vm.LyricsChanged -= OnLyricsChanged;
        _vm.StateChanged -= OnStateChanged;
        _vm.Clock.Changed -= OnClockChanged;
        _lyrics.Detach();
        _progressTimer.Stop();
        _vm = null;
    }

    public void ApplySettings(AppSettings settings)
    {
        _overlay = settings.Overlay;
        var density = _overlay.Density;
        var size = BaseSize(density);
        Width = size.Width;
        Height = size.Height;

        var full = density == OverlayDensities.Full;
        var minimal = density == OverlayDensities.Minimal;
        var art = _overlay.ShowArtwork && !minimal;
        ArtFrame.Visibility = art ? Visibility.Visible : Visibility.Collapsed;
        ArtFrame.Width = ArtFrame.Height = full ? 72 : 56;
        ProgressTrack.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        _lyrics.ShowNext = full;
        _lyrics.FontScale = minimal ? 1.1 : 1.0;
        ApplyMeta();
        ShowLyrics();
        UpdateProgressTimer();
    }

    /// <summary>Hover controls appear only when the overlay can be clicked (click-through off).</summary>
    public void SetInteractive(bool interactive)
    {
        _interactive = interactive;
        UpdateControls();
    }

    private void UpdateControls()
    {
        var show = _interactive && IsMouseOver;
        Controls.IsHitTestVisible = show;
        Anim.To(Controls, OpacityProperty, show ? 1 : 0, show ? 140 : 360, fps: FrameRate);
        Card.BorderBrush = _interactive ? new SolidColorBrush(Color.FromArgb(0x66, _accent.Color.R, _accent.Color.G, _accent.Color.B)) : (Brush)FindResource("HairlineBrush");
    }

    private void OnControl(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string command }) CommandRequested?.Invoke(command);
    }

    // ---- view model ---------------------------------------------------------------------------

    private bool IsLive => _vm?.State is DisplayState.Playing or DisplayState.Paused or DisplayState.Offline or DisplayState.RateLimited;
    private bool LyricsOn => _overlay.ShowLyrics;

    private void OnItemChanged(object? sender, EventArgs e)
    {
        ApplyMeta();
        UpdateProgress();
    }

    private void OnArtworkChanged(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        Art.Source = _vm.Artwork?.Image;
        Anim.ColorTo(_accent, _vm.Accent, 600, FrameRate);
        _lyrics.SetAccent(_vm.Accent);
    }

    private void OnLyricsChanged(object? sender, EventArgs e) => ShowLyrics();

    private bool _lastLive;
    private string? _lastMessage;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        ApplyMeta();
        if (IsLive != _lastLive || (!IsLive && Message() != _lastMessage)) ShowLyrics();
        UpdateProgressTimer();
    }

    private void OnClockChanged(object? sender, EventArgs e)
    {
        UpdateProgress();
        UpdateProgressTimer();
    }

    private void ApplyMeta()
    {
        if (_vm is null) return;
        var minimal = _overlay.Density == OverlayDensities.Minimal;
        var hasItem = _vm.HasItem && IsLive;
        var title = hasItem ? _vm.Title : _vm.StatusTitle;
        var artist = hasItem ? _vm.Artist : "";

        // With lyrics on, "Title · Artist" shares one line above the lyric; with lyrics off they get a line each.
        MetaRow.Visibility = minimal && LyricsOn ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Inlines.Clear();
        TitleText.Inlines.Add(new Run(title));
        if (LyricsOn && artist.Length > 0)
            TitleText.Inlines.Add(new Run("  ·  " + artist) { FontWeight = FontWeights.Normal, Foreground = (Brush)FindResource("InkMutedBrush") });
        ArtistText.Text = artist;
        ArtistText.Visibility = LyricsOn ? Visibility.Collapsed : Visibility.Visible;
        LyricsHost.Visibility = LyricsOn ? Visibility.Visible : Visibility.Collapsed;
        StateText.Text = _vm.State switch
        {
            DisplayState.Paused => "Paused",
            DisplayState.Offline => "Offline",
            DisplayState.Ad => "Ad",
            _ => ""
        };
    }

    private void ShowLyrics()
    {
        if (_vm is null) return;
        _lastLive = IsLive;
        _lastMessage = Message();
        if (!LyricsOn) return;
        if (!_lastLive) _lyrics.Show(null, false, _lastMessage);
        // The overlay is too small (and usually click-through) to scroll a whole unsynced sheet.
        else if (_vm.Lyrics is { Kind: LyricsKind.Unsynced }) _lyrics.Show(null, false, "Lyrics aren't time-synced for this one");
        else _lyrics.Show(_vm.Lyrics, _vm.LyricsLoading);
    }

    /// <summary>Message states: the title line already names the state, so the lyric area explains it (Minimal has no title line).</summary>
    private string? Message() => _overlay.Density != OverlayDensities.Minimal && !string.IsNullOrEmpty(_vm?.StatusDetail) && !IsLive
        ? _vm!.StatusDetail
        : ShortMessage();

    private string? ShortMessage() => _vm?.State switch
    {
        DisplayState.Ad => "Ad break",
        DisplayState.NothingPlaying => "Nothing playing",
        DisplayState.Connecting => "Connecting…",
        DisplayState.Unsupported => "Private playback",
        DisplayState.SignedOut => "Connect Spotify in Settings",
        _ => null
    };

    // ---- progress (Full only) -------------------------------------------------------------------

    private void UpdateProgressTimer()
    {
        var run = IsVisible && _overlay.Density == OverlayDensities.Full && _vm?.Clock.IsRunning == true;
        if (run && !_progressTimer.IsEnabled) _progressTimer.Start();
        else if (!run) _progressTimer.Stop();
    }

    private void UpdateProgress()
    {
        if (_vm is null || _overlay.Density != OverlayDensities.Full) return;
        var duration = _vm.Clock.DurationMs;
        var fraction = duration > 0 && IsLive ? Math.Clamp(_vm.Clock.PositionMs / (double)duration, 0, 1) : 0;
        // Skip sub-pixel changes: each repaint of a transparent window costs a GPU readback.
        if (Math.Abs(fraction - ProgressScale.ScaleX) * ProgressTrack.ActualWidth >= 1 || fraction == 0) ProgressScale.ScaleX = fraction;
    }
}
