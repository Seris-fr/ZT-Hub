using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ZTHubApp.Services
{
    public class ScannedMedia
    {
        public string ContentName { get; set; } = string.Empty;
        public string NormalizedName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Film, Serie, Anime
        public List<int> Seasons { get; set; } = new List<int>();
        public string FullPath { get; set; } = string.Empty;
    }

    public class MediaScannerService
    {
        private readonly Dictionary<string, ScannedMedia> _scannedContent = new();
        private readonly Regex _seasonFolderPattern = new(@"Saison\s*(\d{1,2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Scanne les dossiers Film/, Serie/, Anime/ pour détecter les médias existants
        /// </summary>
        public async Task<Dictionary<string, ScannedMedia>> ScanMediaFoldersAsync(string baseFolder)
        {
            _scannedContent.Clear();

            if (string.IsNullOrEmpty(baseFolder) || !Directory.Exists(baseFolder))
                return _scannedContent;

            await Task.Run(() =>
            {
                // Scanner chaque type de dossier
                ScanTypeFolder(Path.Combine(baseFolder, "Film"), "Film");
                ScanTypeFolder(Path.Combine(baseFolder, "Serie"), "Serie");
                ScanTypeFolder(Path.Combine(baseFolder, "Anime"), "Anime");
            });

            return _scannedContent;
        }

        /// <summary>
        /// Scanne un dossier de type (Film, Serie, Anime)
        /// </summary>
        private void ScanTypeFolder(string typeFolder, string type)
        {
            if (!Directory.Exists(typeFolder))
                return;

            try
            {
                // Lister tous les sous-dossiers (chaque sous-dossier = un contenu)
                var contentFolders = Directory.GetDirectories(typeFolder);

                foreach (var contentFolder in contentFolders)
                {
                    var contentName = Path.GetFileName(contentFolder);
                    var normalizedName = NormalizeName(contentName);

                    var scanned = new ScannedMedia
                    {
                        ContentName = contentName,
                        NormalizedName = normalizedName,
                        Type = type,
                        FullPath = contentFolder
                    };

                    // Si c'est une série ou anime, scanner les saisons
                    if (type == "Serie" || type == "Anime")
                    {
                        scanned.Seasons = ScanSeasons(contentFolder);
                    }

                    // Utiliser le nom normalisé comme clé pour faciliter la recherche
                    var key = $"{type}_{normalizedName}".ToLowerInvariant();
                    _scannedContent[key] = scanned;
                }
            }
            catch (Exception ex)
            {
                // Ignorer les erreurs de scan (permissions, etc.)
                System.Diagnostics.Debug.WriteLine($"Erreur scan {typeFolder}: {ex.Message}");
            }
        }

        /// <summary>
        /// Scanne les dossiers Saison XX dans un dossier de série
        /// </summary>
        private List<int> ScanSeasons(string seriesFolder)
        {
            var seasons = new List<int>();

            try
            {
                var subFolders = Directory.GetDirectories(seriesFolder);

                foreach (var folder in subFolders)
                {
                    var folderName = Path.GetFileName(folder);
                    var match = _seasonFolderPattern.Match(folderName);

                    if (match.Success && int.TryParse(match.Groups[1].Value, out int seasonNum))
                    {
                        seasons.Add(seasonNum);
                    }
                }
            }
            catch
            {
                // Ignorer les erreurs
            }

            return seasons.OrderBy(s => s).ToList();
        }

        /// <summary>
        /// Normalise un nom pour la comparaison
        /// Enlève caractères spéciaux, met en minuscules, enlève espaces multiples
        /// </summary>
        private string NormalizeName(string name)
        {
            // Remplacer underscores et tirets par espaces
            var normalized = name.Replace('_', ' ').Replace('-', ' ');

            // Enlever caractères spéciaux (garder lettres, chiffres, espaces)
            normalized = Regex.Replace(normalized, @"[^\w\s]", "");

            // Enlever espaces multiples
            normalized = Regex.Replace(normalized, @"\s+", " ");

            return normalized.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Vérifie si une saison d'une série est déjà téléchargée
        /// </summary>
        public bool IsSeasonDownloaded(string contentName, int seasonNumber, string type = "Serie")
        {
            var normalizedName = NormalizeName(contentName);
            var key = $"{type}_{normalizedName}".ToLowerInvariant();

            if (_scannedContent.TryGetValue(key, out var media))
            {
                return media.Seasons.Contains(seasonNumber);
            }

            return false;
        }

        /// <summary>
        /// Retourne la liste des saisons manquantes
        /// </summary>
        public List<int> GetMissingSeasons(string contentName, List<int> availableSeasons, string type = "Serie")
        {
            var normalizedName = NormalizeName(contentName);
            var key = $"{type}_{normalizedName}".ToLowerInvariant();

            if (_scannedContent.TryGetValue(key, out var media))
            {
                return availableSeasons.Except(media.Seasons).ToList();
            }

            // Rien de téléchargé, toutes les saisons sont manquantes
            return availableSeasons.ToList();
        }

        /// <summary>
        /// Vérifie si un film est déjà téléchargé
        /// </summary>
        public bool IsFilmDownloaded(string contentName)
        {
            var normalizedName = NormalizeName(contentName);
            var key = $"Film_{normalizedName}".ToLowerInvariant();

            return _scannedContent.ContainsKey(key);
        }

        /// <summary>
        /// Obtient le nombre total de médias scannés
        /// </summary>
        public int GetTotalScannedCount()
        {
            return _scannedContent.Count;
        }

        /// <summary>
        /// Obtient des statistiques de scan
        /// </summary>
        public string GetScanStats()
        {
            var films = _scannedContent.Values.Count(m => m.Type == "Film");
            var series = _scannedContent.Values.Count(m => m.Type == "Serie");
            var animes = _scannedContent.Values.Count(m => m.Type == "Anime");

            return $"{films} films, {series} séries, {animes} animes";
        }
    }
}
