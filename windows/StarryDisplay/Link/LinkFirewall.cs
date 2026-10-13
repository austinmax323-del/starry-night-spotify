using System.Diagnostics;
using StarryDisplay.Services;

namespace StarryDisplay.Link;

/// <summary>
/// One narrowly-scoped inbound Windows Firewall rule for the link: this exe only, this TCP port only,
/// and only from the local subnet or Tailscale (100.64.0.0/10). Needed because Windows treats many
/// home Wi-Fi networks as "Public" and blocks inbound by default. Created on request (one UAC prompt).
/// </summary>
public static class LinkFirewall
{
    public const string RuleName = "Starry Display (Mac link)";

    /// <summary>
    /// True only if the rule exists <b>for this exe</b>. Another copy of the app (a dev build, a moved install) is
    /// not covered by it, and listening on the LAN from that copy would make Windows show its broad
    /// "allow this app?" prompt, so such a copy stays loopback-only until its own rule is created.
    /// </summary>
    public static bool RuleExists()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{RuleName}\" verbose")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0 && CoversProgram(output, Environment.ProcessPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether netsh's verbose rule listing has a field whose value is exactly <paramref name="exe"/> ("Program: C:\…\StarryDisplay.exe").
    /// Matches on the value, not the field name, because netsh localizes field names.
    /// </summary>
    internal static bool CoversProgram(string netshVerboseOutput, string? exe)
    {
        if (string.IsNullOrEmpty(exe)) return false;
        foreach (var line in netshVerboseOutput.Split('\n'))
        {
            var i = line.IndexOf(':'); // the field separator comes before the drive letter's colon
            if (i > 0 && string.Equals(line[(i + 1)..].Trim(), exe, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Replaces the rule (elevated). Returns false if the user declined the UAC prompt.</summary>
    public static async Task<bool> CreateRuleAsync(int port)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        var add = $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port} " +
                  $"remoteip=localsubnet,100.64.0.0/10 program=\"{exe}\" profile=any " +
                  "description=\"Lets a paired Mac control Starry Display on the local network. Created by Starry Display.\"";
        // Also removes any broad allow/block rules Windows' own "allow this app?" prompt created for
        // this exe (a block rule would override ours), then adds the one narrow rule.
        var cmd = $"/c netsh advfirewall firewall delete rule name=all program=\"{exe}\" >nul 2>&1 & " +
                  $"netsh advfirewall firewall delete rule name=\"{RuleName}\" >nul 2>&1 & {add}";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", cmd) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            if (p is null) return false;
            await p.WaitForExitAsync();
            Log.Write($"[link] firewall rule {(p.ExitCode == 0 ? "created" : $"failed (exit {p.ExitCode})")} for port {port}");
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Log.Write("[link] firewall rule: UAC prompt declined");
            return false;
        }
    }
}
