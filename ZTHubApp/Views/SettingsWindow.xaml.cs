using System.Windows;

namespace ZTHubApp.Views
{
    public partial class SettingsWindow : Window
    {
        public string AllDebridApiKey { get; private set; }
        public string BaseUrl { get; private set; }
        public int DownloadDelay { get; private set; }
        public string DefaultDownloadFolder { get; private set; }

        public SettingsWindow(string currentApiKey, string currentBaseUrl, int downloadDelay, string defaultDownloadFolder)
        {
            InitializeComponent();
            ApiKeyBox.Text = currentApiKey;
            UrlBox.Text = currentBaseUrl;
            DelayBox.Text = downloadDelay.ToString();
            DefaultFolderBox.Text = defaultDownloadFolder;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            AllDebridApiKey = ApiKeyBox.Text;
            BaseUrl = UrlBox.Text;

            if (int.TryParse(DelayBox.Text, out var delay) && delay >= 0)
                DownloadDelay = delay;
            else
                DownloadDelay = 10; // Default

            DefaultDownloadFolder = DefaultFolderBox.Text;

            DialogResult = true;
            Close();
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Sélectionnez le dossier de téléchargement par défaut",
                SelectedPath = DefaultFolderBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                DefaultFolderBox.Text = dialog.SelectedPath;
            }
        }

        private void ClearFolder_Click(object sender, RoutedEventArgs e)
        {
            DefaultFolderBox.Text = string.Empty;
        }
    }
}