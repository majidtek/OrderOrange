using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Windows;
using LocalHandler.Models;

namespace LocalHandler.Services;

/// <summary>
/// Keeps the till current without anyone touching the PC. Asks the server for the latest
/// build (<c>api/till/version</c>); when it is newer than this one, downloads the zip,
/// checks its hash, unpacks it beside the app and hands over to a tiny batch file that
/// waits for this process to exit, copies the new files over the old and starts the till
/// again with the same profile. Printed-order memory lives in the profile file, so the
/// restart reprints nothing.
/// </summary>
public static class UpdateService
{
    public sealed record Manifest(string Version, string File, string? Sha256, string? Notes);

    public static string Current =>
        typeof(UpdateService).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        // Same rule as ApiService: an IP-addressed (test) server may present a mismatched certificate.
        ServerCertificateCustomValidationCallback = (req, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None
            || (req.RequestUri is { } u && System.Net.IPAddress.TryParse(u.Host, out _)),
    }) { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Checks once. Returns true when an update was staged and the app is restarting —
    /// the caller should stop doing anything else. False = nothing to do (or the server
    /// could not be reached; the till just carries on and asks again later).
    /// </summary>
    public static async Task<bool> TryUpdateAsync(Setup setup, string profile, Action<string> status)
    {
        try
        {
            var root = new Uri(setup.ApiBaseUrl.TrimEnd('/') + "/");
            var manifest = await Http.GetFromJsonAsync<Manifest>(new Uri(root, "api/till/version"),
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (manifest is null || !System.Version.TryParse(manifest.Version, out var latest)) return false;
            if (latest <= System.Version.Parse(Current)) return false;

            status($"Updating to v{manifest.Version}…");
            TillLog.Write($"update: v{Current} → v{manifest.Version}");
            var work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OrderOrange", "update");
            Directory.CreateDirectory(work);
            var zip = Path.Combine(work, $"LocalHandler-{manifest.Version}.zip");
            await using (var net = await Http.GetStreamAsync(new Uri(root, "api/till/download")))
            await using (var file = File.Create(zip))
                await net.CopyToAsync(file);

            if (!string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                await using var check = File.OpenRead(zip);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(check));
                if (!hash.Equals(manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(zip);
                    status("Update download was corrupt — will retry later.");
                    return false;
                }
            }

            var staged = Path.Combine(work, manifest.Version);
            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            ZipFile.ExtractToDirectory(zip, staged);
            if (!File.Exists(Path.Combine(staged, "LocalHandler.exe")))
            {
                status("Update package is incomplete — skipped.");
                return false;
            }

            var appDir = AppContext.BaseDirectory.TrimEnd('\\');
            var exe = Environment.ProcessPath ?? Path.Combine(appDir, "LocalHandler.exe");
            var args = profile.Length > 0 ? $"--profile=\"{profile}\"" : "";
            var script = Path.Combine(work, "apply-update.cmd");
            File.WriteAllText(script, $"""
                @echo off
                :wait
                tasklist /FI "PID eq {Environment.ProcessId}" 2>nul | find "{Environment.ProcessId}" >nul
                if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)
                robocopy "{staged}" "{appDir}" /E /R:10 /W:1 >nul
                start "" "{exe}" {args}
                del "%~f0"
                """);
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work,
            });
            status("Restarting for the update…");
            await Task.Delay(500);
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"update check failed: {ex.Message}");
            return false;
        }
    }
}
