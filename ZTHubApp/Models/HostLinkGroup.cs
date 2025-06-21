using System.Collections.Generic;

namespace ZTHubApp.Models
{
    public class HostLinkGroup
    {
        public string HostName { get; set; } = string.Empty;
        public string Type { get; set; } = "Général";
        public List<DownloadLink> Links { get; set; } = new List<DownloadLink>();
    }
}