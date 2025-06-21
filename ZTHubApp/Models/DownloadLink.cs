namespace ZTHubApp.Models
{
    public class DownloadLink
    {
        public string Name { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public override string ToString() => Name;
    }
}