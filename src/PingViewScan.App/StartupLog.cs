using System.IO;
using System.Text;

namespace PingViewScan.App;

/// <summary>
/// Minimal startup/crash log. WPF apps are windowed executables, so a failure
/// before the first window appears is otherwise invisible (no console, no
/// message). Everything is written to
/// <c>%LOCALAPPDATA%\PingViewScan\logs\startup.log</c> so a broken start can be
/// diagnosed from the machine that failed.
/// </summary>
internal static class StartupLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PingViewScan",
        "logs",
        "startup.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var info = new FileInfo(Path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(Path, Path + ".old", overwrite: true);
                }

                var text = new StringBuilder()
                    .Append('[')
                    .Append(DateTimeOffset.Now.ToString("O"))
                    .Append("] ")
                    .Append(message);

                if (exception is not null)
                {
                    text.AppendLine().Append(exception);
                }

                File.AppendAllText(Path, text.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never be the reason the app fails.
        }
    }
}
