using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZTHubApp.Models
{
    public enum QueueItemStatus
    {
        Pending,      // Waiting to start
        Unlocking,    // Debridding the link
        Downloading,  // Currently downloading
        Retrying,     // Retrying after failure
        Completed,    // Successfully downloaded
        Failed,       // Download failed
        Cancelled     // User cancelled
    }

    public class DownloadQueueItem : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public List<(string HostName, string Link)> AlternativeLinks { get; set; } = new();
        public string FilePath { get; set; } = string.Empty;

        public int RetryCount { get; set; } = 0;
        public int CurrentSourceIndex { get; set; } = 0;

        private string _currentHostName = "";
        public string CurrentHostName
        {
            get => _currentHostName;
            set
            {
                _currentHostName = value;
                OnPropertyChanged();
            }
        }

        private QueueItemStatus _status = QueueItemStatus.Pending;
        public QueueItemStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
            }
        }

        private int _progress;
        public int Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private string _errorMessage = "";
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get
            {
                return Status switch
                {
                    QueueItemStatus.Pending => "En attente",
                    QueueItemStatus.Unlocking => $"Débridage... ({CurrentHostName})",
                    QueueItemStatus.Downloading => $"{Progress}% ({CurrentHostName})",
                    QueueItemStatus.Retrying => $"⟳ Retry {RetryCount}/3",
                    QueueItemStatus.Completed => "✓ Terminé",
                    QueueItemStatus.Failed => "✗ Erreur",
                    QueueItemStatus.Cancelled => "Annulé",
                    _ => ""
                };
            }
        }

        public string StatusColor
        {
            get
            {
                return Status switch
                {
                    QueueItemStatus.Pending => "#888888",
                    QueueItemStatus.Unlocking => "#FFA500",
                    QueueItemStatus.Downloading => "#4A9EFF",
                    QueueItemStatus.Retrying => "#FF9800",
                    QueueItemStatus.Completed => "#4CAF50",
                    QueueItemStatus.Failed => "#F44336",
                    QueueItemStatus.Cancelled => "#888888",
                    _ => "White"
                };
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
