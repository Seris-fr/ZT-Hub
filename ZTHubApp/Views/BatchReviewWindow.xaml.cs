using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using ZTHubApp.Models;
using ZTHubApp.Services;

namespace ZTHubApp.Views
{
    public partial class BatchReviewWindow : Window
    {
        private readonly List<BatchReviewItem> _items;
        private readonly CancellationTokenSource _imageCts = new();

        public List<BatchReviewItem> ConfirmedItems =>
            _items.Where(i => !i.Skip && i.SelectedResult != null).ToList();

        public BatchReviewWindow(List<BatchReviewItem> items)
        {
            InitializeComponent();
            _items = items;
            ReviewItemsControl.ItemsSource = _items;

            int found = items.Count(i => i.HasResults);
            int notFound = items.Count - found;
            HeaderText.Text = $"Vérification — {found} film(s) trouvé(s)" +
                              (notFound > 0 ? $", {notFound} non trouvé(s)" : "");

            ConfirmButton.Content = $"✓ Télécharger {found} film(s)";

            Loaded += async (s, e) => await LoadImagesAsync();
            Closed += (s, e) => _imageCts.Cancel();
        }

        private async Task LoadImagesAsync()
        {
            int loaded = 0;
            int total = _items.Count(i => i.HasResults);

            var tasks = _items
                .Where(i => i.HasResults)
                .Select(async item =>
                {
                    // Charger les images de tous les résultats de cet item
                    foreach (var result in item.AllResults)
                    {
                        if (string.IsNullOrEmpty(result.ImageUrl) || result.Image != null)
                            continue;

                        try
                        {
                            var imageData = await HttpService.GetByteArrayAsync(result.ImageUrl, _imageCts.Token);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = new MemoryStream(imageData);
                            bitmap.DecodePixelWidth = 80;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            Dispatcher.Invoke(() => result.Image = bitmap);
                        }
                        catch { /* Ignorer les erreurs d'image */ }
                    }

                    Interlocked.Increment(ref loaded);
                    Dispatcher.Invoke(() =>
                        StatusText.Text = $"Images : {loaded}/{total} chargées");
                });

            await Task.WhenAll(tasks);

            if (!_imageCts.Token.IsCancellationRequested)
                StatusText.Text = $"Prêt — vérifiez les résultats puis cliquez sur Confirmer";
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmedItems.Any())
            {
                MessageBox.Show(
                    "Aucun film sélectionné. Décochez 'Ignorer' sur au moins un film.",
                    "Aucune sélection",
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

    public class BatchReviewItem : INotifyPropertyChanged
    {
        public string SearchedName { get; set; } = string.Empty;
        public List<SearchResult> AllResults { get; set; } = new();
        public bool HasResults => AllResults.Any();

        private SearchResult? _selectedResult;
        public SearchResult? SelectedResult
        {
            get => _selectedResult;
            set
            {
                _selectedResult = value;
                OnPropertyChanged();
            }
        }

        private bool _skip;
        public bool Skip
        {
            get => _skip;
            set
            {
                _skip = value;
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
