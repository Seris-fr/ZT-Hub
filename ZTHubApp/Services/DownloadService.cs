using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ZTHubApp.Services
{
    public class DownloadService
    {
        public async Task DownloadFileAsync(string url, string filePath, IProgress<(int percent, string speed)> progress, CancellationToken ct)
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long downloadedBytes = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long lastReportedBytes = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead == 0) break;

                await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                downloadedBytes += bytesRead;

                if (stopwatch.ElapsedMilliseconds > 500)
                {
                    var speed = (downloadedBytes - lastReportedBytes) / stopwatch.Elapsed.TotalSeconds;
                    var percent = totalBytes > 0 ? (int)((double)downloadedBytes / totalBytes * 100) : 0;
                    
                    progress.Report((percent, FormatSpeed(speed)));
                    
                    stopwatch.Restart();
                    lastReportedBytes = downloadedBytes;
                }
            }
            progress.Report((100, "Terminé"));
        }

        public async Task<string?> GetSuggestedFileNameAsync(string url)
        {
            try
            {
                using var client = new HttpClient();
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.IsSuccessStatusCode && response.Content.Headers.ContentDisposition?.FileName is { } fileName && !string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName.Trim('\"');
                }
                
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    string pathFileName = Path.GetFileName(uri.LocalPath);
                    if (!string.IsNullOrEmpty(pathFileName)) return pathFileName;
                }
            }
            catch { /* Ignore les erreurs */ }
            return null;
        }

        private string FormatSpeed(double bytesPerSecond)
        {
            string[] sizes = { "B/s", "KB/s", "MB/s", "GB/s" };
            int order = 0;
            while (bytesPerSecond >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytesPerSecond /= 1024;
            }
            return $"{bytesPerSecond:0.##} {sizes[order]}";
        }
    }
}