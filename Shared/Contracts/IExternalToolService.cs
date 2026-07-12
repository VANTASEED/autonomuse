namespace Autonomuse.Shared.Contracts
{
    public interface IExternalToolService
    {
        /// <summary>
        /// Checks if a tool is installed by attempting to run it with --version
        /// </summary>
        Task<bool> IsToolInstalledAsync(string toolName);

        /// <summary>
        /// Gets the absolute path to a tool. If not installed in AppData, returns just the command name for PATH usage.
        /// </summary>
        string GetToolPath(string toolName);

        /// <summary>
        /// Installs a tool using the system package manager (winget).
        /// </summary>
        Task InstallToolAsync(string toolName);

        /// <summary>
        /// Checks if the device has an active internet connection.
        /// </summary>
        bool HasInternetConnection();
        
        /// <summary>
        /// Returns the status message for a tool.
        /// </summary>
        string GetToolStatus(string toolName);
        
        /// <summary>
        /// Executes a command and captures its output.
        /// </summary>
        Task<(int ExitCode, string StandardOutput, string StandardError)> RunCommandAsync(string toolName, string arguments);

        /// <summary>
        /// Checks which tools are outdated using winget.
        /// </summary>
        Task<System.Collections.Generic.HashSet<string>> CheckOutdatedToolsAsync();
    }
}
