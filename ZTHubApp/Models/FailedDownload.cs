using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZTHubApp.Models
{
    public class FailedDownload : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        private string _originalLink = string.Empty;
        public string OriginalLink
        {
            get => _originalLink;
            set
            {
                _originalLink = value;
                OnPropertyChanged();
            }
        }

        private string _alternativeLink = string.Empty;
        public string AlternativeLink
        {
            get => _alternativeLink;
            set
            {
                _alternativeLink = value;
                OnPropertyChanged();
            }
        }

        private int _attemptCount;
        public int AttemptCount
        {
            get => _attemptCount;
            set
            {
                _attemptCount = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
