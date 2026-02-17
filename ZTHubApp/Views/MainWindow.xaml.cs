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

        public ObservableCollection<SearchResult> SearchResults { get; } = new();
        public ObservableCollection<string> Hosters { get; } = new();
        public ObservableCollection<DownloadLink> DownloadLinks { get; } = new();
        public ObservableCollection<DownloadQueueItem> DownloadQueue { get; } = new();

        private string _searchQuery = "";
        public string SearchQuery { get => _searchQuery; set { _searchQuery = value; OnPropertyChanged(); } }

        private string _selectedCategory = "mangas";
        public string SelectedCategory { get => _selectedCategory; set { _selectedCategory = value; OnPropertyChanged(); } }

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
            if (e.AddedItems.Count == 0 || !(e.AddedItems[0] is SearchResult selectedResult)) return;

            
            _linkExtractionCts?.Cancel();
            
            _linkExtractionCts = new CancellationTokenSource();
            var token = _linkExtractionCts.Token;

            StatusText = "Récupération des liens...";
            DownloadLinks.Clear();
            Hosters.Clear();
            _allDownloadLinks.Clear();

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
            // Ask user for folder once
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = $"Sélectionnez le dossier de destination pour {selectedItems.Count} fichiers",
                SelectedPath = !string.IsNullOrEmpty(_lastDownloadFolder)
                    ? _lastDownloadFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string targetFolder = folderDialog.SelectedPath;
            _lastDownloadFolder = targetFolder;

            try
            {
                StatusText = "Préparation de la file de téléchargement...";

                // Create queue service
                _queueService = new DownloadQueueService(_allDebridApiKey, _downloadDelay);
                _queueService.StatusChanged += (s, status) => Dispatcher.Invoke(() => StatusText = status);
                _queueService.QueueCompleted += OnQueueCompleted;

                DownloadQueue.Clear();

                // Prepare download items
                var downloads = new List<(string Name, string Link, string FilePath)>();

                foreach (var item in selectedItems)
                {
                    string fileName = SanitizeFileName(item.Name.Trim());
                    string filePath = Path.Combine(targetFolder, fileName);
                    downloads.Add((item.Name, item.Link, filePath));
                }

                // Enqueue all downloads
                _queueService.EnqueueDownloads(downloads);

                // Bind queue to UI
                foreach (var queueItem in _queueService.QueueItems)
                {
                    DownloadQueue.Add(queueItem);
                }

                IsQueueActive = true;
                var stats = _queueService.GetQueueStats();
                QueueStatus = $"File: {stats.completed}/{stats.total} terminés";

                // Start processing
                await _queueService.ProcessQueueAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Erreur de file d'attente: {ex.Message}";
                MessageBox.Show(ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsWindow(_allDebridApiKey, GetBaseUrl(), _downloadDelay) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _allDebridApiKey = dialog.AllDebridApiKey;
                _downloadDelay = dialog.DownloadDelay;
                SaveSettings(dialog.BaseUrl);
                _searchService = new SearchService(dialog.BaseUrl);
                StatusText = "Paramètres sauvegardés.";
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
            if (isNewSearch)
            {
                DownloadLinks.Clear();
                Hosters.Clear();
            }

            try
            {
                var (results, totalPages) = await _searchService.SearchAsync(SearchQuery, SelectedCategory, _currentPage, token);
                
                if (token.IsCancellationRequested) return;

                if (isNewSearch) _totalPages = totalPages;
                foreach (var result in results) SearchResults.Add(result);
                StatusText = $"{results.Count} résultats sur la page {_currentPage}.";
        
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
            var tasks = SearchResults.Select(async result =>
            {
                if (string.IsNullOrEmpty(result.ImageUrl)) return;
                
                if (ct.IsCancellationRequested) return;
                if (string.IsNullOrEmpty(result.ImageUrl)) return;
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
                catch { /*  */ }
            });
            await Task.WhenAll(tasks);
        }

        private void LoadEpisodesForHoster(string? hosterName)
        {
            DownloadLinks.Clear();
            if (string.IsNullOrEmpty(hosterName)) return;

            var groupsForHoster = _allDownloadLinks.Where(g => g.HostName == hosterName).ToList();
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
                    ["DownloadDelay"] = _downloadDelay.ToString()
                };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetSettingsPath(), json);
            }
            catch { /* */ }
        }
    }
}