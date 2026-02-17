using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZTHubApp.Models;

namespace ZTHubApp.Services
{
    public class DownloadQueueService
    {
        private readonly AllDebridService _allDebridService;
        private readonly DownloadService _downloadService;
        private readonly int _delaySeconds;

        public ObservableCollection<DownloadQueueItem> QueueItems { get; } = new();

        private CancellationTokenSource? _queueCts;
        private CancellationTokenSource? _currentDownloadCts;

        public bool IsRunning { get; private set; }

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? QueueCompleted;

        public DownloadQueueService(string allDebridApiKey, int delaySeconds)
        {
            _allDebridService = new AllDebridService(allDebridApiKey);
            _downloadService = new DownloadService();
            _delaySeconds = delaySeconds;
        }

        public void EnqueueDownloads(IEnumerable<(string Name, string Link, string FilePath, List<(string HostName, string Link)> AlternativeLinks)> downloads)
        {
            foreach (var (name, link, filePath, altLinks) in downloads)
            {
                QueueItems.Add(new DownloadQueueItem
                {
                    Name = name,
                    Link = link,
                    AlternativeLinks = altLinks,
                    FilePath = filePath,
                    Status = QueueItemStatus.Pending,
                    CurrentHostName = altLinks.FirstOrDefault().HostName ?? "Inconnu"
                });
            }
        }

        public async Task ProcessQueueAsync()
        {
            if (IsRunning) return;

            IsRunning = true;
            _queueCts = new CancellationTokenSource();

            try
            {
                var pendingItems = QueueItems.Where(item => item.Status == QueueItemStatus.Pending).ToList();

                for (int i = 0; i < pendingItems.Count; i++)
                {
                    if (_queueCts.Token.IsCancellationRequested)
                    {
                        // Mark remaining items as cancelled
                        foreach (var remainingItem in pendingItems.Skip(i))
                        {
                            remainingItem.Status = QueueItemStatus.Cancelled;
                        }
                        break;
                    }

                    var item = pendingItems[i];
                    await ProcessSingleDownloadAsync(item, _queueCts.Token);

                    // Apply delay between downloads (except after last item)
                    if (i < pendingItems.Count - 1 && _delaySeconds > 0 && !_queueCts.Token.IsCancellationRequested)
                    {
                        StatusChanged?.Invoke(this, $"Attente de {_delaySeconds} secondes avant le prochain téléchargement...");
                        await Task.Delay(_delaySeconds * 1000, _queueCts.Token);
                    }
                }

                QueueCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                StatusChanged?.Invoke(this, "File d'attente annulée.");
            }
            finally
            {
                IsRunning = false;
                _queueCts = null;
            }
        }

