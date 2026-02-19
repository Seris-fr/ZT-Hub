using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ZTHubApp.Models
{
    /// <summary>
    /// Représente un groupe de saisons pour une même série
    /// </summary>
    public class SeriesGroup : INotifyPropertyChanged
    {
        private string _seriesName = string.Empty;
        public string SeriesName
        {
            get => _seriesName;
            set
            {
                _seriesName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        private string _language = string.Empty;
        public string Language
        {
            get => _language;
            set
            {
                _language = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(LanguageTag));
            }
        }

        public List<int> AvailableSeasons { get; set; } = new List<int>();

        public List<SearchResult> AllResults { get; set; } = new List<SearchResult>();

        private string _image = string.Empty;
        public string Image
        {
            get => _image;
            set
            {
                _image = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Nom d'affichage: "Nom de la série [VF]" ou "Nom de la série [VOSTFR]"
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(Language)
            ? SeriesName
            : $"{SeriesName} [{Language}]";

        /// <summary>
        /// Tag de langue pour affichage
        /// </summary>
        public string LanguageTag => string.IsNullOrEmpty(Language) ? "" : $"[{Language}]";

        /// <summary>
        /// Description des saisons disponibles
        /// </summary>
        public string SeasonsDescription
        {
            get
            {
                if (!AvailableSeasons.Any())
                    return "";

                if (AvailableSeasons.Count == 1)
                    return $"Saison {AvailableSeasons[0]}";

                var sorted = AvailableSeasons.OrderBy(s => s).ToList();
                return $"{sorted.Count} saisons (S{sorted.First()}-S{sorted.Last()})";
            }
        }

        /// <summary>
        /// Indique si ce groupe contient plusieurs saisons
        /// </summary>
        public bool IsMultiSeason => AvailableSeasons.Count > 1;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
