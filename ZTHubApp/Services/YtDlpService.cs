using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ZTHubApp.Services
{
    public class YtDlpService
    {
        private static readonly string YtDlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Vérifie si le lien est un site de streaming supporté par yt-dlp
        /// </summary>
        public static bool IsStreamingSite(string url)
        {
            var streamingSites = new[]
            {
                "streamtape", "doodstream", "mixdrop", "upstream",
                "youtube", "dailymotion", "vimeo", "twitter", "twitch"
            };

            foreach (var site in streamingSites)
            {
                if (url.Contains(site, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Télécharge yt-dlp s'il n'existe pas
        /// </summary>
        public static async Task<bool> EnsureYtDlpExistsAsync()
        {
            if (File.Exists(YtDlpPath))
                return true;

            try
            {
                // Télécharger yt-dlp depuis GitHub
                var ytDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
                var data = await _httpClient.GetByteArrayAsync(ytDlpUrl);
                await File.WriteAllBytesAsync(YtDlpPath, data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Télécharge une vidéo avec yt-dlp
        /// </summary>
        public static async Task<string> DownloadVideoAsync(
            string url,
            string outputFolder,
            IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            if (!await EnsureYtDlpExistsAsync())
                throw new Exception("Impossible de télécharger yt-dlp");

            var processInfo = new ProcessStartInfo
            {
                FileName = YtDlpPath,
                Arguments = $"--no-check-certificate --no-playlist --newline --no-warnings " +
                           $"--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\" " +
                           $"--referer \"{url}\" " +
                           $"--add-header \"Accept:*/*\" " +
                           $"-o \"{Path.Combine(outputFolder, "%(title)s.%(ext)s")}\" " +
                           $"\"{url}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processInfo };

            string downloadedFile = string.Empty;
            var errorMessages = new System.Collections.Generic.List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    progress?.Report(e.Data);

                    // Extraire le nom du fichier téléchargé
                    if (e.Data.Contains("[download] Destination:"))
                    {
                        var parts = e.Data.Split(new[] { "[download] Destination:" }, StringSplitOptions.None);
                        if (parts.Length > 1)
                            downloadedFile = parts[1].Trim();
                    }
                    else if (e.Data.Contains("has already been downloaded"))
                    {
                        var parts = e.Data.Split(new[] { "[download]" }, StringSplitOptions.None);
                        if (parts.Length > 1)
                        {
                            var filename = parts[1].Replace("has already been downloaded", "").Trim();
                            downloadedFile = Path.Combine(outputFolder, filename);
                        }
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorMessages.Add(e.Data);
                    progress?.Report($"⚠ {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Attendre la fin du processus ou l'annulation
            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    process.Kill();
                    throw new OperationCanceledException();
                }
                await Task.Delay(100, cancellationToken);
            }

            if (process.ExitCode != 0)
            {
                var errorMsg = errorMessages.Count > 0
                    ? string.Join("\n", errorMessages.TakeLast(3))
                    : "Erreur inconnue";
                throw new Exception($"yt-dlp a échoué (code {process.ExitCode}):\n{errorMsg}");
            }

            return downloadedFile;
        }
    }
}
