using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ZTHubApp.Models
{
    public class Season : INotifyPropertyChanged
    {
        private int _number;
        public int Number
        {
            get => _number;
            set
            {
                _number = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Description));
            }
        }

        public string DisplayName => Number == 0 ? "Sans saison" : $"Saison {Number:D2}";

        public List<DownloadLink> Episodes { get; set; } = new();

        public int EpisodeCount => Episodes.Count(e => e.Link != "SEPARATOR");

        public string Description => $"{DisplayName} ({EpisodeCount} épisodes)";

        public override string ToString() => Description;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
