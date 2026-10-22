using System.Security.Cryptography;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using OrderOrange.PartnerApp.Till.Services;
using OrderOrange.Shared;

namespace OrderOrange.PartnerApp.Services;

/// <summary>
/// Keeps the app up to date from inside the app (Majed 2026-10-22: "in mobile app also add
/// update and add update notification"). Every few hours it asks the server which version is
/// on offer (api/till/partner/{platform}/version); a newer one shows a bar at the foot of every
/// page and, on Android, a notification. "Update now" downloads the APK into the app's own
/// cache, checks its SHA-256 against the server's, and hands it to Android's installer — the
/// same signing key, so it installs over the running app. Windows has no installer: the bar
/// opens the download in the browser instead.
/// </summary>
public sealed class AppUpdateService : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(AppConfig.ApiBaseUrl), Timeout = TimeSpan.FromMinutes(15) };
    private Timer? _timer;

    /// <summary>The newer version the server offers, or null when this app is current.</summary>
    public TillVersionDto? Available { get; private set; }

    /// <summary>"Later" hides the bar until a newer version than this one appears.</summary>
    public bool Dismissed { get; set; }

    /// <summary>-1 idle, 0..100 downloading, 101 handing over to the installer.</summary>
    public int Progress { get; private set; } = -1;

    public string? Error { get; private set; }

    public event Action? Changed;

    public static string Installed => AppInfo.Current.VersionString;
    public static string Platform => OperatingSystem.IsAndroid() ? "android" : "windows";
    public static bool CanInstall => OperatingSystem.IsAndroid();
    public static string DownloadUrl => $"{AppConfig.ApiBaseUrl}api/till/partner/{Platform}/download";

    /// <summary>First look a few seconds after start-up, then every four hours while the app runs.</summary>
    public void Start() => _timer ??= new Timer(_ => _ = CheckAsync(), null, TimeSpan.FromSeconds(8), TimeSpan.FromHours(4));

    public async Task CheckAsync()
    {
        try
        {
            var offer = await _http.GetFromJsonAsync<TillVersionDto>($"api/till/partner/{Platform}/version");
            if (offer is null || !Version.TryParse(offer.Version, out var remote) || !Version.TryParse(Installed, out var mine)) return;
            if (remote <= mine)
            {
                if (Available is not null) { Available = null; Changed?.Invoke(); }
                return;
            }
            var isNew = Available?.Version != offer.Version;
            Available = offer;
            if (isNew) Dismissed = false;
            Changed?.Invoke();
#if ANDROID
            if (isNew) UpdateNotifier.Show(offer.Version, offer.Notes);
#endif
        }
        catch (Exception ex)
        {
            TillLog.Write($"update check: {ex.Message}");
        }
    }

    /// <summary>Android: download, verify, and open the installer. Windows: open the download page.</summary>
    public async Task UpdateAsync()
    {
        if (Available is not { } offer || Progress >= 0) return;
        if (!CanInstall)
        {
            try { await Launcher.Default.OpenAsync(new Uri(DownloadUrl)); } catch (Exception ex) { Error = ex.Message; Changed?.Invoke(); }
            return;
        }

        Error = null;
        Progress = 0;
        Changed?.Invoke();
        try
        {
            var dir = FileSystem.CacheDirectory;
            foreach (var old in Directory.GetFiles(dir, "OrderOrangePartner-*.apk"))
                try { File.Delete(old); } catch { }
            var path = Path.Combine(dir, $"OrderOrangePartner-{offer.Version}.apk");

            using (var res = await _http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength ?? 0;
                await using var src = await res.Content.ReadAsStreamAsync();
                await using var dst = File.Create(path);
                var buf = new byte[81920];
                long done = 0;
                int n, shown = -1;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    var pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != shown) { shown = pct; Progress = pct; Changed?.Invoke(); }
                }
            }

            if (offer.Sha256 is { Length: > 0 } want)
            {
                string got;
                await using (var f = File.OpenRead(path)) got = Convert.ToHexString(await SHA256.HashDataAsync(f));
                if (!string.Equals(got, want, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The downloaded file is damaged. Try again.");
            }

            Progress = 101;
            Changed?.Invoke();
            // Android's package installer takes it from here (REQUEST_INSTALL_PACKAGES; the first
            // time it asks to allow installs from this app).
            await Launcher.Default.OpenAsync(new OpenFileRequest($"OrderOrange Partner {offer.Version}",
                new ReadOnlyFile(path, "application/vnd.android.package-archive")));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            TillLog.Write($"update install: {ex.Message}");
        }
        finally
        {
            Progress = -1;
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _http.Dispose();
    }
}
