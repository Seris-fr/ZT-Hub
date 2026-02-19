using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ZTHubApp.Services
{
    public class ParsedContentInfo
    {
        public string ContentName { get; set; } = string.Empty;
        public int? Season { get; set; }
        public int? Episode { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public bool HasSeasonInfo { get; set; }
        public bool HasEpisodeInfo { get; set; }
    }

    public static class FileOrganizerService
    {
        // Regex patterns (ordonnés par priorité)

        // S01E01 ou S01.E01 ou S1E1
        private static readonly Regex SeasonEpisodePattern1 = new(
            @"[Ss](\d{1,2})[.\s_-]*[Ee](\d{1,2})",
            RegexOptions.Compiled
        );

        // 1x01 ou 1X01
        private static readonly Regex SeasonEpisodePattern2 = new(
            @"(\d{1,2})[xX](\d{1,2})",
            RegexOptions.Compiled
        );

        // Saison 1 Episode 01 (français)
        private static readonly Regex SeasonEpisodePattern3 = new(
            @"[Ss]aison\s*(\d{1,2})\s*[Ee]pisode\s*(\d{1,2})",
            RegexOptions.Compiled
        );

        // Season 1 Episode 01 (anglais)
        private static readonly Regex SeasonEpisodePattern4 = new(
            @"[Ss]eason\s*(\d{1,2})\s*[Ee]pisode\s*(\d{1,2})",
            RegexOptions.Compiled
        );

        // Juste saison: S01, Saison 1, Season 1
        private static readonly Regex SeasonOnlyPattern = new(
            @"(?:[Ss]aison|[Ss]eason|\b[Ss])\s*(\d{1,2})\b",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Parse un nom de fichier pour extraire les informations de contenu
        /// </summary>
        public static ParsedContentInfo ParseFileName(string fileName)
        {
            var result = new ParsedContentInfo
            {
                OriginalFileName = fileName
            };

            // Supprimer l'extension pour faciliter le parsing
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

            // Essayer d'extraire saison et épisode
            Match match;

            if ((match = SeasonEpisodePattern1.Match(nameWithoutExt)).Success)
            {
                result.Season = int.Parse(match.Groups[1].Value);
                result.Episode = int.Parse(match.Groups[2].Value);
                result.HasSeasonInfo = true;
                result.HasEpisodeInfo = true;
                result.ContentName = ExtractContentName(nameWithoutExt, match.Index);
            }
            else if ((match = SeasonEpisodePattern2.Match(nameWithoutExt)).Success)
            {
                result.Season = int.Parse(match.Groups[1].Value);
                result.Episode = int.Parse(match.Groups[2].Value);
                result.HasSeasonInfo = true;
                result.HasEpisodeInfo = true;
                result.ContentName = ExtractContentName(nameWithoutExt, match.Index);
            }
            else if ((match = SeasonEpisodePattern3.Match(nameWithoutExt)).Success)
            {
                result.Season = int.Parse(match.Groups[1].Value);
                result.Episode = int.Parse(match.Groups[2].Value);
                result.HasSeasonInfo = true;
                result.HasEpisodeInfo = true;
                result.ContentName = ExtractContentName(nameWithoutExt, match.Index);
            }
            else if ((match = SeasonEpisodePattern4.Match(nameWithoutExt)).Success)
            {
                result.Season = int.Parse(match.Groups[1].Value);
                result.Episode = int.Parse(match.Groups[2].Value);
                result.HasSeasonInfo = true;
                result.HasEpisodeInfo = true;
                result.ContentName = ExtractContentName(nameWithoutExt, match.Index);
            }
            else if ((match = SeasonOnlyPattern.Match(nameWithoutExt)).Success)
            {
                result.Season = int.Parse(match.Groups[1].Value);
                result.HasSeasonInfo = true;
                result.ContentName = ExtractContentName(nameWithoutExt, match.Index);
            }
            else
            {
                // Pas de pattern trouvé, utiliser le nom complet
                result.ContentName = CleanContentName(nameWithoutExt);
            }

            return result;
        }

        /// <summary>
        /// Extrait le nom du contenu avant le pattern de saison/épisode
        /// </summary>
        private static string ExtractContentName(string fullName, int patternIndex)
        {
            if (patternIndex <= 0)
                return CleanContentName(fullName);

            var beforePattern = fullName.Substring(0, patternIndex);
            return CleanContentName(beforePattern);
        }

        /// <summary>
        /// Nettoie le nom du contenu (enlever caractères spéciaux, etc.)
        /// </summary>
        private static string CleanContentName(string name)
        {
            // Enlever les patterns communs de qualité/source
            var cleanedName = Regex.Replace(name, @"\b(1080p|720p|2160p|4K|HDTV|WEB-DL|BluRay|BDRip|WEBRip|PROPER|REPACK|FRENCH|VOSTFR|VFF|MULTI)\b", "", RegexOptions.IgnoreCase);

            // Enlever les tags entre crochets/parenthèses
            cleanedName = Regex.Replace(cleanedName, @"\[.*?\]|\(.*?\)", "");

            // Remplacer points/underscores par espaces
            cleanedName = cleanedName.Replace('.', ' ').Replace('_', ' ');

            // Enlever espaces multiples
            cleanedName = Regex.Replace(cleanedName, @"\s+", " ");

            return cleanedName.Trim();
        }

        /// <summary>
        /// Construit le chemin complet selon la hiérarchie: Type/Nom/[Saison X]/fichier
        /// </summary>
        public static string BuildOrganizedPath(string baseFolder, string category, ParsedContentInfo info, string fileName)
        {
            // Déterminer le type de dossier racine selon la catégorie
            string typeFolder = category switch
            {
                "mangas" => "Anime",
                "films" => "Film",
                "series" => "Serie",
                _ => "Autres"
            };

            // Nettoyer le nom du contenu pour éviter caractères invalides
            string safeName = SanitizeFolderName(info.ContentName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = SanitizeFolderName(Path.GetFileNameWithoutExtension(fileName));
            }

            // Construction du chemin selon le type
            string finalPath;

            if (category == "films")
            {
                // Films: Film/Nom du film/fichier.mkv
                finalPath = Path.Combine(baseFolder, typeFolder, safeName, fileName);
            }
            else if ((category == "series" || category == "mangas") && info.HasSeasonInfo)
            {
                // Séries/Animes avec saison: Serie/Nom/Saison X/fichier.mkv
                string seasonFolder = $"Saison {info.Season:D2}";
                finalPath = Path.Combine(baseFolder, typeFolder, safeName, seasonFolder, fileName);
            }
            else if (category == "series" || category == "mangas")
            {
                // Séries/Animes sans info de saison détectée: Serie/Nom/fichier.mkv
                finalPath = Path.Combine(baseFolder, typeFolder, safeName, fileName);
            }
            else
            {
                // Fallback pour autres cas
                finalPath = Path.Combine(baseFolder, typeFolder, safeName, fileName);
            }

            return finalPath;
        }

        /// <summary>
        /// Nettoie un nom de dossier des caractères invalides
        /// </summary>
        private static string SanitizeFolderName(string folderName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                folderName = folderName.Replace(c.ToString(), "_");
            }

            // Enlever caractères problématiques supplémentaires
            folderName = folderName.Replace(":", "_").Replace("?", "_");

            return folderName.Trim();
        }

        /// <summary>
        /// Crée la hiérarchie de dossiers si elle n'existe pas
        /// </summary>
        public static void EnsureDirectoryExists(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}
