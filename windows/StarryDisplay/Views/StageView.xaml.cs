using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StarryDisplay.Core;

namespace StarryDisplay.Views;

/// <summary>
/// The full "now playing" card: artwork, metadata, synced lyrics sheet, progress.
/// Transitions: artwork + backdrop crossfade, title/artist fade-rise, lyrics fade-reset.
/// Nothing is recreated on a track change; elements are updated in place.
/// </summary>
public partial class StageView : UserControl, IComposition
{
    /// <summary>Below this width the lyrics column is hidden and the card becomes a compact now-playing tile.</summary>
    private const double LyricsMinWidth = 700;

    private readonly LyricsView _lyrics = new(LyricsLayout.Sheet);
    private readonly SolidColorBrush _accent = new(Services.ArtworkService.DefaultAccent);
    private readonly DispatcherTimer _progressTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(250) };
    private NowPlayingViewModel? _vm;
    private AppSettings _settings = new();
    private Image _artFront, _artBack, _backFront, _backBack;
    private int _lastElapsedSecond = -1;
    private bool _statusShown;

    public StageView()
    {
        InitializeComponent();
        _artFront = ArtA; _artBack = ArtB; _backFront = BackA; _backBack = BackB;
        LyricsHost.Child = _lyrics;
        ProgressFill.Background = _accent;
        DeviceGlyph.Foreground = _accent;
        Stars.Fill = Anim.CreateStarField(seed: 7);

        SizeChanged += (_, _) => Relayout();
        IsVisibleChanged += (_, _) => UpdateProgressTimer();
        _progressTimer.Tick += (_, _) => UpdateProgress();
    }

    public string Id => Layouts.Stage;
    public bool IsOpaque => true;
    public Size DefaultSize => new(1000, 400);
    public FrameworkElement View => this;

    public void Attach(NowPlayingViewModel vm, AppSettings settings)
    {
        _vm = vm;
        vm.ItemChanged += OnItemChanged;
        vm.ArtworkChanged += OnArtworkChanged;
        vm.LyricsChanged += OnLyricsChanged;
        vm.StateChanged += OnStateChanged;
        vm.PropertyChanged += OnPropertyChanged;
        vm.Clock.Changed += OnClockChanged;
        _lyrics.Attach(vm.Clock);
        ApplySettings(settings);

        // Render the current state without transitions.
        ApplyMeta();
        ApplyArtwork(animate: false);
        ShowLyrics();
        ApplyState(animate: false);
        UpdateProgress();
    }

    public void Detach()
    {
        if (_vm is null) return;
        _vm.ItemChanged -= OnItemChanged;
        _vm.ArtworkChanged -= OnArtworkChanged;
        _vm.LyricsChanged -= OnLyricsChanged;
        _vm.StateChanged -= OnStateChanged;
        _vm.PropertyChanged -= OnPropertyChanged;
        _vm.Clock.Changed -= OnClockChanged;
        _lyrics.Detach();
        _progressTimer.Stop();
        _vm = null;
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _lyrics.FontScale = settings.LyricsScale;
        BackdropLayer.Visibility = settings.ArtworkBackdrop ? Visibility.Visible : Visibility.Collapsed;
        var sky = SkyBrush(settings);
        BaseFill.Fill = sky ?? (Brush)FindResource("NightBrush");
        // With a theme sky, let it show: thinner legibility veil, quieter artwork glow.
        Veil.Opacity = sky is null ? 1.0 : 0.55;
        BackdropLayer.Opacity = sky is null ? 0.58 : 0.34;
    }

    /// <summary>
    /// Card background from a theme's sky colours (the Mac's Starry Night scheme), blended a quarter
    /// toward the night base so light schemes stay legible under white text.
    /// </summary>
    private static Brush? SkyBrush(AppSettings s)
    {
        var top = Link.LinkProtocol.NormalizeColor(s.SkyTop);
        var bottom = Link.LinkProtocol.NormalizeColor(s.SkyBottom);
        if (top is null || bottom is null) return null;
        var night = Color.FromRgb(0x0A, 0x0D, 0x18);
        Color Mix(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return Color.FromRgb((byte)(c.R * 0.75 + night.R * 0.25), (byte)(c.G * 0.75 + night.G * 0.25), (byte)(c.B * 0.75 + night.B * 0.25));
        }
        var brush = new LinearGradientBrush(Mix(top), Mix(bottom), new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    // ---- events -------------------------------------------------------------------------

    private void OnItemChanged(object? sender, EventArgs e)
    {
        Anim.SwapContent(Meta, ApplyMeta);
        _lastElapsedSecond = -1;
        UpdateProgress();
    }

    private void OnArtworkChanged(object? sender, EventArgs e) => ApplyArtwork(animate: true);

    private void OnLyricsChanged(object? sender, EventArgs e) => ShowLyrics();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        ApplyState(animate: true);
        ShowLyricsIfStateMessageChanged();
        UpdateProgressTimer();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingViewModel.DeviceName) or nameof(NowPlayingViewModel.DeviceGlyph)) ApplyDevice();
        if (e.PropertyName is nameof(NowPlayingViewModel.IsPlaying)) ApplyPlayGlyph();
    }

    private void OnClockChanged(object? sender, EventArgs e)
    {
        UpdateProgress();
        UpdateProgressTimer();
    }

    // ---- rendering ----------------------------------------------------------------------

    private void ApplyMeta()
    {
        if (_vm is null) return;
        TitleText.Text = _vm.Title;
        TitleText.ToolTip = string.IsNullOrEmpty(_vm.Title) ? null : _vm.Title;
        ArtistText.Text = _vm.Artist;
        AlbumText.Text = _vm.Item.Kind == PlaybackKind.Track ? _vm.Album : "";
        ApplyDevice();
        ApplyPlayGlyph();
        TotalText.Text = Format(_vm.Item.DurationMs);
        ProgressBlock.Visibility = _vm.Item.DurationMs > 0 ? Visibility.Visible : Visibility.Hidden;
    }

    private void ApplyDevice()
    {
        if (_vm is null) return;
        DeviceText.Text = _vm.DeviceName;
        DeviceGlyph.Text = _vm.DeviceGlyph;
        DeviceChip.Visibility = string.IsNullOrEmpty(_vm.DeviceName) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyPlayGlyph()
    {
        if (_vm is null) return;
        // Only paused gets a glyph; a moving clock already says "playing".
        PlayGlyph.Text = _vm.IsPlaying ? "" : "\uE769";
        Anim.To(ArtBox, OpacityProperty, _vm.State == DisplayState.Paused ? 0.72 : 1.0, 400);
    }

    private void ApplyArtwork(bool animate)
    {
        if (_vm is null) return;
        var art = _vm.Artwork;
        var ms = animate ? 520 : 0;
        _artFront = Anim.Crossfade(_artFront, _artBack, art?.Image, ms);
        _artBack = ReferenceEquals(_artFront, ArtA) ? ArtB : ArtA;
        _backFront = Anim.Crossfade(_backFront, _backBack, art?.Backdrop, animate ? 900 : 0);
        _backBack = ReferenceEquals(_backFront, BackA) ? BackB : BackA;
        Anim.To(ArtPlaceholder, OpacityProperty, art is null ? 0.6 : 0, ms == 0 ? 1 : 300);
        Anim.ColorTo(_accent, _vm.Accent, animate ? 700 : 1);
        _lyrics.SetAccent(_vm.Accent);
    }

    private string? _lastLyricsMessage;

    private void ShowLyrics()
    {
        if (_vm is null) return;
        _lastLyricsMessage = LyricsEmptyMessage();
        _lyrics.Show(_vm.Lyrics, _vm.LyricsLoading, _lastLyricsMessage);
    }

    private void ShowLyricsIfStateMessageChanged()
    {
        if (_vm?.Lyrics is null && !(_vm?.LyricsLoading ?? false) && LyricsEmptyMessage() != _lastLyricsMessage) ShowLyrics();
    }

    private string? LyricsEmptyMessage() => _vm?.State switch
    {
        DisplayState.Ad => "Spotify will be right back.",
        _ => null
    };

    private void ApplyState(bool animate)
    {
        if (_vm is null) return;
        var state = _vm.State;

        // Message states take over the card; live states (incl. offline/rate-limited with a track) keep content.
        var showStatus = state is DisplayState.Connecting or DisplayState.NothingPlaying or DisplayState.Unsupported or DisplayState.SignedOut
                         || (state is DisplayState.Offline or DisplayState.RateLimited && !_vm.HasItem);
        if (showStatus)
        {
            StatusGlyph.Text = state switch
            {
                DisplayState.Offline => "\uE7BA",
                DisplayState.Unsupported => "\uE72E",
                DisplayState.Connecting => "\uE895",
                _ => "\uE708"
            };
            StatusTitle.Text = _vm.StatusTitle;
            StatusDetail.Text = _vm.StatusDetail;
            LastPlayed.Text = state == DisplayState.NothingPlaying && _vm.Item.HasItem ? $"Last played  ·  {_vm.Title} — {_vm.Artist}" : "";
        }
        if (showStatus != _statusShown || !animate)
        {
            _statusShown = showStatus;
            var ms = animate ? 380 : 1;
            Anim.To(StatusPanel, OpacityProperty, showStatus ? 1 : 0, ms);
            Anim.To(LiveContent, OpacityProperty, showStatus ? 0 : 1, ms);
            LiveContent.IsHitTestVisible = !showStatus;
        }

        // State chip for live-but-not-normal conditions.
        (string glyph, string text)? chip = state switch
        {
            DisplayState.Paused => ("\uE769", "Paused"),
            DisplayState.Offline => ("\uE7BA", "Offline · retrying"),
            DisplayState.RateLimited => ("\uE823", "Slowing down"),
            DisplayState.Ad => ("\uE8D6", "Ad break"),
            _ => null
        };
        if (chip is { } c)
        {
            StateGlyph.Text = c.glyph;
            StateText.Text = c.text;
            StateChip.Visibility = Visibility.Visible;
        }
        else StateChip.Visibility = Visibility.Collapsed;

        ApplyPlayGlyph();
    }

    private void UpdateProgressTimer()
    {
        var run = IsVisible && _vm is not null && _vm.Clock.IsRunning && _vm.Clock.DurationMs > 0;
        if (run && !_progressTimer.IsEnabled) _progressTimer.Start();
        else if (!run && _progressTimer.IsEnabled) _progressTimer.Stop();
    }

    private void UpdateProgress()
    {
        if (_vm is null) return;
        var clock = _vm.Clock;
        var pos = clock.PositionMs;
        var fraction = clock.DurationMs > 0 ? Math.Clamp(pos / (double)clock.DurationMs, 0, 1) : 0;
        // Redraw only when the fill moves at least one pixel.
        if (Math.Abs(fraction - ProgressScale.ScaleX) * ProgressFill.ActualWidth >= 1 || fraction == 0) ProgressScale.ScaleX = fraction;
        var second = pos / 1000;
        if (second != _lastElapsedSecond)
        {
            _lastElapsedSecond = second;
            ElapsedText.Text = Format(pos);
            TotalText.Text = Format(clock.DurationMs);
        }
    }

    private static string Format(int ms)
    {
        if (ms <= 0) return "0:00";
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private void Relayout()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Artwork is a square filling the card height, but never more than ~40% of the width.
        var pad = h < 260 ? 18 : 28;
        LiveContent.Margin = new Thickness(pad);
        var art = Math.Max(0, Math.Min(h - pad * 2, w * 0.40));
        ArtBox.Width = ArtBox.Height = art;
        var compactArt = art < 72;
        ArtColumn.Width = compactArt ? new GridLength(0) : GridLength.Auto;
        GapColumn.Width = new GridLength(compactArt ? 0 : (h < 260 ? 20 : 30));
        ArtBox.Visibility = compactArt ? Visibility.Collapsed : Visibility.Visible;

        // Narrow: hide lyrics, keep title/artist/progress. Short: shrink type.
        var showLyrics = w - art >= LyricsMinWidth - 300 && w >= LyricsMinWidth && h >= 230;
        LyricsHost.Visibility = showLyrics ? Visibility.Visible : Visibility.Collapsed;
        Meta.VerticalAlignment = VerticalAlignment.Top;
        TitleText.FontSize = h < 230 ? 22 : 30;
        ArtistText.FontSize = h < 230 ? 15 : 17;
    }
}
