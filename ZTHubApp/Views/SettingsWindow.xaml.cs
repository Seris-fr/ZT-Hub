using System.Windows;

namespace ZTHubApp.Views
{
    public partial class SettingsWindow : Window
    {
        public string AllDebridApiKey { get; private set; }
        public string BaseUrl { get; private set; }

        public SettingsWindow(string currentApiKey, string currentBaseUrl)
        {
            InitializeComponent();
            ApiKeyBox.Text = currentApiKey;
            UrlBox.Text = currentBaseUrl;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            AllDebridApiKey = ApiKeyBox.Text;
            BaseUrl = UrlBox.Text;
            DialogResult = true;
            Close();
        }
    }
}