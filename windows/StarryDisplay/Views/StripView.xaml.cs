using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using StarryDisplay.Core;

namespace StarryDisplay.Views;

/// <summary>
/// Transparent lyrics strip: current line + next line floating over the desktop or a game.
/// Pairs with click-through (Ctrl+Alt+L). Track info shows on hover and for a few seconds
/// after each track change, then gets out of the way.
/// </summary>
public partial class StripView : UserControl, IComposition
{
    private readonly LyricsView _lyrics = new(LyricsLayout.Strip);
    private readonly DispatcherTimer _headerTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private NowPlayingViewModel? _vm;

    public StripView()
    {
        InitializeComponent();
        LyricsHost.Child = _lyrics;
        _headerTimer.Tick += (_, _) => { _headerTimer.Stop(); if (!IsMouseOver) HideHeader(); };
        MouseEnter += (_, _) => ShowHeader();
        MouseLeave += (_, _) => { if (!_headerTimer.IsEnabled) HideHeader(); };
    }

    public string Id => Layouts.Strip;
    public bool IsOpaque => false;
    public Size DefaultSize => new(940, 168);
    public FrameworkElement View => this;

    public void Attach(NowPlayingViewModel vm, AppSettings settings)
    {
        _vm = vm;
        vm.ItemChanged += OnItemChanged;
        vm.ArtworkChanged += OnArtworkChanged;
        vm.LyricsChanged += OnLyricsChanged;
        vm.StateChanged += OnStateChanged;
        _lyrics.Attach(vm.Clock);
        ApplySettings(settings);
        ApplyHeader();
        Thumb.Source = vm.Artwork?.Image;
        ShowLyrics();
    }

    public void Detach()
    {
        if (_vm is null) return;
        _vm.ItemChanged -= OnItemChanged;
        _vm.ArtworkChanged -= OnArtworkChanged;
        _vm.LyricsChanged -= OnLyricsChanged;
        _vm.StateChanged -= OnStateChanged;
        _lyrics.Detach();
        _headerTimer.Stop();
        _vm = null;
    }

    public void ApplySettings(AppSettings settings) => _lyrics.FontScale = settings.LyricsScale;

    private void OnItemChanged(object? sender, EventArgs e)
    {
        ApplyHeader();
        ShowHeader();
        _headerTimer.Stop();
        _headerTimer.Start();
    }

    private void OnArtworkChanged(object? sender, EventArgs e)
    {
        Thumb.Source = _vm?.Artwork?.Image;
        if (_vm is not null) _lyrics.SetAccent(_vm.Accent);
    }

    private void OnLyricsChanged(object? sender, EventArgs e) => ShowLyrics();

    private string? _lastMessage;
    private bool _lastLive;

    private bool IsLive => _vm?.State is DisplayState.Playing or DisplayState.Paused or DisplayState.Offline or DisplayState.RateLimited;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        ApplyHeader();
        if (IsLive != _lastLive || (!IsLive && Message() != _lastMessage)) ShowLyrics();
    }

    private void ShowLyrics()
    {
        if (_vm is null) return;
        _lastLive = IsLive;
        _lastMessage = Message();
        // In message states (ad, nothing playing…) the strip shows the state instead of lyrics.
        if (_lastLive) _lyrics.Show(_vm.Lyrics, _vm.LyricsLoading);
        else _lyrics.Show(null, false, _lastMessage);
    }

    private string? Message() => _vm?.State switch
    {
        DisplayState.Ad => "Ad break",
        DisplayState.NothingPlaying => "Nothing playing",
        DisplayState.Connecting => "Connecting…",
        DisplayState.Unsupported => "Private playback",
        DisplayState.SignedOut => "Connect Spotify in Settings",
        _ => null
    };

    private void ApplyHeader()
    {
        if (_vm is null) return;
        var state = _vm.State switch
        {
            DisplayState.Paused => "  ·  Paused",
            DisplayState.Offline => "  ·  Offline",
            _ => ""
        };
        HeaderText.Text = _vm.HasItem ? $"{_vm.Title}  —  {_vm.Artist}{state}" : _vm.StatusTitle;
    }

    private void ShowHeader() => Anim.To(Header, OpacityProperty, 1, 220);
    private void HideHeader() => Anim.To(Header, OpacityProperty, 0, 420);
}
