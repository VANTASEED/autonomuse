using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Autonomuse.Services;

public static class UpdateService
{
    private const string RepoReleases = "https://api.github.com/repos/VANTASEED/autonomuse/releases/latest";
    private const string UserAgent = "Autonomuse-Updater/1.0";
    private const int BufferSize = 81920;
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "autonomuse_update.log");

    public static string CurrentAppVersion
    {
        get
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Version", out var prop))
                    {
                        return prop.GetString() ?? "Unknown";
                    }
                }
                return "1.0.0";
            }
            catch
            {
                return "1.0.0";
            }
        }
    }

    /// <summary>Returns the new version string if an update is available, otherwise null. Throws on error.</summary>
    public static async Task<string?> GetAvailableUpdateAsync()
    {
        var ctx = await CheckVersionAsync();
        if (ctx == null) return null;

        var dismissedVersion = await GetSettingAsync("UpdateDismissedVersion");
        if (ctx.TagName == dismissedVersion) { Log("Skipped — version was dismissed"); return null; }

        return ctx.TagName.TrimStart('v', 'V');
    }

    public static async Task CheckForUpdateAsync(bool isManual = false)
    {
        try
        {
            var ctx = await CheckVersionAsync();
            var page = Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null;

            if (ctx == null)
            {
                if (isManual && page != null)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        page.DisplayAlertAsync("Up to Date", "Autonomuse is already up to date.", "OK"));
                }
                return;
            }

            if (!isManual)
            {
                var dismissedVersion = await GetSettingAsync("UpdateDismissedVersion");
                if (ctx.TagName == dismissedVersion) { Log("Skipped — version was dismissed"); return; }
            }

            if (page == null)
            {
                Log("No active page found to display update prompt");
                return;
            }

            var proceed = await MainThread.InvokeOnMainThreadAsync(() =>
                page.DisplayAlertAsync("Update Available",
                    $"Autonomuse {ctx.TagName.TrimStart('v', 'V')} is available.\nDownload and install now?",
                    "Install Now", "Skip this Version"));
            if (!proceed)
            {
                await SetSettingAsync("UpdateDismissedVersion", ctx.TagName);
                Log("User declined update — dismissed");
                return;
            }

            var progressPage = new UpdateProgressPage();
            await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PushModalAsync(progressPage));

            var tempPath = Path.Combine(Path.GetTempPath(), "Autonomuse_Setup.exe");
            var success = await DownloadWithProgressAsync(ctx.DownloadUrl, tempPath, progressPage);

            await MainThread.InvokeOnMainThreadAsync(() => page.Navigation.PopModalAsync());

            if (!success)
            {
                Log("Download cancelled or failed");
                if (isManual)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        page.DisplayAlertAsync("Update Cancelled", "The update download was cancelled or failed.", "OK"));
                }
                return;
            }

            Log("Launching installer...");
            Process.Start(new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "/SILENT /NORESTART",
                UseShellExecute = true
            });

            Log("Exiting for update");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log($"Error: {ex}");
            if (isManual)
            {
                try
                {
                    var page = Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0].Page : null;
                    if (page != null)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                            page.DisplayAlertAsync("Update Error", $"Failed to check/install update: {ex.Message}", "OK"));
                    }
                }
                catch { }
            }
        }
    }

    private record UpdateContext(string TagName, string DownloadUrl);

    private static async Task<UpdateContext?> CheckVersionAsync()
    {
        Log("--- Checking for updates ---");
        var currentVersion = GetCurrentVersion();
        if (currentVersion == null) { Log("No local version found"); return null; }
        Log($"Local version: {currentVersion}");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.Timeout = TimeSpan.FromMinutes(5);

        var response = await client.GetAsync(RepoReleases);
        if (!response.IsSuccessStatusCode) { Log($"GitHub API returned {response.StatusCode}"); return null; }
        Log("GitHub API OK");

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString();
        if (string.IsNullOrEmpty(tagName)) { Log("No tag_name"); return null; }
        Log($"GitHub tag: {tagName}");

        var latestVersion = ParseVersion(tagName);
        if (latestVersion == null) { Log($"Could not parse '{tagName}'"); return null; }
        Log($"GitHub version: {latestVersion}");

        if (latestVersion <= currentVersion) { Log($"Up to date ({currentVersion} >= {latestVersion})"); return null; }
        Log($"Update: {currentVersion} → {latestVersion}");

        var downloadUrl = root.GetProperty("assets").EnumerateArray()
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault(u => u != null && u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (downloadUrl == null) { Log("No .exe asset"); return null; }

        Log($"Download URL: {downloadUrl}");
        return new UpdateContext(tagName, downloadUrl);
    }

    private static async Task<bool> DownloadWithProgressAsync(string url, string destPath, UpdateProgressPage progressPage)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, progressPage.Token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            Log($"Download size: {FormatSize(totalBytes)}");

            await using var contentStream = await response.Content.ReadAsStreamAsync(progressPage.Token);
            await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true);
            var buffer = new byte[BufferSize];
            var totalRead = 0L;

            while (true)
            {
                var read = await contentStream.ReadAsync(buffer, progressPage.Token);
                if (read == 0) break;
                await fileStream.WriteAsync(buffer.AsMemory(0, read), progressPage.Token);
                totalRead += read;

                if (totalBytes > 0)
                {
                    var fraction = (double)totalRead / totalBytes;
                    progressPage.SetProgress(fraction, $"{FormatSize(totalRead)} / {FormatSize(totalBytes)} ({fraction * 100:F0}%)");
                }
                else
                {
                    progressPage.SetProgress(0, $"{FormatSize(totalRead)} downloaded");
                }
            }

            Log($"Downloaded {FormatSize(totalRead)}");
            return true;
        }
        catch (OperationCanceledException)
        {
            Log("Download cancelled by user");
            return false;
        }
        catch (Exception ex)
        {
            Log($"Download failed: {ex}");
            return false;
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    private static Version GetCurrentVersion()
    {
        try
        {
            var raw = CurrentAppVersion;
            var parsed = ParseVersion(raw);
            if (parsed != null) return parsed;
            return new Version(1, 0, 0);
        }
        catch (Exception ex)
        {
            Log($"GetCurrentVersion failed: {ex}");
            return new Version(1, 0, 0);
        }
    }

    private static Version? ParseVersion(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var cleaned = s.TrimStart('v', 'V');
        var numPart = cleaned.Split('-', '+')[0];
        var segments = numPart.Split('.');
        var nums = new List<int>();
        foreach (var seg in segments)
            if (int.TryParse(seg, out var n)) nums.Add(n);
            else { Log($"ParseVersion: non-numeric '{seg}' in '{s}'"); return null; }
        while (nums.Count < 3) nums.Add(0);
        nums[1] = Math.Min(nums[1], 65535);
        nums[2] = Math.Min(nums[2], 65535);
        return new Version(nums[0], nums[1], nums[2]);
    }

    private static string SettingsDbPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Autonomuse", "com.vantaseed.autonomuse", "Data", "setting.db");

    private static async Task<string?> GetSettingAsync(string key)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={SettingsDbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT [Values] FROM Settings WHERE [Parameter] = @key";
            cmd.Parameters.AddWithValue("@key", key);
            return await cmd.ExecuteScalarAsync() as string;
        }
        catch { return null; }
    }

    private static async Task SetSettingAsync(string key, string value)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={SettingsDbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Settings ([Parameter], [Values]) VALUES (@key, @val) ON CONFLICT([Parameter]) DO UPDATE SET [Values] = @val";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@val", value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    private static void Log(string message)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}"); }
        catch { }
    }
}
