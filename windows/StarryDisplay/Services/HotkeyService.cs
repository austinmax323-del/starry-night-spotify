using System.Runtime.InteropServices;
using System.Windows.Interop;
using StarryDisplay.Core;

namespace StarryDisplay.Services;

/// <summary>
/// Global hotkeys via RegisterHotKey, owned by a message-only window that lives as long as the app, so
/// display windows can be swapped without handing hotkeys over.
///
/// Pattern adapted (not copied) from NHotkey.Wpf (thomaslevesque/NHotkey, Apache-2.0: an HwndSource parented
/// to HWND_MESSAGE as the "hotkey sink") and EarTrumpet (File-New-Project/EarTrumpet, MIT: suspend while the
/// user records a new combination). RegisterHotKey is the documented API for this; unlike a low-level keyboard
/// hook it sees nothing but our own combinations, so no game input passes through this process.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int BaseId = 0x5140;
    private readonly HwndSource _sink;
    private readonly Dictionary<int, string> _actions = new();
    private HotkeySettings _settings = new();
    private bool _suspended;

    /// <summary>Per action: "on", "off", "in use", or "invalid". Shown in Settings.</summary>
    public IReadOnlyDictionary<string, string> Status => _status;
    private readonly Dictionary<string, string> _status = new();

    /// <summary>An action from <see cref="HotkeyActions"/>.</summary>
    public event Action<string>? Pressed;
    public event Action? StatusChanged;

    public HotkeyService()
    {
        _sink = new HwndSource(new HwndSourceParameters("StarryDisplay hotkeys")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: never visible, never in z-order
            WindowStyle = 0,
            HwndSourceHook = WndProc
        });
    }

    public void Apply(HotkeySettings settings)
    {
        _settings = settings;
        if (!_suspended) Register();
    }

    /// <summary>Releases every hotkey (while Settings records a new combination, so it can be pressed there).</summary>
    public void Suspend()
    {
        _suspended = true;
        UnregisterAll();
    }

    public void Resume()
    {
        _suspended = false;
        Register();
    }

    private void Register()
    {
        UnregisterAll();
        _status.Clear();
        var id = BaseId;
        foreach (var (action, text) in _settings.All())
        {
            id++;
            if (!_settings.Enabled || string.IsNullOrEmpty(text)) { _status[action] = "off"; continue; }
            if (HotkeyGesture.Parse(text) is not { } gesture) { _status[action] = "invalid"; continue; }

            if (NativeMethods.RegisterHotKey(_sink.Handle, id, (uint)gesture.Modifiers | NativeMethods.ModNoRepeat, gesture.VirtualKey))
            {
                _actions[id] = action;
                _status[action] = "on";
            }
            else
            {
                // Another app owns it (ERROR_HOTKEY_ALREADY_REGISTERED). Not fatal: tray/Settings/Mac still work.
                _status[action] = "in use";
                Log.Write($"Hotkey {gesture} ({action}) is taken by another app (error {Marshal.GetLastWin32Error()}).");
            }
        }
        StatusChanged?.Invoke();
    }

    private void UnregisterAll()
    {
        foreach (var id in _actions.Keys) NativeMethods.UnregisterHotKey(_sink.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotKey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _sink.Dispose();
    }
}
