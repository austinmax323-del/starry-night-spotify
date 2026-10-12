using Microsoft.Win32;

namespace StarryDisplay.Services;

/// <summary>Per-user "start with Windows" via HKCU\…\Run. No service, no scheduled task, no admin.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StarryDisplay";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe) key.SetValue(ValueName, $"\"{exe}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Write("Could not update start-with-Windows.", ex);
        }
    }
}
