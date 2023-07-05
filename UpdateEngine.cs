using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoUpdater.ui;

namespace AutoUpdater
{
    /// <summary>
    /// The main class to use for checking for updates. See README file for server requirements.
    /// </summary>
    public sealed class UpdateEngine : IDisposable
    {
        #region Constructors
        /// <summary>
        /// The main class to use for checking for updates.
        /// </summary>
        /// <param name="appName">The name of the application. It will be sent in the check query.</param>
        /// <param name="version">The current version of the application. It will be sent in the check query</param>
        /// <param name="updateUrl">The URL to which the check query is sent. </param>
        public UpdateEngine(string appName, string version, string updateUrl)
        {
            m_AppName = SanitizeAppName(appName);
            m_CurrentVersion = version;
            m_UpdateUrl = updateUrl;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Checks if there is a newer version of the application available.
        /// </summary>
        /// <returns>True if a newer version exists, false otherwise. </returns>
        public async Task<bool> CheckForUpdateAsync()
        {
            try
            {
                var updateUrl = $"{m_UpdateUrl}?name={m_AppName}";
                var response = await m_HttpClient.GetAsync(updateUrl);
                response.EnsureSuccessStatusCode();
                var contentString = await response.Content.ReadAsStringAsync();
                m_Data = JsonSerializer.Deserialize<VersionData>(contentString);
                if (m_Data == null)
                    throw new Exception($"Unable to parse response: {contentString}");

                if (!TryParseVersion(m_Data.Version ?? string.Empty, out var mostRecentVersion))
                    throw new Exception($"Unable to parse version from response: {m_Data.Version}");

                if (!TryParseVersion(m_CurrentVersion, out var currentVersion))
                    throw new Exception($"Unable to parse current version: {m_CurrentVersion}");

                return mostRecentVersion > currentVersion;
            }
            catch (Exception ex)
            {
                await WriteError(ex.ToString());
                return false;
            }
        }
        /// <summary>
        /// Downloads the update and runs it. The update is downloaded to the OS temp folder. The update should be an installer served by the server under the same URL as the check query, but with the version appended to it.
        /// </summary>
        /// <returns>True if the update was downloaded successfully, false otherwise.</returns>
        public async Task<bool> DownloadAndRunUpdate()
        {
            if (m_Data == null)
            {
                await WriteError("Update response is null or URL is null or empty");
                return false;
            }

            m_IndeterminateProgressWindow ??= new IndeterminateProgress();
            m_IndeterminateProgressWindow.ShowDialog();
            var result = false;
            var tempPath = Path.GetTempFileName();

            try
            {
                var response = await m_HttpClient.GetAsync(m_Data.Url);
                response.EnsureSuccessStatusCode();

                using var fileStream = File.Create(tempPath);
                await response.Content.CopyToAsync(fileStream);
                fileStream.Close();
                fileStream.Dispose();
                var updaterPath = Path.ChangeExtension(tempPath, ".msi");
                File.Move(tempPath, updaterPath);
                var installerProcess = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo(updaterPath)
                    {
                        UseShellExecute = true
                    }
                };
                installerProcess.Start();
                result = true;
            }
            catch (Exception ex)
            {
                await WriteError(ex.ToString());
            }
            finally
            {
                m_IndeterminateProgressWindow?.Close();
                m_IndeterminateProgressWindow = null;
            }
            return result;
        }
        #endregion

        #region Private Methods
        private static bool TryParseVersion(string version, out Version? parsedVersion)
        {
            parsedVersion = null;
            try
            {
                parsedVersion = new Version(version);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private async Task WriteError(string message)
        {
            await m_ErrorLogSemaphore.WaitAsync();
            var errorLogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "autoupdater", "error.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(errorLogFile) ?? string.Empty);
                await File.AppendAllTextAsync(errorLogFile, $"{m_AppName} - {DateTime.Now} - {message}\n");
            }
            catch { }
            finally
            {
                m_ErrorLogSemaphore.Release();
            }
        }
        private static string SanitizeAppName(string appName)
        {
            return appName.Replace(" ", "-").ToLower();
        }
        public void Dispose()
        {
            m_HttpClient.Dispose();
        }
        #endregion

        #region Fields
        private readonly string m_AppName;
        private readonly string m_CurrentVersion;
        private readonly string m_UpdateUrl;
        private readonly SemaphoreSlim m_ErrorLogSemaphore = new(1, 1);
        private static readonly HttpClient m_HttpClient = new();
        private IndeterminateProgress? m_IndeterminateProgressWindow;
        private VersionData? m_Data;
        #endregion


    }

    public sealed class VersionData
    {
        public string Version { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
