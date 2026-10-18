using Microsoft.Maui.Storage;

namespace OrderOrange.AndroidTill.Services;

/// <summary>A plain text diary of what the till printed and what went wrong, in the app's data folder.</summary>
public static class TillLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024;

    public static string Path => System.IO.Path.Combine(FileSystem.AppDataDirectory, "till.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(Path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    // Keep the newest half rather than growing without end.
                    var text = File.ReadAllText(Path);
                    File.WriteAllText(Path, text[(text.Length / 2)..]);
                }
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
        }
        catch { /* logging must never break printing */ }
    }

    public static string Tail(int maxChars = 20_000)
    {
        try
        {
            if (!File.Exists(Path)) return "";
            var text = File.ReadAllText(Path);
            return text.Length <= maxChars ? text : text[^maxChars..];
        }
        catch { return ""; }
    }
}
