using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace ZTHubApp.Services
{
    public static class UqloadService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        static UqloadService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        /// <summary>
        /// Vérifie si l'URL est un lien uqload
        /// </summary>
        public static bool IsUqloadUrl(string url)
        {
            return url.Contains("uqload.is", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("uqload.com", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("uqload.co", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extrait l'URL directe de la vidéo depuis une page uqload
        /// </summary>
        public static async Task<string> ExtractVideoUrlAsync(string pageUrl)
        {
            try
            {
                // Télécharger la page HTML
                var html = await _httpClient.GetStringAsync(pageUrl);

                // Méthode 1: Chercher les patterns JavaScript communs
                var patterns = new[]
                {
                    @"sources:\s*\[\s*\{\s*src:\s*['""]([^'""]+)['""]",
                    @"file:\s*['""]([^'""]+\.mp4[^'""]*)['""]",
                    @"src:\s*['""]([^'""]+\.mp4[^'""]*)['""]",
                    @"video_url\s*=\s*['""]([^'""]+)['""]",
                    @"source\s+src=['""]([^'""]+\.mp4[^'""]*)['""]"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var videoUrl = match.Groups[1].Value;
                        if (!string.IsNullOrEmpty(videoUrl) &&
                            (videoUrl.StartsWith("http") || videoUrl.StartsWith("//")))
                        {
                            if (videoUrl.StartsWith("//"))
                                videoUrl = "https:" + videoUrl;
                            return videoUrl;
                        }
                    }
                }

                // Méthode 2: Parser le HTML avec HtmlAgilityPack
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Chercher la balise video
                var videoNode = doc.DocumentNode.SelectSingleNode("//video[@id='player_html5_api']//source");
                if (videoNode != null)
                {
                    var src = videoNode.GetAttributeValue("src", "");
                    if (!string.IsNullOrEmpty(src))
                    {
                        if (src.StartsWith("//"))
                            src = "https:" + src;
                        return src;
                    }
                }

                // Méthode 3: Chercher dans les scripts
                var scripts = doc.DocumentNode.SelectNodes("//script[not(@src)]");
                if (scripts != null)
                {
                    foreach (var script in scripts)
                    {
                        var scriptContent = script.InnerText;

                        // Chercher eval(function(p,a,c,k,e,d)
                        if (scriptContent.Contains("eval(function(p,a,c,k,e,d)"))
                        {
                            // Pattern pour extraire l'URL encodée
                            var evalMatch = Regex.Match(scriptContent, @"sources.*?(\bhttps?://[^'""<>\s]+\.mp4[^'""<>\s]*)", RegexOptions.IgnoreCase);
                            if (evalMatch.Success && evalMatch.Groups.Count > 1)
                            {
                                return evalMatch.Groups[1].Value;
                            }
                        }

                        // Chercher directement les URLs mp4
                        var urlMatch = Regex.Match(scriptContent, @"(https?://[^'""<>\s]+\.mp4[^'""<>\s]*)", RegexOptions.IgnoreCase);
                        if (urlMatch.Success)
                        {
                            return urlMatch.Groups[1].Value;
                        }
                    }
                }

                throw new Exception("Impossible de trouver l'URL de la vidéo sur la page uqload. Le site a peut-être changé de structure.");
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Erreur lors du téléchargement de la page: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Erreur lors de l'extraction de l'URL: {ex.Message}");
            }
        }
    }
}
