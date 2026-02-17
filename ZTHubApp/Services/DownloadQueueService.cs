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

        public void EnqueueDownloads(IEnumerable<(string Name, string Link, string FilePath)> downloads)
        {
            foreach (var (name, link, filePath) in downloads)
            {
                QueueItems.Add(new DownloadQueueItem
                {
                    Name = name,
                    Link = link,
                    FilePath = filePath,
                    Status = QueueItemStatus.Pending
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
            _currentDownloadCts = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(queueToken, _currentDownloadCts.Token);

            try
            {
                // Step 1: Unlock link
                item.Status = QueueItemStatus.Unlocking;
                StatusChanged?.Invoke(this, $"Débridage: {item.Name}");
                var finalLink = await _allDebridService.UnlockLinkAsync(item.Link);

                linkedCts.Token.ThrowIfCancellationRequested();

                // Step 2: Download
                item.Status = QueueItemStatus.Downloading;
                item.Progress = 0;
                StatusChanged?.Invoke(this, $"Téléchargement: {item.Name}");

                var progress = new Progress<(int percent, string speed)>(update =>
                {
                    item.Progress = update.percent;
                    StatusChanged?.Invoke(this, $"Téléchargement: {item.Name} - {update.percent}% ({update.speed})");
                });

                await _downloadService.DownloadFileAsync(finalLink, item.FilePath, progress, linkedCts.Token);

                item.Status = QueueItemStatus.Completed;
                item.Progress = 100;
                StatusChanged?.Invoke(this, $"Terminé: {item.Name}");
            }
            catch (OperationCanceledException)
            {
                item.Status = QueueItemStatus.Cancelled;
                StatusChanged?.Invoke(this, $"Annulé: {item.Name}");
            }
            catch (Exception ex)
            {
                item.Status = QueueItemStatus.Failed;
                item.ErrorMessage = ex.Message;
                StatusChanged?.Invoke(this, $"Échec: {item.Name} - {ex.Message}");
                // Continue with next item instead of stopping entire queue
            }
            finally
            {
                linkedCts.Dispose();
                _currentDownloadCts = null;
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
