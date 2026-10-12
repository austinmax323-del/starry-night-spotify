using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StarryDisplay.Core;
using StarryDisplay.Lyrics;

namespace StarryDisplay.Views;

public enum LyricsLayout
{
    /// <summary>Full scrolling sheet: context above and below, left-aligned.</summary>
    Sheet,
    /// <summary>Current line + next line, centered (for the overlay strip).</summary>
    Strip
}

/// <summary>
/// Time-aligned lyric display shared by every composition.
/// - No per-frame work: a one-shot timer fires exactly when the next line starts.
/// - On a line change the sheet glides (one short eased animation) and emphasis moves.
/// - Seeks / pause / resume arrive via <see cref="PlaybackClock.Changed"/> and resync immediately.
/// </summary>
public sealed class LyricsView : Grid
{
    /// <summary>Show a line slightly before its timestamp; feels in time given render + poll latency.</summary>
    private const int LeadMs = 180;
    private const int LoadingMessageDelayMs = 450;

    private readonly StackPanel _sheet = new() { VerticalAlignment = VerticalAlignment.Top };
    /// <summary>
    /// Hosts the scrolling sheet. Must be a Canvas: a Grid/Border would arrange the (tall) sheet into a
    /// viewport-sized slot and WPF's layout clip would then hide every line below the first screenful,
    /// moving with the scroll. Canvas measures children at their full size and never layout-clips.
    /// The soft top/bottom edge fade is an OpacityMask on the outer control in viewport-absolute
    /// coordinates, so it stays put while the sheet scrolls underneath it.
    /// </summary>
    private readonly Canvas _viewport = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly TranslateTransform _scroll = new();
    private readonly TextBlock _message = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0
    };
    private readonly DispatcherTimer _lineTimer = new(DispatcherPriority.Render);
    private readonly DispatcherTimer _loadingTimer = new() { Interval = TimeSpan.FromMilliseconds(LoadingMessageDelayMs) };
    private readonly List<TextBlock> _blocks = new();
    private readonly List<int> _times = new();
    private double[] _tops = Array.Empty<double>();
    private double[] _heights = Array.Empty<double>();
    private double[] _targetOpacity = Array.Empty<double>();

    private PlaybackClock? _clock;
    private LyricsKind? _kind;
    private int _active = int.MinValue;
    private bool _appliedRunning;
    private int _version;
    private double _manualOffset;
    private SolidColorBrush _breakBrush = new(Color.FromRgb(0x9F, 0xB4, 0xFF));

    public LyricsView(LyricsLayout layout)
    {
        Layout = layout;
        Background = Brushes.Transparent;
        _sheet.RenderTransform = _scroll;
        _viewport.Children.Add(_sheet);
        Children.Add(_viewport);
        Children.Add(_message);

        _message.FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont");
        _message.Foreground = (Brush)Application.Current.FindResource("InkMutedBrush");

        _lineTimer.Tick += (_, _) => Resync(animate: true);
        _loadingTimer.Tick += (_, _) => { _loadingTimer.Stop(); ShowMessage("Finding lyrics…"); };
        SizeChanged += (_, _) =>
        {
            OpacityMask = Layout == LyricsLayout.Strip ? null : EdgeFade(ActualHeight, 0.16, 0.86);
            Remeasure(jump: true);
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Remeasure(jump: true); }
            else _lineTimer.Stop();
        };
        MouseWheel += OnWheel;
    }

    public LyricsLayout Layout { get; }

    private double _fontScale = 1.0;
    public double FontScale
    {
        get => _fontScale;
        set
        {
            if (Math.Abs(_fontScale - value) < 0.001) return;
            _fontScale = value;
            ApplyFonts();
            Remeasure(jump: true);
        }
    }

    private double BaseSize => (Layout == LyricsLayout.Strip ? 34 : 27) * _fontScale;
    private double Anchor => Layout == LyricsLayout.Strip ? 0.36 : 0.36;

    public void SetAccent(Color accent)
    {
        if (_breakBrush.IsFrozen) _breakBrush = _breakBrush.Clone();
        Anim.ColorTo(_breakBrush, accent, 600);
    }

    public void Attach(PlaybackClock clock)
    {
        Detach();
        _clock = clock;
        _clock.Changed += OnClockChanged;
        Resync(animate: false);
    }

    public void Detach()
    {
        if (_clock is not null) _clock.Changed -= OnClockChanged;
        _clock = null;
        _lineTimer.Stop();
    }

    /// <summary>
    /// Shows a document (or a state). Fades out the old content first so a new song never
    /// flashes the previous song's words.
    /// </summary>
    /// <param name="emptyMessage">Message when <paramref name="doc"/> is null and nothing is loading.</param>
    public void Show(LyricsDocument? doc, bool loading, string? emptyMessage = null)
    {
        var version = ++_version;
        _loadingTimer.Stop();
        var hadContent = _sheet.Opacity > 0.01 || _message.Opacity > 0.01;

        Services.Log.Trace(() => $"Lyrics.Show v{version} kind={doc?.Kind} lines={doc?.Lines.Count} loading={loading} visible={IsVisible} size={ActualWidth:0}x{ActualHeight:0} hadContent={hadContent}");

        void Apply()
        {
            Services.Log.Trace(() => $"Lyrics.Apply v{version} current=v{_version}");
            if (version != _version) return; // superseded by a newer Show()
            Build(doc);
            if (doc is null)
            {
                if (loading) _loadingTimer.Start();
                else if (!string.IsNullOrEmpty(emptyMessage)) ShowMessage(emptyMessage);
                return;
            }

            switch (doc.Kind)
            {
                case LyricsKind.Synced:
                case LyricsKind.Unsynced:
                    Anim.To(_sheet, OpacityProperty, 1, 320);
                    break;
                case LyricsKind.Instrumental:
                    ShowMessage("♪  Instrumental");
                    break;
                case LyricsKind.Unavailable:
                    ShowMessage("Lyrics are unavailable right now");
                    break;
                default:
                    ShowMessage(doc.Source == "podcast" ? "No lyrics for podcasts" : "No lyrics for this one");
                    break;
            }
        }

        if (hadContent && IsVisible)
        {
            Anim.To(_message, OpacityProperty, 0, 140, Anim.EaseInOut);
            Anim.To(_sheet, OpacityProperty, 0, 140, Anim.EaseInOut, Apply);
        }
        else
        {
            Anim.Set(_message, OpacityProperty, 0);
            Anim.Set(_sheet, OpacityProperty, 0);
            Apply();
        }
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _message.FontSize = Math.Max(15, BaseSize * 0.62);
        Anim.To(_message, OpacityProperty, 1, 280);
    }

    private void Build(LyricsDocument? doc)
    {
        _lineTimer.Stop();
        _sheet.Children.Clear();
        _blocks.Clear();
        _times.Clear();
        _active = int.MinValue;
        _manualOffset = 0;
        _kind = doc?.Kind;
        Anim.Set(_scroll, TranslateTransform.YProperty, 0);

        if (doc is null || doc.Kind is not (LyricsKind.Synced or LyricsKind.Unsynced) || doc.Lines.Count == 0)
        {
            _tops = _heights = _targetOpacity = Array.Empty<double>();
            return;
        }

        var lines = doc.Lines;
        if (doc.Kind == LyricsKind.Synced && lines[0].TimeMs > 4000)
        {
            // Long intro: show a "• • •" so the sheet doesn't look frozen before the first words.
            lines = lines.Prepend(new LyricLine(0, "")).ToList();
        }

        foreach (var line in lines)
        {
            var tb = new TextBlock
            {
                Text = line.IsBreak ? "• • •" : line.Text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = _message.FontFamily,
                FontWeight = FontWeights.SemiBold,
                Foreground = line.IsBreak ? _breakBrush : (Brush)Application.Current.FindResource("InkBrush"),
                TextAlignment = Layout == LyricsLayout.Strip ? TextAlignment.Center : TextAlignment.Left,
                RenderTransformOrigin = Layout == LyricsLayout.Strip ? new Point(0.5, 0.5) : new Point(0, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Tag = line.IsBreak,
                Opacity = doc.Kind == LyricsKind.Unsynced ? 0.8 : 0.3
            };
            TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Ideal);
            _blocks.Add(tb);
            _times.Add(line.TimeMs);
            _sheet.Children.Add(tb);
        }

        _targetOpacity = _blocks.Select(b => b.Opacity).ToArray();
        ApplyFonts();
        Remeasure(jump: true);
    }

    private void ApplyFonts()
    {
        var unsynced = _kind == LyricsKind.Unsynced;
        foreach (var tb in _blocks)
        {
            var isBreak = tb.Tag is true;
            tb.FontSize = BaseSize * (unsynced ? 0.72 : 1.0) * (isBreak ? 0.8 : 1.0);
            tb.Margin = new Thickness(0, 0, 0, Layout == LyricsLayout.Strip ? 4 : BaseSize * (unsynced ? 0.25 : 0.42));
        }
        _message.FontSize = Math.Max(15, BaseSize * 0.62);
    }

    /// <summary>Re-reads line positions after a size/font change, then re-centres.</summary>
    private void Remeasure(bool jump)
    {
        if (_blocks.Count == 0 || ActualWidth <= 0) return;
        _sheet.Width = ActualWidth;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_blocks.Count == 0) return;
            _tops = new double[_blocks.Count];
            _heights = new double[_blocks.Count];
            for (var i = 0; i < _blocks.Count; i++)
            {
                _tops[i] = _blocks[i].TranslatePoint(new Point(0, 0), _sheet).Y;
                _heights[i] = _blocks[i].ActualHeight;
            }
            Services.Log.Trace(() => $"Lyrics.Remeasure n={_blocks.Count} w={ActualWidth:0} h={ActualHeight:0} sheetH={_sheet.ActualHeight:0} top0={_tops[0]:0} h0={_heights[0]:0} sheetOpacity={_sheet.Opacity:0.00} pos={Position()}");
            if (_kind == LyricsKind.Synced)
            {
                var keep = _active;
                _active = int.MinValue;
                ApplyActive(keep == int.MinValue ? IndexAt(Position()) : keep, animate: !jump);
                Resync(animate: true);
            }
            else
            {
                ScrollUnsynced(0);
            }
        });
    }

    private void OnClockChanged(object? sender, EventArgs e) => Resync(animate: true);

    private int Position() => (_clock?.PositionMs ?? 0) + LeadMs;

    private int IndexAt(int positionMs)
    {
        // Binary search: last line whose time <= position.
        int lo = 0, hi = _times.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_times[mid] <= positionMs) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    private void Resync(bool animate)
    {
        _lineTimer.Stop();
        if (!IsVisible || _clock is null || _kind != LyricsKind.Synced || _blocks.Count == 0 || _tops.Length != _blocks.Count) return;

        var pos = Position();
        var idx = IndexAt(pos);
        if (idx != _active || _clock.IsRunning != _appliedRunning) ApplyActive(idx, animate);

        if (!_clock.IsRunning || idx + 1 >= _times.Count) return;
        var wait = _times[idx + 1] - pos;
        _lineTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(wait + 12, 12, 120_000));
        _lineTimer.Start();
    }

    private void ApplyActive(int index, bool animate)
    {
        _active = index;
        _appliedRunning = _clock?.IsRunning ?? false;
        var strip = Layout == LyricsLayout.Strip;
        var focus = Math.Max(0, index);
        var paused = _clock is { IsRunning: false };

        for (var i = 0; i < _blocks.Count; i++)
        {
            var d = i - index; // index -1 (before first line) makes everything "upcoming"
            var opacity = strip ? StripOpacity(d) : SheetOpacity(d);
            if (paused && d == 0) opacity = 0.82;
            var scale = d == 0 ? 1.0 : strip ? 0.8 : 0.95;

            if (Math.Abs(_targetOpacity[i] - opacity) > 0.001 || !animate)
            {
                _targetOpacity[i] = opacity;
                var st = (ScaleTransform)_blocks[i].RenderTransform;
                if (animate)
                {
                    Anim.To(_blocks[i], OpacityProperty, opacity, 360);
                    Anim.To(st, ScaleTransform.ScaleXProperty, scale, 420);
                    Anim.To(st, ScaleTransform.ScaleYProperty, scale, 420);
                }
                else
                {
                    Anim.Set(_blocks[i], OpacityProperty, opacity);
                    Anim.Set(st, ScaleTransform.ScaleXProperty, scale);
                    Anim.Set(st, ScaleTransform.ScaleYProperty, scale);
                }
            }
        }

        var target = ActualHeight * Anchor - (_tops[focus] + _heights[focus] / 2);
        Services.Log.Trace(() => $"Lyrics.Active {index} animate={animate} scrollTo={target:0} sheetOpacity={_sheet.Opacity:0.00}");
        if (animate) Anim.To(_scroll, TranslateTransform.YProperty, target, strip ? 430 : 560);
        else Anim.Set(_scroll, TranslateTransform.YProperty, target);
    }

    private static double SheetOpacity(int d) => d switch
    {
        0 => 1.0,
        1 => 0.5,
        2 => 0.3,
        3 => 0.18,
        -1 => 0.32,
        -2 => 0.14,
        _ => 0.08
    };

    private static double StripOpacity(int d) => d switch { 0 => 1.0, 1 => 0.55, _ => 0.0 };

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_kind != LyricsKind.Unsynced) return;
        ScrollUnsynced(_manualOffset + e.Delta * 0.6);
        e.Handled = true;
    }

    private void ScrollUnsynced(double offset)
    {
        var content = _sheet.ActualHeight;
        var min = Math.Min(0, ActualHeight * 0.85 - content);
        _manualOffset = Math.Clamp(offset, min, ActualHeight * 0.1);
        Anim.To(_scroll, TranslateTransform.YProperty, _manualOffset + ActualHeight * 0.1, 260);
    }

    private static Brush EdgeFade(double height, double top, double bottom)
    {
        var b = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, Math.Max(1, height))
        };
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        b.GradientStops.Add(new GradientStop(Colors.Black, top));
        b.GradientStops.Add(new GradientStop(Colors.Black, bottom));
        b.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        b.Freeze();
        return b;
    }
}
