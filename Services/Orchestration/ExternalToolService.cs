using System.Diagnostics;
using Autonomuse.Shared.Contracts;
using Microsoft.Maui.Networking;

namespace Autonomuse.Services.Orchestration
{
    public class ExternalToolService : IExternalToolService
    {
        private readonly ISettingsService _settingsService;
        private readonly string _toolsFolder;

        public ExternalToolService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            // We still maintain the Tools folder for local binary fallbacks
            _toolsFolder = Path.Combine(FileSystem.AppDataDirectory, "Tools");
            if (!Directory.Exists(_toolsFolder)) Directory.CreateDirectory(_toolsFolder);
        }

        private void RefreshEnvironmentPath()
        {
            try
            {
                // Pull fresh PATH from User and Machine targets to bypass process staleness
                var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
                var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
                
                var currentPath = Environment.GetEnvironmentVariable("Path") ?? "";
                
                // Merge paths ensuring no duplicates
                var paths = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                var newPaths = (userPath + ";" + machinePath).Split(';', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var p in newPaths)
                {
                    if (!paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    {
                        paths.Add(p);
                    }
                }

                // Add common WinGet shim location explicitly just in case
                var winGetShimPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps");
                if (!paths.Contains(winGetShimPath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(winGetShimPath);
                }

                Environment.SetEnvironmentVariable("PATH", string.Join(";", paths));
            }
            catch
            {
                // If this fails, we just continue with what we have
            }
        }

        public string GetToolPath(string toolName)
        {
            if (toolName.Equals("yt-dlp", StringComparison.OrdinalIgnoreCase))
            {
                // 1. Check local AppData first (manual install/fallback)
                var localPath = Path.Combine(_toolsFolder, "yt-dlp.exe");
                if (File.Exists(localPath)) return localPath;

                // 2. Check common WinGet location explicitly (bypass PATH check if it fails)
                var winGetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\yt-dlp.exe");
                if (File.Exists(winGetPath)) return winGetPath;
            }
            else if (toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase))
            {
                // 1. WinGet shim location
                var winGetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\fpcalc.exe");
                if (File.Exists(winGetPath)) return winGetPath;

                // 2. Common Program Files location (Chromaprint often installs here)
                var progFilesPath = @"C:\Program Files\Chromaprint\bin\fpcalc.exe";
                if (File.Exists(progFilesPath)) return progFilesPath;

                var progFilesX86Path = @"C:\Program Files (x86)\Chromaprint\bin\fpcalc.exe";
                if (File.Exists(progFilesX86Path)) return progFilesX86Path;
            }
            return toolName; // Assume it's in PATH
        }

        public async Task<bool> IsToolInstalledAsync(string toolName)
        {
            // Fresh update of environment variables before every check
            RefreshEnvironmentPath();

            var path = GetToolPath(toolName);
            
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = toolName is "fpcalc" or "ffmpeg" ? "-version" : "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return false;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 || toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract version and store it
                    string version = ExtractVersion(toolName, output);
                    if (!string.IsNullOrEmpty(version))
                    {
                        await _settingsService.SaveToolVersionAsync(toolName, version);
                        
                        // If checking fpcalc, also check ffmpeg (per user request)
                        if (toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase))
                        {
                            await CheckFFmpegVersionAsync();
                        }
                    }
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task CheckFFmpegVersionAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    string version = ExtractVersion("ffmpeg", output);
                    if (!string.IsNullOrEmpty(version))
                    {
                        await _settingsService.SaveToolVersionAsync("ffmpeg", version);
                    }
                }
            }
            catch { }
        }

        private string ExtractVersion(string toolName, string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            
            // Typical version strings:
            // fpcalc version 1.5.1
            // ffmpeg version 6.0-essentials_build-www.gyan.dev Copyright (c) 2000-2023 the FFmpeg developers
            // yt-dlp 2023.03.04 [github.com/yt-dlp/yt-dlp]
            
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return "";
            
            var firstLine = lines[0];
            if (toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstLine, @"version\s+([\d\.]+)");
                return match.Success ? match.Groups[1].Value : firstLine.Trim();
            }
            else if (toolName.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase))
            {
                var match = System.Text.RegularExpressions.Regex.Match(firstLine, @"version\s+([^\s]+)");
                return match.Success ? match.Groups[1].Value : firstLine.Trim();
            }
            else
            {
                return firstLine.Trim();
            }
        }

        public async Task InstallToolAsync(string toolName)
        {
            if (toolName.Equals("yt-dlp", StringComparison.OrdinalIgnoreCase) || toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase))
            {
                string packageId = toolName.Equals("yt-dlp", StringComparison.OrdinalIgnoreCase) ? "yt-dlp" : "chromaprint";
                int maxRetries = 3;
                int currentRetry = 0;
                Exception? lastException = null;

                while (currentRetry < maxRetries)
                {
                    try
                    {
                        // If it's fpcalc, the user wants both ffmpeg and chromaprint
                        string args = toolName.Equals("fpcalc", StringComparison.OrdinalIgnoreCase) 
                            ? "install ffmpeg --silent --accept-source-agreements --accept-package-agreements && winget install chromaprint --silent --accept-source-agreements --accept-package-agreements"
                            : $"install {packageId} --silent --accept-source-agreements --accept-package-agreements";

                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c winget {args}",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = Process.Start(startInfo);
                        if (process == null) throw new InvalidOperationException("Failed to start winget process.");

                        await process.WaitForExitAsync();
                        
                        // 0x8a15003f (-1978335189) is WINGET_CLIF_COMMAND_COMMAND_NOT_APPLICABLE (already up-to-date)
                        if (process.ExitCode == 0 || process.ExitCode == -1978335189) return; // Success
 
                        lastException = new Exception($"winget installation of {packageId} failed with exit code {process.ExitCode}");
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                    }

                    currentRetry++;
                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(2000); 
                    }
                }

                if (lastException != null) throw lastException;
            }
        }

        public bool HasInternetConnection()
        {
            return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        }

        public string GetToolStatus(string toolName)
        {
            var path = GetToolPath(toolName);
            return File.Exists(path) ? "Ready" : "Managed by System";
        }

        public async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCommandAsync(string toolName, string arguments)
        {
            RefreshEnvironmentPath();
            var path = GetToolPath(toolName);

            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return (-1, "", "Failed to start process.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return (process.ExitCode, await stdoutTask, await stderrTask);
        }

        public async Task<System.Collections.Generic.HashSet<string>> CheckOutdatedToolsAsync()
        {
            var outdated = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!HasInternetConnection()) return outdated;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c winget upgrade",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return outdated;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    // Split by 2 or more spaces to parse table columns
                    var parts = System.Text.RegularExpressions.Regex.Split(line, @"\s{2,}");
                    if (parts.Length >= 4)
                    {
                        var id = parts[1].Trim();
                        if (id.Equals("yt-dlp", StringComparison.OrdinalIgnoreCase))
                        {
                            outdated.Add("yt-dlp");
                        }
                        else if (id.Equals("chromaprint", StringComparison.OrdinalIgnoreCase) || id.Contains("chromaprint.chromaprint", StringComparison.OrdinalIgnoreCase))
                        {
                            outdated.Add("fpcalc");
                        }
                        else if (id.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) || id.Equals("yt-dlp.FFmpeg", StringComparison.OrdinalIgnoreCase))
                        {
                            outdated.Add("ffmpeg");
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            return outdated;
        }
    }
}