        private async Task ProcessSingleDownloadAsync(DownloadQueueItem item, CancellationToken queueToken)
        {
            const int MAX_RETRIES = 3;
            const int RETRY_DELAY_SECONDS = 10;

            // INFINITE LOOP - only exits on success or cancellation
            while (true)
            {
                while (item.CurrentSourceIndex < item.AlternativeLinks.Count)
                {
                    var (hostName, link) = item.AlternativeLinks[item.CurrentSourceIndex];
                    item.CurrentHostName = hostName;
                    item.Link = link;
                    item.RetryCount = 0;

                    while (item.RetryCount < MAX_RETRIES)
                    {
                        _currentDownloadCts = new CancellationTokenSource();
                        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(queueToken, _currentDownloadCts.Token);

                        try
                        {
                            // Step 1: Unlock link
                            item.Status = QueueItemStatus.Unlocking;
                            StatusChanged?.Invoke(this, $"Débridage: {item.Name} ({hostName})");
                            var finalLink = await _allDebridService.UnlockLinkAsync(link);

                            linkedCts.Token.ThrowIfCancellationRequested();

                            // Step 1.5: Get correct file extension from unlocked link
                            var suggestedFileName = await _downloadService.GetSuggestedFileNameAsync(finalLink);
                            if (!string.IsNullOrEmpty(suggestedFileName))
                            {
                                var extension = System.IO.Path.GetExtension(suggestedFileName);
                                if (!string.IsNullOrEmpty(extension))
                                {
                                    // Update file path with correct extension
                                    var currentExtension = System.IO.Path.GetExtension(item.FilePath);
                                    if (string.IsNullOrEmpty(currentExtension) || currentExtension != extension)
                                    {
                                        var filePathWithoutExt = System.IO.Path.Combine(
                                            System.IO.Path.GetDirectoryName(item.FilePath) ?? "",
                                            System.IO.Path.GetFileNameWithoutExtension(item.FilePath)
                                        );
                                        item.FilePath = filePathWithoutExt + extension;
                                    }
                                }
                            }

                            linkedCts.Token.ThrowIfCancellationRequested();

                            // Step 2: Download
                            item.Status = QueueItemStatus.Downloading;
                            item.Progress = 0;
                            StatusChanged?.Invoke(this, $"Téléchargement: {item.Name} ({hostName})");

                            var progress = new Progress<(int percent, string speed)>(update =>
                            {
                                item.Progress = update.percent;
                                StatusChanged?.Invoke(this, $"Téléchargement: {item.Name} - {update.percent}% ({update.speed}) ({hostName})");
                            });

                            await _downloadService.DownloadFileAsync(finalLink, item.FilePath, progress, linkedCts.Token);

                            // Success!
                            item.Status = QueueItemStatus.Completed;
                            item.Progress = 100;
                            StatusChanged?.Invoke(this, $"Terminé: {item.Name}");
                            linkedCts.Dispose();
                            _currentDownloadCts = null;
                            return;
                        }
                        catch (OperationCanceledException)
                        {
                            item.Status = QueueItemStatus.Cancelled;
                            StatusChanged?.Invoke(this, $"Annulé: {item.Name}");
                            linkedCts.Dispose();
                            _currentDownloadCts = null;
                            return;
                        }
                        catch (Exception ex)
                        {
                            linkedCts.Dispose();
                            _currentDownloadCts = null;

                            item.RetryCount++;
                            item.ErrorMessage = ex.Message;

                            if (item.RetryCount < MAX_RETRIES)
                            {
                                // Retry with same source
                                item.Status = QueueItemStatus.Retrying;
                                StatusChanged?.Invoke(this, $"Échec tentative {item.RetryCount}/{MAX_RETRIES} pour {item.Name} ({hostName}). Nouvelle tentative dans {RETRY_DELAY_SECONDS}s...");

                                try
                                {
                                    await Task.Delay(RETRY_DELAY_SECONDS * 1000, queueToken);
                                }
                                catch (OperationCanceledException)
                                {
                                    item.Status = QueueItemStatus.Cancelled;
                                    return;
                                }
                            }
                            else
                            {
                                // Max retries reached, try next source
                                StatusChanged?.Invoke(this, $"Échec des 3 tentatives avec {hostName} pour {item.Name}. Changement de source...");
                                break;
                            }
                        }
                    }

                    // Move to next source
                    item.CurrentSourceIndex++;
                    if (item.CurrentSourceIndex < item.AlternativeLinks.Count)
                    {
                        var nextHost = item.AlternativeLinks[item.CurrentSourceIndex].HostName;
                        StatusChanged?.Invoke(this, $"Changement de source: {nextHost} pour {item.Name}");
                    }
                }

                // All sources exhausted - reset to first source and try again
                item.CurrentSourceIndex = 0;
                StatusChanged?.Invoke(this, $"Toutes les sources ont échoué pour {item.Name}. Recommence depuis 1fichier...");

                // Small delay before restarting the cycle
                try
                {
                    await Task.Delay(RETRY_DELAY_SECONDS * 1000, queueToken);
                }
                catch (OperationCanceledException)
                {
                    item.Status = QueueItemStatus.Cancelled;
                    return;
                }
            }
        }

        public void CancelCurrentDownload()
        {
            _currentDownloadCts?.Cancel();
        }

        public void CancelEntireQueue()
        {
            _queueCts?.Cancel();
        }

        public void ClearQueue()
        {
            QueueItems.Clear();
        }

        public (int total, int completed, int failed, int pending) GetQueueStats()
        {
            return (
                total: QueueItems.Count,
                completed: QueueItems.Count(i => i.Status == QueueItemStatus.Completed),
                failed: QueueItems.Count(i => i.Status == QueueItemStatus.Failed),
                pending: QueueItems.Count(i => i.Status == QueueItemStatus.Pending)
            );
        }
    }
}
