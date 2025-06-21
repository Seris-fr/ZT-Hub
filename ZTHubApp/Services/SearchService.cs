using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using ZTHubApp.Models;

namespace ZTHubApp.Services
{
    public class SearchService
    {
        private readonly string _baseUrl;

        public SearchService(string baseUrl = "https://www.zone-telechargement.cv")
        {
            _baseUrl = baseUrl;
        }

        public async Task<(List<SearchResult> results, int totalPages)> SearchAsync(string query, string category, int page, CancellationToken ct)
        {
            try
            {
                string url = $"{_baseUrl}?p={category}&search={Uri.EscapeDataString(query)}&page={page}";
                string html = await HttpService.GetStringAsync(url, ct);
                ct.ThrowIfCancellationRequested();

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var results = new List<SearchResult>();
                var coverDivs = doc.DocumentNode.SelectNodes("//div[@class='cover_global']") ?? new HtmlNodeCollection(null);

                foreach (var div in coverDivs)
                {
                    var linkNode = div.SelectSingleNode(".//a");
                    var imgNode = div.SelectSingleNode(".//img");
                    var titleNode = div.SelectSingleNode(".//div[@class='cover_infos_title']");

                    if (linkNode?.GetAttributeValue("href", "") is string link && !string.IsNullOrEmpty(link) &&
                        imgNode?.GetAttributeValue("src", "") is string imgUrl && !string.IsNullOrEmpty(imgUrl) &&
                        titleNode?.InnerText is string title && !string.IsNullOrEmpty(title))
                    {
                        results.Add(new SearchResult
                        {
                            Title = title.Trim(),
                            ImageUrl = imgUrl.StartsWith("http") ? imgUrl : new Uri(new Uri(_baseUrl), imgUrl).ToString(),
                            Link = link.StartsWith("http") ? link : new Uri(new Uri(_baseUrl), link).ToString()
                        });
                    }
                }
                
                int totalPages = GetTotalPages(doc);
                return (results, totalPages);
            }
            
            catch (OperationCanceledException)
            {
                
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception($"Erreur lors de la recherche: {ex.Message}");
            }
        }

        private int GetTotalPages(HtmlDocument doc)
        {
            var navDiv = doc.DocumentNode.SelectSingleNode("//div[@class='navigation']");
            if (navDiv == null) return 1;

            var pageLinks = navDiv.SelectNodes(".//a[@href]");
            if (pageLinks == null) return 1;

            return pageLinks
                .Select(link => Regex.Match(link.GetAttributeValue("href", ""), @"page=(\d+)"))
                .Where(match => match.Success)
                .Select(match => int.TryParse(match.Groups[1].Value, out int pageNum) ? pageNum : 1)
                .DefaultIfEmpty(1)
                .Max();
        }
    }
}