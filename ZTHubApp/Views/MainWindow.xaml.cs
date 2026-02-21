using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ZTHubApp.Models;
using ZTHubApp.Services;

namespace ZTHubApp.Views
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private SearchService _searchService;
        private readonly LinkExtractorService _linkExtractor = new();
        private readonly DownloadService _downloadService = new();
        private readonly MediaScannerService _scannerService = new();

        public ObservableCollection<object> SearchResults { get; } = new(); // Holds SearchResult or SeriesGroup
        public ObservableCollection<string> Hosters { get; } = new();
        public ObservableCollection<DownloadLink> DownloadLinks { get; } = new();
        public ObservableCollection<DownloadQueueItem> DownloadQueue { get; } = new();
        public ObservableCollection<Season> Seasons { get; } = new();
        public ObservableCollection<FailedDownload> FailedDownloads { get; } = new();

        private bool _hasFailedDownloads;
        public bool HasFailedDownloads
        {
            get => _hasFailedDownloads;
            set
            {
                _hasFailedDownloads = value;
                OnPropertyChanged();
            }
        }

        private bool _hasSeasons;
        public bool HasSeasons
        {
            get => _hasSeasons;
            set
            {
                _hasSeasons = value;
                OnPropertyChanged();
            }
        }

        private string _episodeListHeader = "Épisodes:";
        public string EpisodeListHeader
        {
            get => _episodeListHeader;
            set
            {
                _episodeListHeader = value;
                OnPropertyChanged();
            }
        }

        private string _searchQuery = "";
        public string SearchQuery { get => _searchQuery; set { _searchQuery = value; OnPropertyChanged(); } }

        private string _selectedCategory = "mangas";
        public string SelectedCategory { get => _selectedCategory; set { _selectedCategory = value; OnPropertyChanged(); } }

        private bool _vfOnlyFilter = false;
        public bool VFOnlyFilter { get => _vfOnlyFilter; set { _vfOnlyFilter = value; OnPropertyChanged(); } }

        private bool _hasSearchResults = false;
        public bool HasSearchResults { get => _hasSearchResults; set { _hasSearchResults = value; OnPropertyChanged(); } }

        private string _statusText = "Prêt";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

        private int _currentPage = 1;
        private int _totalPages = 1;
        public string PageInfo => $"Page {_currentPage} / {_totalPages}";
        public bool CanGoPrevious => _currentPage > 1;
        public bool CanGoNext => _currentPage < _totalPages;

        private List<HostLinkGroup> _allDownloadLinks = new();
        private string _allDebridApiKey = "";
        private CancellationTokenSource? _downloadCts;

        private CancellationTokenSource? _linkExtractionCts;

        private CancellationTokenSource? _searchCts;

        private DownloadQueueService? _queueService;
        private int _downloadDelay = 10; // Default 10 seconds
        private string _lastDownloadFolder = "";
        private string _defaultDownloadFolder = "";

        private bool _isQueueActive;
        public bool IsQueueActive
        {
            get => _isQueueActive;
            set
            {
                _isQueueActive = value;
                OnPropertyChanged();
            }
        }

        private string _queueStatus = "";
        public string QueueStatus
        {
            get => _queueStatus;
            set
            {
                _queueStatus = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            LoadSettings();
            _searchService = new SearchService(GetBaseUrl());

            InitializeComponent();
            this.DataContext = this;

            // Scanner les fichiers existants au démarrage
            Loaded += async (s, e) => await ScanExistingMediaAsync();
        }

        private async Task ScanExistingMediaAsync()
        {
            if (string.IsNullOrEmpty(_defaultDownloadFolder) || !Directory.Exists(_defaultDownloadFolder))
                return;

            try
            {
                StatusText = "Scan des médias existants...";
                await _scannerService.ScanMediaFoldersAsync(_defaultDownloadFolder);
                var stats = _scannerService.GetScanStats();
                StatusText = $"Scan terminé: {stats}";
                await Task.Delay(2000);
                StatusText = "Prêt";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur scan: {ex.Message}");
                StatusText = "Prêt";
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName == nameof(_currentPage) || propertyName == nameof(_totalPages))
            {
                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
        
        // --- Event Handlers du XAML ---
        
        
        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) PerformSearch();
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e) => PerformSearch();
        
        private async void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (CanGoPrevious) { _currentPage--; await ExecuteSearchAsync(isNewSearch: false); }
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (CanGoNext) { _currentPage++; await ExecuteSearchAsync(isNewSearch: false); }
        }
        

        
        private async void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox listBox) return;

            var selectedItems = listBox.SelectedItems.Cast<object>().ToList();
            if (selectedItems.Count == 0) return;

            var selectedItem = selectedItems[0];

            // Handle SeriesGroup - show popup for season selection
            if (selectedItem is SeriesGroup seriesGroup)
            {
                var seasonWindow = new SeasonSelectionWindow(seriesGroup, _scannerService, SelectedCategory);
                var dialogResult = seasonWindow.ShowDialog();

                if (dialogResult == true && seasonWindow.SelectedSeasons.Count > 0)
                {
                    // Get search results for selected seasons
                    var selectedResults = seriesGroup.AllResults
                        .Where(r =>
                        {
                            var metadata = ContentMetadataExtractor.ExtractMetadata(r.Title);
                            return metadata.SeasonNumber.HasValue &&
                                   seasonWindow.SelectedSeasons.Contains(metadata.SeasonNumber.Value);
                        })
                        .ToList();

                    // Extract links for all selected seasons
                    await LoadLinksForMultipleResults(selectedResults);
                }
                return;
            }

            // Handle multiple SearchResults selected (Ctrl+clic)
            var selectedSearchResults = selectedItems.OfType<SearchResult>().ToList();
            if (selectedSearchResults.Count > 1)
            {
                StatusText = $"Chargement des liens pour {selectedSearchResults.Count} saisons...";
                await LoadLinksForMultipleResults(selectedSearchResults);
                return;
            }

            // Handle SearchResult (individual item)
            if (selectedItem is SearchResult selectedResult)
            {
                _linkExtractionCts?.Cancel();
                _linkExtractionCts = new CancellationTokenSource();
                var token = _linkExtractionCts.Token;

                StatusText = "Récupération des liens...";
                DownloadLinks.Clear();
                Hosters.Clear();
                _allDownloadLinks.Clear();
                Seasons.Clear();
                HasSeasons = false;
                EpisodeListHeader = "Épisodes:";

                try
                {
                    _allDownloadLinks = await _linkExtractor.ExtractDownloadLinksAsync(selectedResult.Link, token);

                    if (token.IsCancellationRequested) return;

                    var uniqueHosters = _allDownloadLinks.Select(g => g.HostName).Distinct().OrderBy(h => h);
                    foreach (var hoster in uniqueHosters) Hosters.Add(hoster);

                    if (Hosters.Any() && HosterComboBox != null) HosterComboBox.SelectedIndex = 0;
                    StatusText = $"{Hosters.Count} hébergeur(s) trouvé(s).";
                }
                catch (OperationCanceledException)
                {
                    StatusText = "Opération annulée.";
                }
                catch (Exception ex)
                {
                    StatusText = $"Erreur: {ex.Message}";
                    MessageBox.Show(ex.Message, "Erreur d'extraction", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task LoadLinksForMultipleResults(List<SearchResult> results)
        {
            _linkExtractionCts?.Cancel();
            _linkExtractionCts = new CancellationTokenSource();
            var token = _linkExtractionCts.Token;

            StatusText = $"Récupération des liens pour {results.Count} saison(s)...";
            DownloadLinks.Clear();
            Hosters.Clear();
            _allDownloadLinks.Clear();
            Seasons.Clear();
            HasSeasons = false;
            EpisodeListHeader = "Épisodes:";

            try
            {
                // Extract links for all selected results with separators
                bool isFirst = true;
                foreach (var result in results)
                {
                    if (token.IsCancellationRequested) return;

                    // Add separator between results
                    if (!isFirst)
                    {
                        // Add a separator group
                        _allDownloadLinks.Add(new HostLinkGroup
                        {
                            HostName = "──SEPARATOR──",
                            Type = "Separator",
                            Links = new List<DownloadLink>
                            {
                                new DownloadLink { Name = "═══════════════════════════════════", Link = "SEPARATOR" }
                            }
                        });
                    }

                    // Add title/header for this result
                    var metadata = ContentMetadataExtractor.ExtractMetadata(result.Title);
                    string seasonInfo = metadata.SeasonNumber.HasValue ? $" - Saison {metadata.SeasonNumber.Value:D2}" : "";

                    _allDownloadLinks.Add(new HostLinkGroup
                    {
                        HostName = "──HEADER──",
                        Type = "Header",
                        Links = new List<DownloadLink>
                        {
                            new DownloadLink { Name = $"📁 {metadata.CleanedName}{seasonInfo}", Link = "SEPARATOR" }
                        }
                    });

                    var links = await _linkExtractor.ExtractDownloadLinksAsync(result.Link, token);
                    _allDownloadLinks.AddRange(links);

                    isFirst = false;
                }

                if (token.IsCancellationRequested) return;

                var uniqueHosters = _allDownloadLinks
                    .Where(g => !g.HostName.StartsWith("──")) // Exclude separators and headers
                    .Select(g => g.HostName)
                    .Distinct()
                    .OrderBy(h => h);
                foreach (var hoster in uniqueHosters) Hosters.Add(hoster);

                if (Hosters.Any() && HosterComboBox != null) HosterComboBox.SelectedIndex = 0;
                StatusText = $"{Hosters.Count} hébergeur(s) trouvé(s) pour {results.Count} saison(s).";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Opération annulée.";
            }
            catch (Exception ex)
            {
                StatusText = $"Erreur: {ex.Message}";
                MessageBox.Show(ex.Message, "Erreur d'extraction", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void HosterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox cb) LoadEpisodesForHoster(cb.SelectedItem as string);
        }
        
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = EpisodeListBox.SelectedItems.Cast<DownloadLink>()
                .Where(link => link.Link != "SEPARATOR")
                .ToList();

            if (!selectedItems.Any())
            {
                MessageBox.Show("Veuillez sélectionner au moins un lien à télécharger.",
                    "Aucune sélection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(_allDebridApiKey))
            {
                MessageBox.Show("Veuillez configurer votre clé API AllDebrid dans les paramètres (icône ⚙️).",
                    "Configuration requise", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Single item - use legacy flow
            if (selectedItems.Count == 1)
            {
                await DownloadSingleItemAsync(selectedItems[0]);
                return;
            }

            // Multiple items - use queue
            await DownloadMultipleItemsAsync(selectedItems);
        }

        private async Task DownloadSingleItemAsync(DownloadLink selectedLink)
        {
            try
            {
                StatusText = "Débridage du lien...";
                var allDebridService = new AllDebridService(_allDebridApiKey);
                var finalLink = await allDebridService.UnlockLinkAsync(selectedLink.Link);

                StatusText = "Récupération du nom du fichier...";
                string suggestedFileName = await _downloadService.GetSuggestedFileNameAsync(finalLink)
                    ?? selectedLink.Name.Trim();

                var saveDialog = new SaveFileDialog
                {
                    FileName = SanitizeFileName(suggestedFileName),
                    Filter = "Tous les fichiers (*.*)|*.*",
                    InitialDirectory = !string.IsNullOrEmpty(_lastDownloadFolder)
                        ? _lastDownloadFolder
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (saveDialog.ShowDialog() == true)
                {
                    _lastDownloadFolder = Path.GetDirectoryName(saveDialog.FileName) ?? "";
                    _downloadCts = new CancellationTokenSource();
                    var progress = new Progress<(int percent, string speed)>(
                        update => StatusText = $"Téléchargement : {update.percent}% ({update.speed})");
                    await _downloadService.DownloadFileAsync(finalLink, saveDialog.FileName, progress, _downloadCts.Token);
                    StatusText = "Téléchargement terminé !";
                }
            }
            catch (OperationCanceledException) { StatusText = "Téléchargement annulé."; }
            catch (Exception ex) { StatusText = $"Erreur de téléchargement: {ex.Message}"; }
        }

        private async Task DownloadMultipleItemsAsync(List<DownloadLink> selectedItems)
        {
            string targetFolder;

            // Si dossier par défaut configuré et valide, l'utiliser
            if (!string.IsNullOrWhiteSpace(_defaultDownloadFolder) && Directory.Exists(_defaultDownloadFolder))
            {
                targetFolder = _defaultDownloadFolder;
            }
            else
            {
                // Sinon, demander à l'utilisateur
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = $"Sélectionnez le dossier de destination pour {selectedItems.Count} fichiers",
                    SelectedPath = !string.IsNullOrEmpty(_lastDownloadFolder)
                        ? _lastDownloadFolder
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                targetFolder = folderDialog.SelectedPath;
            }

            _lastDownloadFolder = targetFolder;

            // Confirmation avec chemin de destination
            var confirmMessage = $"Voulez-vous télécharger {selectedItems.Count} fichier(s) ?\n\n" +
                                 $"📁 Destination : {targetFolder}\n\n" +
                                 string.Join("\n", selectedItems.Take(5).Select(i => "• " + i.Name));

            if (selectedItems.Count > 5)
                confirmMessage += $"\n... et {selectedItems.Count - 5} autre(s)";

            var confirmResult = MessageBox.Show(
                confirmMessage,
                "Confirmer le téléchargement",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
            {
                StatusText = "Téléchargement annulé.";
                return;
            }

            try
            {
                StatusText = "Préparation de la file de téléchargement...";

                // Create queue service if it doesn't exist
                if (_queueService == null)
                {
                    _queueService = new DownloadQueueService(_allDebridApiKey, _downloadDelay);
                    _queueService.StatusChanged += (s, status) => Dispatcher.Invoke(() =>
                    {
                        StatusText = status;
                        // Update queue status in real-time
                        if (_queueService != null)
                        {
                            var stats = _queueService.GetQueueStats();
                            QueueStatus = $"File: {stats.completed}/{stats.total} terminés ({stats.pending} en attente)";
                        }
                    });
                    _queueService.QueueCompleted += OnQueueCompleted;
                    _queueService.DownloadFailed += OnDownloadFailed;
                    DownloadQueue.Clear();
                }

                // Prepare download items with alternative sources
                var downloads = new List<(string Name, string Link, string FilePath, List<(string HostName, string Link)> AlternativeLinks)>();

                foreach (var item in selectedItems)
                {
                    string fileName = SanitizeFileName(item.Name.Trim());
                    string filePath;

                    // Trouver le header de saison qui précède cet item dans DownloadLinks
                    int? seasonNumber = null;
                    int itemIndex = DownloadLinks.IndexOf(item);

                    if (itemIndex >= 0)
                    {
                        // Remonter dans la liste pour trouver le header le plus proche
                        for (int i = itemIndex - 1; i >= 0; i--)
                        {
                            var link = DownloadLinks[i];
                            if (link.Link == "SEPARATOR" && link.Name.StartsWith("📁"))
                            {
                                // Extraire le numéro de saison du header
                                var match = System.Text.RegularExpressions.Regex.Match(link.Name, @"Saison (\d+)");
                                if (match.Success)
                                {
                                    seasonNumber = int.Parse(match.Groups[1].Value);
                                    break;
                                }
                            }
                        }
                    }

                    // Créer le chemin avec ou sans dossier de saison
                    if (seasonNumber.HasValue)
                    {
                        string seasonFolder = Path.Combine(targetFolder, $"Saison {seasonNumber.Value:D2}");
                        Directory.CreateDirectory(seasonFolder);
                        filePath = Path.Combine(seasonFolder, fileName);
                    }
                    else
                    {
                        filePath = Path.Combine(targetFolder, fileName);
                    }

                    // Find all alternative links for this file from all hosters
                    var alternativeLinks = new List<(string HostName, string Link)>();

                    foreach (var hostGroup in _allDownloadLinks)
                    {
                        var matchingLink = hostGroup.Links.FirstOrDefault(l => l.Link == item.Link);
                        if (matchingLink != null)
                        {
                            // Found the current link, now get all links with same name from all hosts
                            foreach (var otherHostGroup in _allDownloadLinks)
                            {
                                var sameFileLink = otherHostGroup.Links.FirstOrDefault(l => l.Name.Trim() == item.Name.Trim());
                                if (sameFileLink != null && sameFileLink.Link != "SEPARATOR")
                                {
                                    alternativeLinks.Add((otherHostGroup.HostName, sameFileLink.Link));
                                }
                            }
                            break;
                        }
                    }

                    // If no alternatives found, use just the current link
                    if (!alternativeLinks.Any())
                    {
                        alternativeLinks.Add(("Inconnu", item.Link));
                    }

                    // IMPORTANT: Always put 1fichier first
                    alternativeLinks = alternativeLinks
                        .OrderByDescending(link => link.HostName.Contains("1fichier", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(link => link.HostName)
                        .ToList();

                    downloads.Add((item.Name, item.Link, filePath, alternativeLinks));
                }

                // Enqueue all downloads
                _queueService.EnqueueDownloads(downloads);

                // Bind new queue items to UI
                // Only add items that aren't already in DownloadQueue
                var existingItems = DownloadQueue.ToList();
                foreach (var queueItem in _queueService.QueueItems)
                {
                    if (!existingItems.Contains(queueItem))
                    {
                        DownloadQueue.Add(queueItem);
                    }
                }

                IsQueueActive = true;
                var stats = _queueService.GetQueueStats();
                QueueStatus = $"File: {stats.completed}/{stats.total} terminés";

                // Start processing only if not already running
                if (!_queueService.IsRunning)
                {
                    StatusText = "Démarrage de la file de téléchargement...";
                    await _queueService.ProcessQueueAsync();
                }
                else
                {
                    StatusText = $"{downloads.Count} téléchargements ajoutés à la file en cours";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Erreur de file d'attente: {ex.Message}";
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDownloadFailed(object? sender, DownloadQueueItem failedItem)
        {
            Dispatcher.Invoke(() =>
            {
                FailedDownloads.Add(new FailedDownload
                {
                    Name = failedItem.Name,
                    OriginalLink = failedItem.Link,
                    AttemptCount = failedItem.RetryCount
                });
                HasFailedDownloads = true;
            });
        }

        private void OnQueueCompleted(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_queueService != null)
                {
                    var stats = _queueService.GetQueueStats();
                    StatusText = $"File terminée: {stats.completed} réussis, {stats.failed} échoués sur {stats.total}";
                    QueueStatus = $"Terminé: {stats.completed}/{stats.total} réussis, {stats.failed} échoués";

                    if (stats.failed > 0)
                    {
                        var failedItems = _queueService.QueueItems
                            .Where(i => i.Status == QueueItemStatus.Failed)
                            .Select(i => $"- {i.Name}: {i.ErrorMessage}")
                            .ToList();

                        MessageBox.Show(
                            $"Téléchargements terminés avec {stats.failed} erreur(s):\n\n" +
                            string.Join("\n", failedItems),
                            "Téléchargements terminés avec erreurs",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Tous les téléchargements ont réussi ({stats.completed}/{stats.total})",
                            "Téléchargements terminés",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            });
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            _downloadCts?.Cancel();
            _queueService?.CancelCurrentDownload();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            EpisodeListBox.SelectAll();
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            EpisodeListBox.UnselectAll();
        }

        private void CancelQueue_Click(object sender, RoutedEventArgs e)
        {
            _queueService?.CancelEntireQueue();
            StatusText = "Annulation de la file d'attente...";
        }

        private async void RetryFailedDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not FailedDownload failedItem)
                return;

            if (string.IsNullOrWhiteSpace(failedItem.AlternativeLink))
            {
                MessageBox.Show("Veuillez coller un lien alternatif avant de télécharger.",
                    "Lien manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Utiliser la logique de DL Direct
                var link = failedItem.AlternativeLink.Trim();

                // Vérifier si c'est un site de streaming
                if (YtDlpService.IsStreamingSite(link))
                {
                    StatusText = $"Téléchargement avec yt-dlp: {failedItem.Name}...";

                    string targetFolder = !string.IsNullOrEmpty(_defaultDownloadFolder) && Directory.Exists(_defaultDownloadFolder)
                        ? _defaultDownloadFolder
                        : _lastDownloadFolder;

                    if (string.IsNullOrEmpty(targetFolder))
                        targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                    _downloadCts = new CancellationTokenSource();
                    var progress = new Progress<string>(msg => StatusText = msg);
                    await YtDlpService.DownloadVideoAsync(link, targetFolder, progress, _downloadCts.Token);

                    StatusText = $"✅ {failedItem.Name} téléchargé avec succès !";
                    FailedDownloads.Remove(failedItem);
                    HasFailedDownloads = FailedDownloads.Count > 0;
                }
                else
                {
                    // Débridage + téléchargement normal
                    StatusText = $"Débridage: {failedItem.Name}...";
                    var allDebridService = new AllDebridService(_allDebridApiKey);
                    var finalLink = await allDebridService.UnlockLinkAsync(link);

                    string fileName = SanitizeFileName(failedItem.Name);
                    string targetFolder = !string.IsNullOrEmpty(_defaultDownloadFolder) && Directory.Exists(_defaultDownloadFolder)
                        ? _defaultDownloadFolder
                        : _lastDownloadFolder;

                    if (string.IsNullOrEmpty(targetFolder))
                        targetFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                    string filePath = Path.Combine(targetFolder, fileName);

                    _downloadCts = new CancellationTokenSource();
                    var progress = new Progress<(int percent, string speed)>(
                        update => StatusText = $"Téléchargement {failedItem.Name}: {update.percent}% ({update.speed})");
                    await _downloadService.DownloadFileAsync(finalLink, filePath, progress, _downloadCts.Token);

                    StatusText = $"✅ {failedItem.Name} téléchargé avec succès !";
                    FailedDownloads.Remove(failedItem);
                    HasFailedDownloads = FailedDownloads.Count > 0;
                }
            }
            catch (Exception ex)
            {
                StatusText = $"❌ Erreur: {ex.Message}";
                MessageBox.Show($"Erreur lors du téléchargement:\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsWindow(_allDebridApiKey, GetBaseUrl(), _downloadDelay, _defaultDownloadFolder) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _allDebridApiKey = dialog.AllDebridApiKey;
                _downloadDelay = dialog.DownloadDelay;
                _defaultDownloadFolder = dialog.DefaultDownloadFolder;
                SaveSettings(dialog.BaseUrl);
                _searchService = new SearchService(dialog.BaseUrl);
                StatusText = "Paramètres sauvegardés.";
            }
        }

        private async void DirectDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_allDebridApiKey))
            {
                MessageBox.Show("Veuillez configurer votre clé API AllDebrid dans les paramètres (icône ⚙️).",
                    "Configuration requise", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Demander le lien à télécharger
            var inputWindow = new Window
            {
                Title = "Téléchargement direct",
                Width = 500,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30))
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = "Collez le lien à télécharger:", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 5) };
            var textBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "Télécharger", Width = 100, Margin = new Thickness(0, 0, 5, 0) };
            var cancelButton = new Button { Content = "Annuler", Width = 100 };

            okButton.Click += (s, ev) => { inputWindow.DialogResult = true; inputWindow.Close(); };
            cancelButton.Click += (s, ev) => { inputWindow.DialogResult = false; inputWindow.Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(label, 0);
            Grid.SetRow(textBox, 1);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            inputWindow.Content = grid;

            if (inputWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(textBox.Text))
                return;

            var link = textBox.Text.Trim();

            try
            {
                // Vérifier si c'est uqload (traitement spécial)
                if (UqloadService.IsUqloadUrl(link))
                {
                    StatusText = "Extraction de l'URL vidéo depuis uqload...";

                    // Extraire l'URL directe de la vidéo
                    var videoUrl = await UqloadService.ExtractVideoUrlAsync(link);
                    StatusText = $"URL extraite: {videoUrl.Substring(0, Math.Min(50, videoUrl.Length))}...";

                    // Demander le dossier de destination
                    string targetFolder;
                    if (!string.IsNullOrEmpty(_defaultDownloadFolder) && Directory.Exists(_defaultDownloadFolder))
                    {
                        targetFolder = _defaultDownloadFolder;
                    }
                    else
                    {
                        var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                        {
                            Description = "Choisissez le dossier de destination",
                            SelectedPath = !string.IsNullOrEmpty(_lastDownloadFolder)
                                ? _lastDownloadFolder
                                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                        };

                        if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                            return;

                        targetFolder = folderDialog.SelectedPath;
                    }

                    _lastDownloadFolder = targetFolder;

                    // Télécharger la vidéo
                    string fileName = $"uqload_video_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                    string filePath = Path.Combine(targetFolder, fileName);

                    _downloadCts = new CancellationTokenSource();
                    var progress = new Progress<(int percent, string speed)>(
                        update => StatusText = $"Téléchargement uqload: {update.percent}% ({update.speed})");
                    await _downloadService.DownloadFileWithHeadersAsync(videoUrl, filePath, link, progress, _downloadCts.Token);

                    StatusText = "Téléchargement terminé !";
                    MessageBox.Show($"Vidéo uqload téléchargée avec succès !\n\n{filePath}", "Téléchargement terminé", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                // Vérifier si c'est un site de streaming (streamtape, youtube, etc.)
                else if (YtDlpService.IsStreamingSite(link))
                {
                    StatusText = "Site de streaming détecté, utilisation de yt-dlp...";

                    // Choisir le dossier de destination
                    string targetFolder;
                    if (!string.IsNullOrEmpty(_defaultDownloadFolder) && Directory.Exists(_defaultDownloadFolder))
                    {
                        targetFolder = _defaultDownloadFolder;
                    }
                    else
                    {
                        var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                        {
                            Description = "Choisissez le dossier de destination",
                            SelectedPath = !string.IsNullOrEmpty(_lastDownloadFolder)
                                ? _lastDownloadFolder
                                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                        };

                        if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                            return;

                        targetFolder = folderDialog.SelectedPath;
                    }

                    _lastDownloadFolder = targetFolder;
                    _downloadCts = new CancellationTokenSource();

                    var progress = new Progress<string>(msg => StatusText = msg);
                    var downloadedFile = await YtDlpService.DownloadVideoAsync(link, targetFolder, progress, _downloadCts.Token);

                    StatusText = "Téléchargement terminé !";
                    MessageBox.Show($"Vidéo téléchargée avec succès !\n\n{downloadedFile}", "Téléchargement terminé", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Logique normale pour les liens directs
                    string finalLink = link;

                    // Tenter de débridder, sinon utiliser le lien direct
                    try
                    {
                        StatusText = "Débridage du lien...";
                        var allDebridService = new AllDebridService(_allDebridApiKey);
                        finalLink = await allDebridService.UnlockLinkAsync(link);
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("unsupported") || ex.Message.Contains("not supported"))
                        {
                            StatusText = "Hébergeur non supporté, téléchargement direct...";
                            finalLink = link;
                        }
                        else
                        {
                            throw;
                        }
                    }

                    StatusText = "Récupération du nom du fichier...";
                    string suggestedFileName = await _downloadService.GetSuggestedFileNameAsync(finalLink)
                        ?? Path.GetFileName(new Uri(link).AbsolutePath);

                    var saveDialog = new SaveFileDialog
                    {
                        FileName = SanitizeFileName(suggestedFileName),
                        Filter = "Tous les fichiers (*.*)|*.*",
                        InitialDirectory = !string.IsNullOrEmpty(_defaultDownloadFolder) && Directory.Exists(_defaultDownloadFolder)
                            ? _defaultDownloadFolder
                            : (!string.IsNullOrEmpty(_lastDownloadFolder)
                                ? _lastDownloadFolder
                                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
                    };

                    if (saveDialog.ShowDialog() == true)
                    {
                        _lastDownloadFolder = Path.GetDirectoryName(saveDialog.FileName) ?? "";
                        _downloadCts = new CancellationTokenSource();
                        var progress = new Progress<(int percent, string speed)>(
                            update => StatusText = $"Téléchargement : {update.percent}% ({update.speed})");
                        await _downloadService.DownloadFileAsync(finalLink, saveDialog.FileName, progress, _downloadCts.Token);
                        StatusText = "Téléchargement terminé !";
                        MessageBox.Show($"Fichier téléchargé avec succès !\n\n{saveDialog.FileName}", "Téléchargement terminé", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Erreur: {ex.Message}";
                MessageBox.Show($"Erreur lors du téléchargement:\n\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void BatchImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_allDebridApiKey))
            {
                MessageBox.Show("Veuillez configurer votre clé API AllDebrid dans les paramètres (icône ⚙️).",
                    "Configuration requise", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetFolder;
            if (!string.IsNullOrWhiteSpace(_defaultDownloadFolder) && Directory.Exists(_defaultDownloadFolder))
            {
                targetFolder = _defaultDownloadFolder;
            }
            else
            {
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Sélectionnez le dossier de destination pour les films",
                    SelectedPath = !string.IsNullOrEmpty(_lastDownloadFolder)
                        ? _lastDownloadFolder
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                targetFolder = folderDialog.SelectedPath;
            }

            // Fenêtre modale avec TextBox multiligne
            var inputWindow = new Window
            {
                Title = "Import Liste de Films",
                Width = 600,
                Height = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30))
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "Collez la liste de films (un par ligne):\nExemple: Interstellar (2014)",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };

            var textBox = new TextBox
            {
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 13
            };

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "Importer & Télécharger", Width = 160, Margin = new Thickness(0, 0, 5, 0) };
            var cancelButton = new Button { Content = "Annuler", Width = 100 };

            okButton.Click += (s, ev) => { inputWindow.DialogResult = true; inputWindow.Close(); };
            cancelButton.Click += (s, ev) => { inputWindow.DialogResult = false; inputWindow.Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(label, 0);
            Grid.SetRow(textBox, 1);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            inputWindow.Content = grid;

            if (inputWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(textBox.Text))
                return;

            // Parser la liste
            var filmNames = textBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .ToList();

            if (!filmNames.Any())
            {
                MessageBox.Show("La liste est vide.", "Aucun film", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Confirmation
            var confirmMessage = $"Rechercher et télécharger {filmNames.Count} film(s) ?\n\n" +
                                 $"📁 Destination : {targetFolder}\n\n" +
                                 string.Join("\n", filmNames.Take(10).Select(f => "• " + f));
            if (filmNames.Count > 10)
                confirmMessage += $"\n... et {filmNames.Count - 10} autre(s)";

            if (MessageBox.Show(confirmMessage, "Confirmer l'import", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            await ProcessBatchImportAsync(filmNames, targetFolder);
        }

        private async Task ProcessBatchImportAsync(List<string> filmNames, string targetFolder)
        {
            try
            {
                // Create queue service if needed
                if (_queueService == null)
                {
                    _queueService = new DownloadQueueService(_allDebridApiKey, _downloadDelay);
                    _queueService.StatusChanged += (s, status) => Dispatcher.Invoke(() =>
                    {
                        StatusText = status;
                        if (_queueService != null)
                        {
                            var stats = _queueService.GetQueueStats();
                            QueueStatus = $"File: {stats.completed}/{stats.total} terminés ({stats.pending} en attente)";
                        }
                    });
                    _queueService.QueueCompleted += OnQueueCompleted;
                    _queueService.DownloadFailed += OnDownloadFailed;
                    DownloadQueue.Clear();
                }

                var downloads = new List<(string Name, string Link, string FilePath, List<(string HostName, string Link)> AlternativeLinks)>();
                var notFound = new List<string>();
                var cts = new CancellationTokenSource();

                for (int i = 0; i < filmNames.Count; i++)
                {
                    var filmName = filmNames[i];
                    StatusText = $"Recherche {i + 1}/{filmNames.Count}: {filmName}...";

                    try
                    {
                        // Rechercher le film
                        var (results, _) = await _searchService.SearchAsync(filmName, "films", 1, cts.Token);

                        if (!results.Any())
                        {
                            notFound.Add(filmName);
                            continue;
                        }

                        // Prendre le premier résultat
                        var result = results.First();

                        // Extraire les liens
                        StatusText = $"Extraction des liens {i + 1}/{filmNames.Count}: {filmName}...";
                        var hostGroups = await _linkExtractor.ExtractDownloadLinksAsync(result.Link, cts.Token);

                        if (!hostGroups.Any() || !hostGroups.Any(g => g.Links.Any()))
                        {
                            notFound.Add(filmName);
                            continue;
                        }

                        // Trouver le premier lien disponible, prioriser 1fichier
                        var orderedGroups = hostGroups
                            .OrderByDescending(g => g.HostName.Contains("1fichier", StringComparison.OrdinalIgnoreCase))
                            .ThenBy(g => g.HostName)
                            .ToList();

                        var firstLink = orderedGroups.First().Links.First();

                        // Collecter les liens alternatifs (même fichier sur différents hosters)
                        var alternativeLinks = new List<(string HostName, string Link)>();
                        foreach (var group in orderedGroups)
                        {
                            if (group.Links.Any())
                            {
                                alternativeLinks.Add((group.HostName, group.Links.First().Link));
                            }
                        }

                        // Construire le chemin: targetFolder/Film/NomDuFilm (Année)/fichier
                        string fileName = SanitizeFileName(firstLink.Name.Trim());
                        var parsedInfo = FileOrganizerService.ParseFileName(fileName);

                        // Utiliser le titre du résultat de recherche pour le nom du dossier
                        string folderName = SanitizeFileName(result.Title.Trim());
                        string filmFolder = Path.Combine(targetFolder, "Film", folderName);
                        Directory.CreateDirectory(filmFolder);

                        string filePath = Path.Combine(filmFolder, fileName);

                        downloads.Add((firstLink.Name, firstLink.Link, filePath, alternativeLinks));
                    }
                    catch (Exception ex)
                    {
                        notFound.Add($"{filmName} (Erreur: {ex.Message})");
                    }

                    // Petit délai entre les recherches pour ne pas spam le site
                    await Task.Delay(500);
                }

                if (!downloads.Any())
                {
                    MessageBox.Show("Aucun film trouvé dans la liste.", "Aucun résultat", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Afficher les films non trouvés
                if (notFound.Any())
                {
                    MessageBox.Show(
                        $"{downloads.Count} film(s) trouvé(s), {notFound.Count} non trouvé(s):\n\n" +
                        string.Join("\n", notFound.Select(f => "• " + f)),
                        "Résultat de la recherche",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                // Ajouter à la file de téléchargement
                _queueService.EnqueueDownloads(downloads);

                var existingItems = DownloadQueue.ToList();
                foreach (var queueItem in _queueService.QueueItems)
                {
                    if (!existingItems.Contains(queueItem))
                    {
                        DownloadQueue.Add(queueItem);
                    }
                }

                IsQueueActive = true;
                var queueStats = _queueService.GetQueueStats();
                QueueStatus = $"File: {queueStats.completed}/{queueStats.total} terminés";

                StatusText = $"{downloads.Count} film(s) ajouté(s) à la file de téléchargement";

                if (!_queueService.IsRunning)
                {
                    await _queueService.ProcessQueueAsync();
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Erreur: {ex.Message}";
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PerformSearch()
        {
            _currentPage = 1;
            ExecuteSearchAsync(isNewSearch: true);
        }

        private async Task ExecuteSearchAsync(bool isNewSearch)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            StatusText = "Recherche en cours...";
            SearchResults.Clear();
            HasSearchResults = false;
            if (isNewSearch)
            {
                DownloadLinks.Clear();
                Hosters.Clear();
            }

            try
            {
                var (rawResults, totalPages) = await _searchService.SearchAsync(SearchQuery, SelectedCategory, _currentPage, token);

                if (token.IsCancellationRequested) return;

                // Grouper les résultats pour les séries/animes
                var processed = SearchResultProcessor.ProcessResults(rawResults, SelectedCategory, VFOnlyFilter);

                if (isNewSearch) _totalPages = totalPages;

                // Ajouter les groupes de séries
                foreach (var group in processed.SeriesGroups)
                {
                    SearchResults.Add(group);
                }

                // Ajouter les résultats individuels
                foreach (var result in processed.UngroupedResults)
                {
                    SearchResults.Add(result);
                }

                HasSearchResults = SearchResults.Count > 0;

                int groupCount = processed.SeriesGroups.Count;
                int individualCount = processed.UngroupedResults.Count;
                StatusText = $"{groupCount + individualCount} résultats (dont {groupCount} séries groupées) sur la page {_currentPage}.";

                OnPropertyChanged(nameof(PageInfo));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));

                await LoadImagesAsync(token);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Recherche annulée.";
            }
            catch (Exception ex)
            {
                StatusText = $"Erreur: {ex.Message}";
            }
        }

        private async Task LoadImagesAsync(CancellationToken ct)
        {
            var tasks = SearchResults.Select(async item =>
            {
                // Handle both SearchResult and SeriesGroup
                if (item is SearchResult result)
                {
                    if (string.IsNullOrEmpty(result.ImageUrl)) return;
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        var imageData = await HttpService.GetByteArrayAsync(result.ImageUrl, ct);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = new MemoryStream(imageData);
                        bitmap.DecodePixelWidth = 120;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        Dispatcher.Invoke(() => result.Image = bitmap);
                    }
                    catch (OperationCanceledException) { /* Ignore les images annulées */ }
                    catch { /* Ignore image errors */ }
                }
                // SeriesGroup images are loaded from the first result's ImageUrl
                else if (item is SeriesGroup group)
                {
                    if (string.IsNullOrEmpty(group.Image)) return;
                    if (ct.IsCancellationRequested) return;

                    // No need to load image for SeriesGroup as it uses ImageUrl directly
                    // The XAML will handle image loading via binding
                }
            });
            await Task.WhenAll(tasks);
        }

        private void LoadEpisodesForHoster(string? hosterName)
        {
            DownloadLinks.Clear();
            Seasons.Clear();
            HasSeasons = false;

            if (string.IsNullOrEmpty(hosterName)) return;

            // Include separators and headers along with the selected hoster
            var groupsForHoster = _allDownloadLinks
                .Where(g => g.HostName == hosterName || g.HostName.StartsWith("──"))
                .ToList();
            if (!groupsForHoster.Any()) return;

            // Déterminer si on doit afficher les saisons
            bool shouldShowSeasons = SelectedCategory == "series" || SelectedCategory == "mangas";

            if (shouldShowSeasons)
            {
                // Grouper par saison
                var seasonGroups = GroupBySeason(groupsForHoster);

                if (seasonGroups.Count > 1 || (seasonGroups.Count == 1 && seasonGroups[0].Number > 0))
                {
                    // Afficher la navigation par saisons
                    HasSeasons = true;
                    foreach (var season in seasonGroups.OrderBy(s => s.Number))
                    {
                        Seasons.Add(season);
                    }
                    EpisodeListHeader = "Épisodes: (sélectionnez une ou plusieurs saisons)";
                    return;
                }
            }

            // Pas de saisons ou films: afficher directement
            LoadAllEpisodes(groupsForHoster);
        }

        private List<Season> GroupBySeason(List<HostLinkGroup> groups)
        {
            var seasonDict = new Dictionary<int, Season>();

            foreach (var group in groups)
            {
                foreach (var link in group.Links)
                {
                    if (link.Link == "SEPARATOR") continue;

                    var contentInfo = FileOrganizerService.ParseFileName(link.Name);
                    int seasonNumber = contentInfo.Season ?? 0;

                    if (!seasonDict.ContainsKey(seasonNumber))
                    {
                        seasonDict[seasonNumber] = new Season { Number = seasonNumber };
                    }

                    seasonDict[seasonNumber].Episodes.Add(link);
                }
            }

            return seasonDict.Values.ToList();
        }

        private void LoadAllEpisodes(List<HostLinkGroup> groupsForHoster)
        {
            // Essayer de grouper par saison pour l'organisation
            var seasonGroups = GroupBySeason(groupsForHoster);

            // Si plusieurs saisons détectées, afficher avec séparateurs
            if (seasonGroups.Count > 1)
            {
                bool isFirst = true;
                int totalEpisodes = 0;

                foreach (var season in seasonGroups.OrderBy(s => s.Number))
                {
                    // Séparateur entre les saisons
                    if (!isFirst)
                    {
                        DownloadLinks.Add(new DownloadLink { Name = "═══════════════════════════════════", Link = "SEPARATOR" });
                    }

                    // Header de la saison
                    DownloadLinks.Add(new DownloadLink
                    {
                        Name = $"📁 {season.DisplayName} ({season.EpisodeCount} épisodes)",
                        Link = "SEPARATOR"
                    });

                    // Ajouter les épisodes de cette saison
                    foreach (var episode in season.Episodes)
                    {
                        if (!episode.Name.StartsWith("[Premium]") && groupsForHoster.Any(g => g.Type.Contains("Premium")))
                        {
                            episode.Name = $"[Premium] {episode.Name}";
                        }
                        DownloadLinks.Add(episode);
                        if (episode.Link != "SEPARATOR") totalEpisodes++;
                    }

                    isFirst = false;
                }

                EpisodeListHeader = $"Épisodes de {seasonGroups.Count} saisons: ({totalEpisodes})";
            }
            else
            {
                // Pas de saisons multiples, affichage classique
                var premiumGroup = groupsForHoster.FirstOrDefault(g => g.Type.Contains("Premium"));
                if (premiumGroup != null)
                {
                    foreach (var link in premiumGroup.Links)
                    {
                        if (!link.Name.StartsWith("[Premium]"))
                        {
                            link.Name = $"[Premium] {link.Name}";
                        }
                        DownloadLinks.Add(link);
                    }
                }

                var otherGroups = groupsForHoster.Where(g => !g.Type.Contains("Premium")).ToList();
                if (premiumGroup != null && otherGroups.Any())
                {
                    DownloadLinks.Add(new DownloadLink { Name = "────────── Parties Multiples ──────────", Link = "SEPARATOR" });
                }

                foreach (var group in otherGroups)
                {
                    foreach (var link in group.Links) DownloadLinks.Add(link);
                }

                int totalEpisodes = DownloadLinks.Count(l => l.Link != "SEPARATOR");
                EpisodeListHeader = $"Épisodes: ({totalEpisodes} disponibles)";
            }
        }

        private void SeasonListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DownloadLinks.Clear();

            if (SeasonListBox.SelectedItems.Count == 0)
            {
                EpisodeListHeader = "Épisodes: (sélectionnez une ou plusieurs saisons)";
                return;
            }

            // Récupérer tous les épisodes des saisons sélectionnées
            var selectedSeasons = SeasonListBox.SelectedItems.Cast<Season>().ToList();

            // Afficher avec séparateurs entre les saisons
            bool isFirst = true;
            int totalEpisodes = 0;

            foreach (var season in selectedSeasons.OrderBy(s => s.Number))
            {
                // Ajouter un séparateur visuel pour chaque saison (sauf la première)
                if (!isFirst && season.Episodes.Any())
                {
                    DownloadLinks.Add(new DownloadLink
                    {
                        Name = "═══════════════════════════════════",
                        Link = "SEPARATOR"
                    });
                }

                // Ajouter le header de la saison
                if (selectedSeasons.Count > 1)
                {
                    DownloadLinks.Add(new DownloadLink
                    {
                        Name = $"📁 {season.DisplayName} ({season.EpisodeCount} épisodes)",
                        Link = "SEPARATOR"
                    });
                }

                // Ajouter tous les épisodes de cette saison
                foreach (var episode in season.Episodes)
                {
                    DownloadLinks.Add(episode);
                    if (episode.Link != "SEPARATOR") totalEpisodes++;
                }

                isFirst = false;
            }

            if (selectedSeasons.Count == 1)
            {
                EpisodeListHeader = $"Épisodes de {selectedSeasons[0].DisplayName}: ({totalEpisodes})";
            }
            else
            {
                EpisodeListHeader = $"Épisodes de {selectedSeasons.Count} saisons: ({totalEpisodes})";
            }
        }
        
        private string SanitizeFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), "_"));
        }

        // --- Persistence des paramètres ---
        
        private static string GetSettingsPath()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZTHubApp");
            Directory.CreateDirectory(appDataPath);
            return Path.Combine(appDataPath, "settings.json");
        }
        
        private string GetBaseUrl()
        {
            try
            {
                var settingsPath = GetSettingsPath();
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (settings != null && settings.TryGetValue("BaseUrl", out var url) && !string.IsNullOrWhiteSpace(url))
                    {
                        return url;
                    }
                }
            }
            catch {}
            return "https://www.zone-telechargement.cv"; 
        }

        private void LoadSettings()
        {
            try
            {
                var settingsPath = GetSettingsPath();
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (settings != null)
                    {
                        if (settings.TryGetValue("AllDebridApiKey", out var apiKey))
                            _allDebridApiKey = apiKey;

                        if (settings.TryGetValue("DownloadDelay", out var delay) && int.TryParse(delay, out var delayValue))
                            _downloadDelay = delayValue;

                        if (settings.TryGetValue("DefaultDownloadFolder", out var defaultFolder))
                            _defaultDownloadFolder = defaultFolder;
                    }
                }
            }
            catch { /* */ }
        }

        private void SaveSettings(string baseUrl)
        {
            try
            {
                var settings = new Dictionary<string, string>
                {
                    ["AllDebridApiKey"] = _allDebridApiKey,
                    ["BaseUrl"] = baseUrl,
                    ["DownloadDelay"] = _downloadDelay.ToString(),
                    ["DefaultDownloadFolder"] = _defaultDownloadFolder
                };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetSettingsPath(), json);
            }
            catch { /* */ }
        }
    }
}