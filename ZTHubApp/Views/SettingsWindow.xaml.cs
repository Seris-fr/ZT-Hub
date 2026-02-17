using System.Windows;

namespace ZTHubApp.Views
{
    public partial class SettingsWindow : Window
    {
        public string AllDebridApiKey { get; private set; }
        public string BaseUrl { get; private set; }
        public int DownloadDelay { get; private set; }

        public SettingsWindow(string currentApiKey, string currentBaseUrl, int downloadDelay)
        {
            InitializeComponent();
            ApiKeyBox.Text = currentApiKey;
            UrlBox.Text = currentBaseUrl;
            DelayBox.Text = downloadDelay.ToString();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            AllDebridApiKey = ApiKeyBox.Text;
            BaseUrl = UrlBox.Text;

            if (int.TryParse(DelayBox.Text, out var delay) && delay >= 0)
                DownloadDelay = delay;
            else
                DownloadDelay = 10; // Default

            DialogResult = true;
            Close();
        }
    }
}