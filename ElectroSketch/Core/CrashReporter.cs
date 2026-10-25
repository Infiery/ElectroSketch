using System.Text;
using System.IO;

namespace ElectroSketch.Core;

public static class CrashReporter
{
    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ElectroSketch",
        "crash.log");

    public static void Log(Exception exception, string context)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var entry = new StringBuilder()
                .AppendLine(new string('=', 72))
                .AppendLine($"{DateTimeOffset.Now:O} · {context}")
                .AppendLine(exception.ToString())
                .ToString();
            File.AppendAllText(LogPath, entry);
        }
        catch
        {
            // Diagnostics must never cause a second application failure.
        }
    }
}
