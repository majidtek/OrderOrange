using System.Security.Cryptography;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using OrderOrange.Shared;

namespace OrderOrange.AndroidTill.Services;

/// <summary>
/// Self-update, like the Windows till: the server's <c>api/till/android/version</c> names
/// the current release; when it is newer than this build the home screen offers it, the
/// APK is downloaded, its SHA-256 checked, and Android's installer is opened on it (the
/// cashier confirms the install; the first time Android asks to allow installs from this
/// app). Majed 2026-10-18: "in android app add update".
/// </summary>
public static class UpdateService
{
    /// <summary>The release waiting to be installed, or null when this build is current.</summary>
    public static TillVersionDto? Available { get; private set; }
    public static DateTime LastCheck { get; private set; }

    public static string Installed
    {
        get { try { return AppInfo.Current.VersionString; } catch { return "0.0.0"; } }
    }

    /// <summary>Asks the server; remembers a newer release in <see cref="Available"/>.</summary>
    public static async Task<TillVersionDto?> CheckAsync(ApiService api)
    {
        var dto = await api.AndroidVersionAsync();
        LastCheck = DateTime.Now;
        if (dto is null) return Available;
        Available = IsNewer(dto.Version, Installed) ? dto : null;
        if (Available is not null) TillLog.Write($"update available: {dto.Version} (installed {Installed})");
        return Available;
    }

    public static bool IsNewer(string server, string installed) =>
        Version.TryParse(Normalise(server), out var s) && Version.TryParse(Normalise(installed), out var i) && s > i;

    /// <summary>"1.4" → "1.4.0", "1.4.0 (8)" → "1.4.0", so Version.Parse never trips.</summary>
    private static string Normalise(string v)
    {
        v = v.Trim();
        var cut = v.IndexOfAny([' ', '(', '-', '+']);
        if (cut > 0) v = v[..cut];
        return v.Count(c => c == '.') switch { 0 => v + ".0.0", 1 => v + ".0", _ => v };
    }

    /// <summary>
    /// Downloads the release into the app cache, verifies it, and hands it to Android's
    /// package installer. Null on success (the installer is now on screen), else the reason.
    /// </summary>
    public static async Task<string?> DownloadAndInstallAsync(ApiService api, TillVersionDto release, IProgress<int>? progress, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.Combine(FileSystem.CacheDirectory, "update");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*.apk")) { try { File.Delete(old); } catch { } }
            var path = Path.Combine(dir, $"OrderOrangeTill-{release.Version}.apk");

            var err = await api.DownloadAndroidAsync(path, progress, ct);
            if (err is not null) return err;

            if (!string.IsNullOrWhiteSpace(release.Sha256))
            {
                await using var fs = File.OpenRead(path);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                if (!hash.Equals(release.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(path); } catch { }
                    return "The downloaded file is damaged (checksum mismatch). Try again.";
                }
            }

            TillLog.Write($"update {release.Version} downloaded ({new FileInfo(path).Length / 1024} KB) — opening the installer");
            // Launcher shares the file through the app's FileProvider and opens the installer on it.
            var opened = await Launcher.Default.OpenAsync(new OpenFileRequest("OrderOrange Till", new ReadOnlyFile(path, "application/vnd.android.package-archive")));
            return opened ? null : "Android did not open the installer. Allow installs from this app in Settings → Apps → OrderOrange Till → Install unknown apps.";
        }
        catch (Exception ex)
        {
            TillLog.Write($"update failed: {ex.Message}");
            return ex.Message;
        }
    }
}
