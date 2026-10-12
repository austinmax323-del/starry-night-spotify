using System.IO;

namespace StarryDisplay.Services;

/// <summary>Tiny append-only log in the data folder. Never pass tokens or auth codes here.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024;

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StarryDisplay");

    public static string FilePath => Path.Combine(LogDirectory, "app.log");

    /// <summary>Verbose tracing, on when a file named <c>diag.flag</c> exists in the data folder.</summary>
    public static bool Verbose { get; } = File.Exists(Path.Combine(LogDirectory, "diag.flag"));

    public static void Trace(Func<string> message)
    {
        if (Verbose) Write("[trace] " + message());
    }

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes) File.Move(FilePath, FilePath + ".1", overwrite: true);
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
                if (ex is not null) line += $" | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
