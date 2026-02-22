using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZTHubApp.Services
{
    public class ContentMetadata
    {
        public string CleanedName { get; set; } = string.Empty;
        public int? SeasonNumber { get; set; }
        public string Language { get; set; } = string.Empty;
        public List<string> LanguageTags { get; set; } = new List<string>();
        public bool IsSeries { get; set; }
        public string OriginalTitle { get; set; } = string.Empty;
    }

    public static class ContentMetadataExtractor
    {
        // Patterns pour détecter les saisons (ordonnés par priorité)
        private static readonly Regex SeasonPattern1 = new(
            @"[Ss](\\d{1,2})(?:[Ee]\\d{1,2})?",
            RegexOptions.Compiled
        );

        private static readonly Regex SeasonPattern2 = new(
            @"(\\d{1,2})[xX]\\d{1,2}",
            RegexOptions.Compiled
        );

        private static readonly Regex SeasonPattern3 = new(
            @"[Ss]aison\\s*(\\d{1,2})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        private static readonly Regex SeasonPattern4 = new(
            @"[Ss]eason\\s*(\\d{1,2})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        // Patterns pour les langues (ordre de priorité)
        private static readonly Dictionary<string, Regex> LanguagePatterns = new()
        {
            { "VF", new Regex(@"\b(VF|FRENCH|TRUEFRENCH|FR)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase) },
            { "VOSTFR", new Regex(@"\bVOSTFR\b", RegexOptions.Compiled | RegexOptions.IgnoreCase) },
            { "MULTI", new Regex(@"\bMULTI\b", RegexOptions.Compiled | RegexOptions.IgnoreCase) },
            { "VO", new Regex(@"\b(VO|VOEN)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase) }
        };

        // Patterns pour la qualité/format à supprimer
        private static readonly Regex QualityPattern = new(
            @"\b(1080p|720p|2160p|4K|HDTV|WEB-DL|BluRay|BDRip|WEBRip|PROPER|REPACK|x264|x265|HEVC|AAC|AC3|DTS|10bit)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// <summary>
        /// Extrait les métadonnées d'un titre de contenu
        /// </summary>
        public static ContentMetadata ExtractMetadata(string title)
        {
            var metadata = new ContentMetadata
            {
                OriginalTitle = title
            };

            // Détecter les langues
            var detectedLanguages = new List<string>();
            foreach (var kvp in LanguagePatterns)
            {
                if (kvp.Value.IsMatch(title))
                {
                    detectedLanguages.Add(kvp.Key);
                    metadata.LanguageTags.Add(kvp.Key);
                }
            }

            // Langue principale (première détectée par ordre de priorité)
            metadata.Language = detectedLanguages.FirstOrDefault() ?? "VO";

            // Détecter la saison
            Match seasonMatch;
            int seasonIndex = -1;

            if ((seasonMatch = SeasonPattern3.Match(title)).Success)
            {
                metadata.SeasonNumber = int.Parse(seasonMatch.Groups[1].Value);
                metadata.IsSeries = true;
                seasonIndex = seasonMatch.Index;
            }
            else if ((seasonMatch = SeasonPattern4.Match(title)).Success)
            {
                metadata.SeasonNumber = int.Parse(seasonMatch.Groups[1].Value);
                metadata.IsSeries = true;
                seasonIndex = seasonMatch.Index;
            }
            else if ((seasonMatch = SeasonPattern1.Match(title)).Success)
            {
                metadata.SeasonNumber = int.Parse(seasonMatch.Groups[1].Value);
                metadata.IsSeries = true;
                seasonIndex = seasonMatch.Index;
            }
            else if ((seasonMatch = SeasonPattern2.Match(title)).Success)
            {
                metadata.SeasonNumber = int.Parse(seasonMatch.Groups[1].Value);
                metadata.IsSeries = true;
                seasonIndex = seasonMatch.Index;
            }

            // Extraire le nom nettoyé
            string workingTitle = title;

            // Enlever la partie saison si détectée
            if (seasonIndex >= 0)
            {
                workingTitle = title.Substring(0, seasonIndex);
            }

            // Enlever les tags de qualité
            workingTitle = QualityPattern.Replace(workingTitle, "");

            // Enlever les tags de langue
            foreach (var pattern in LanguagePatterns.Values)
            {
                workingTitle = pattern.Replace(workingTitle, "");
            }

            // Enlever les tags entre crochets/parenthèses
            workingTitle = Regex.Replace(workingTitle, @"\[.*?\]|\(.*?\)", "");

            // Remplacer points/underscores par espaces
            workingTitle = workingTitle.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');

            // Enlever espaces multiples
            workingTitle = Regex.Replace(workingTitle, @"\s+", " ");

            metadata.CleanedName = workingTitle.Trim();

            return metadata;
        }

        /// <summary>
        /// Génère une clé unique pour grouper les résultats
        /// Format: "nom_nettoyé_langue" (en minuscules)
        /// </summary>
        public static string GenerateGroupKey(ContentMetadata metadata)
        {
            var normalizedName = metadata.CleanedName.ToLowerInvariant();
            var normalizedLang = metadata.Language.ToLowerInvariant();
            return $"{normalizedName}_{normalizedLang}";
        }

        /// <summary>
        /// Vérifie si un titre correspond au filtre VF uniquement
        /// </summary>
        public static bool IsVFContent(ContentMetadata metadata)
        {
            return metadata.Language == "VF";
        }
    }
}
