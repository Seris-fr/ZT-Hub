using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using ZTHubApp.Models;
using ZTHubApp.Services;

namespace ZTHubApp.Views
{
    public partial class SeasonSelectionWindow : Window
    {
        private readonly List<SeasonCheckItem> _seasonItems = new();
        private readonly SeriesGroup _seriesGroup;
        private readonly MediaScannerService _scannerService;
        private readonly string _contentType;

        public List<int> SelectedSeasons
        {
            get => _seasonItems.Where(s => s.IsSelected && s.IsEnabled).Select(s => s.SeasonNumber).ToList();
        }

        public SeasonSelectionWindow(
            SeriesGroup seriesGroup,
            MediaScannerService scannerService,
            string category)
        {
            InitializeComponent();

            _seriesGroup = seriesGroup;
            _scannerService = scannerService;
            _contentType = category == "mangas" ? "Anime" : "Serie";

            InitializeSeasons();
            UpdateInfoText();
        }

        private void InitializeSeasons()
        {
            // Mettre à jour le titre
            TitleTextBlock.Text = $"{_seriesGroup.DisplayName}\n{_seriesGroup.SeasonsDescription}";

            // Créer les items pour chaque saison disponible
            foreach (var seasonNum in _seriesGroup.AvailableSeasons.OrderBy(s => s))
            {
                bool isDownloaded = _scannerService.IsSeasonDownloaded(
                    _seriesGroup.SeriesName,
                    seasonNum,
                    _contentType);

                var item = new SeasonCheckItem
                {
                    SeasonNumber = seasonNum,
                    DisplayText = $"Saison {seasonNum:D2}",
                    IsSelected = !isDownloaded, // Pré-cocher les saisons manquantes
                    IsEnabled = !isDownloaded,  // Désactiver les saisons déjà téléchargées
                    TextColor = isDownloaded ? new SolidColorBrush(Colors.Gray) : new SolidColorBrush(Colors.White)
                };

                if (isDownloaded)
                {
                    item.DisplayText += " (Déjà téléchargée)";
                }

                _seasonItems.Add(item);
            }

            SeasonsItemsControl.ItemsSource = _seasonItems;
        }

        private void UpdateInfoText()
        {
            var downloadedCount = _seasonItems.Count(s => !s.IsEnabled);
            var availableCount = _seasonItems.Count(s => s.IsEnabled);

            if (downloadedCount > 0)
            {
                InfoTextBlock.Text = $"ℹ {downloadedCount} saison(s) déjà téléchargée(s), {availableCount} disponible(s)";
                InfoTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                InfoTextBlock.Visibility = Visibility.Collapsed;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _seasonItems.Where(s => s.IsEnabled))
            {
                item.IsSelected = true;
            }
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _seasonItems.Where(s => s.IsEnabled))
            {
                item.IsSelected = false;
            }
        }

        private void SelectMissing_Click(object sender, RoutedEventArgs e)
        {
            // Cocher uniquement les saisons non téléchargées (déjà fait par défaut)
            foreach (var item in _seasonItems)
            {
                item.IsSelected = item.IsEnabled;
            }
        }

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            var selectedCount = SelectedSeasons.Count;

            if (selectedCount == 0)
            {
                MessageBox.Show(
                    "Veuillez sélectionner au moins une saison à télécharger.",
                    "Aucune saison sélectionnée",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    /// <summary>
    /// Élément de saison avec CheckBox binding
    /// </summary>
    public class SeasonCheckItem : INotifyPropertyChanged
    {
        public int SeasonNumber { get; set; }

        private string _displayText = string.Empty;
        public string DisplayText
        {
            get => _displayText;
            set
            {
                _displayText = value;
                OnPropertyChanged();
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }

        private SolidColorBrush _textColor = new(Colors.White);
        public SolidColorBrush TextColor
        {
            get => _textColor;
            set
            {
                _textColor = value;
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
