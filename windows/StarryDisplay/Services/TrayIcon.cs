using System.Drawing;
using System.Windows.Forms;

namespace StarryDisplay.Services;

/// <summary>Tray icon + menu. Raises the same command strings as the window toolbar.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _show, _stage, _strip, _topmost, _lock, _pause;

    public event Action<string>? CommandRequested;

    public TrayIcon()
    {
        var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new NightColors()), ShowImageMargin = false, ShowCheckMargin = true };
        menu.ForeColor = Color.FromArgb(235, 238, 248);

        _show = Item("Hide display", "toggle");
        var layout = new ToolStripMenuItem("Layout") { ForeColor = menu.ForeColor };
        _stage = Item("Stage card", "layout:stage");
        _strip = Item("Lyrics strip", "layout:strip");
        layout.DropDownItems.AddRange(new ToolStripItem[] { _stage, _strip });
        layout.DropDown.BackColor = NightColors.Back;
        if (layout.DropDown is ToolStripDropDownMenu dd) { dd.ShowImageMargin = false; dd.ShowCheckMargin = true; }
        _topmost = Item("Always on top", "topmost");
        _lock = Item("Click-through   Ctrl+Alt+L", "lock");
        _pause = Item("Pause updates", "pause");

        menu.Items.AddRange(new ToolStripItem[]
        {
            _show, layout, new ToolStripSeparator(), _topmost, _lock, _pause, new ToolStripSeparator(),
            Item("Settings…", "settings"), Item("Quit Starry Display", "quit")
        });

        _icon = new NotifyIcon
        {
            Text = "Starry Display",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) CommandRequested?.Invoke("tray-click");
        };
    }

    public void Update(bool windowVisible, string layout, bool topmost, bool locked, bool paused)
    {
        _show.Text = windowVisible ? "Hide display" : "Show display";
        _stage.Checked = layout == Core.Layouts.Stage;
        _strip.Checked = layout == Core.Layouts.Strip;
        _topmost.Checked = topmost;
        _lock.Checked = locked;
        _pause.Checked = paused;
    }

    /// <summary>Tooltip, e.g. "Song — Artist". Windows caps tray tooltips at 127 chars.</summary>
    public void SetTooltip(string text)
    {
        text = string.IsNullOrWhiteSpace(text) ? "Starry Display" : text;
        _icon.Text = text.Length > 120 ? text[..119] + "…" : text;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private ToolStripMenuItem Item(string text, string command)
    {
        var item = new ToolStripMenuItem(text) { ForeColor = Color.FromArgb(235, 238, 248) };
        item.Click += (_, _) => CommandRequested?.Invoke(command);
        return item;
    }

    private static Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && Icon.ExtractAssociatedIcon(exe) is { } icon) return icon;
        }
        catch (Exception ex)
        {
            Log.Write("Tray icon fallback.", ex);
        }
        return SystemIcons.Application;
    }

    /// <summary>Dark menu colours so the tray menu matches the display.</summary>
    private sealed class NightColors : ProfessionalColorTable
    {
        public static readonly Color Back = Color.FromArgb(22, 25, 36);
        private static readonly Color Hover = Color.FromArgb(44, 49, 68);
        private static readonly Color Line = Color.FromArgb(52, 57, 76);
        public override Color ToolStripDropDownBackground => Back;
        public override Color MenuBorder => Line;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}
