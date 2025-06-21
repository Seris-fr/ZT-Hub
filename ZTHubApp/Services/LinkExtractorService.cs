using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using ZTHubApp.Models;

namespace ZTHubApp.Services
{
    public class LinkExtractorService
    {
        public async Task<List<HostLinkGroup>> ExtractDownloadLinksAsync(string url, CancellationToken ct)
        {
            try
            {
                
                string html = await HttpService.GetStringAsync(url, ct);
                ct.ThrowIfCancellationRequested(); 
                
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var postInfoDiv = doc.DocumentNode.SelectSingleNode("//div[@class='postinfo']");
                if (postInfoDiv == null) return new List<HostLinkGroup>();

                var allGroups = new List<HostLinkGroup>();
                string currentSection = "Général";
                HostLinkGroup? currentHostGroup = null;

                foreach (var node in postInfoDiv.ChildNodes)
                {
                    if (node.Name == "#text" && string.IsNullOrWhiteSpace(node.InnerText)) continue;
                    
                    if (node.Name == "font" && node.GetAttributeValue("color", "") == "red")
                    {
                        currentSection = node.InnerText.Trim();
                        currentHostGroup = null;
                        continue;
                    }

                    var hosterDiv = node.SelectSingleNode(".//div[contains(@style, 'font-weight:bold')]");
                    if (node.Name == "b" && hosterDiv != null)
                    {
                        string hosterName = hosterDiv.InnerText.Trim();
                        if (string.IsNullOrEmpty(hosterName) || hosterName.Contains("Mot de passe")) continue;

                        currentHostGroup = new HostLinkGroup { HostName = hosterName, Type = currentSection };
                        allGroups.Add(currentHostGroup);
                        continue;
                    }

                    HtmlNode? linkNode = (node.Name == "a") ? node : node.SelectSingleNode(".//a");
                    if (linkNode != null && currentHostGroup != null)
                    {
                        currentHostGroup.Links.Add(new DownloadLink
                        {
                            Name = linkNode.InnerText?.Trim() ?? "Lien sans nom",
                            Link = linkNode.GetAttributeValue("href", "")
                        });
                    }
                }

                return allGroups.Where(g => g.Links.Any()).ToList();
            }
            
            catch (OperationCanceledException)
            {
                throw; 
            }
            
            catch (Exception ex)
            {
                throw new Exception($"Erreur d'extraction des liens: {ex.Message}");
            }
        }
    }
}