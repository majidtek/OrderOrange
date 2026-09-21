using System.IO;

namespace LocalHandler.Services;

/// <summary>
/// One line per thing the till did — every ticket, every fallback, every update — in
/// %LOCALAPPDATA%\OrderOrange\till.log. Small (rolls at 2 MB), plain text, so "why did
/// the kitchen get two tickets?" can be answered from the file instead of guessed.
/// </summary>
public static class TillLog
{
    public static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrderOrange", "till.log");

    private static readonly object Gate = new();

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > 2_000_000)
                    File.Move(Path, Path + ".old", overwrite: true);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
            }
        }
        catch { /* logging must never stop a print */ }
    }
}
