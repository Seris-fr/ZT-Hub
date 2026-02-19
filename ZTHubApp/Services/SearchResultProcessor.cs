using System.Collections.Generic;
using System.Linq;
using ZTHubApp.Models;

namespace ZTHubApp.Services
{
    public class ProcessedSearchResults
    {
        public List<SeriesGroup> SeriesGroups { get; set; } = new List<SeriesGroup>();
        public List<SearchResult> UngroupedResults { get; set; } = new List<SearchResult>();
    }

    public static class SearchResultProcessor
    {
        /// <summary>
        /// Traite les résultats de recherche pour grouper les séries multi-saisons
        /// </summary>
        /// <param name="rawResults">Résultats bruts de la recherche</param>
        /// <param name="category">Catégorie actuelle (series, mangas, films)</param>
        /// <param name="vfOnly">Si true, filtre uniquement les contenus VF</param>
        public static ProcessedSearchResults ProcessResults(
            List<SearchResult> rawResults,
            string category,
            bool vfOnly = false)
        {
            var processed = new ProcessedSearchResults();

            // Extraire les métadonnées pour chaque résultat
            var resultsWithMetadata = rawResults
                .Select(r => new
                {
                    Result = r,
                    Metadata = ContentMetadataExtractor.ExtractMetadata(r.Title)
                })
                .ToList();

            // Filtrer VF si demandé
            if (vfOnly)
            {
                resultsWithMetadata = resultsWithMetadata
                    .Where(r => ContentMetadataExtractor.IsVFContent(r.Metadata))
                    .ToList();
            }

            // Pour les séries et animes, grouper par nom + langue
            if (category == "series" || category == "mangas")
            {
                // Grouper par clé (nom_langue)
                var grouped = resultsWithMetadata
                    .Where(r => r.Metadata.IsSeries && r.Metadata.SeasonNumber.HasValue)
                    .GroupBy(r => ContentMetadataExtractor.GenerateGroupKey(r.Metadata))
                    .ToList();

                foreach (var group in grouped)
                {
                    var items = group.ToList();

                    // Si plusieurs saisons, créer un SeriesGroup
                    if (items.Count > 1)
                    {
                        var firstMetadata = items.First().Metadata;
                        var seriesGroup = new SeriesGroup
                        {
                            SeriesName = firstMetadata.CleanedName,
                            Language = firstMetadata.Language,
                            AvailableSeasons = items
                                .Select(i => i.Metadata.SeasonNumber!.Value)
                                .Distinct()
                                .OrderBy(s => s)
                                .ToList(),
                            AllResults = items.Select(i => i.Result).ToList(),
                            Image = items.First().Result.ImageUrl
                        };

                        processed.SeriesGroups.Add(seriesGroup);
                    }
                    else
                    {
                        // Une seule saison, ne pas grouper
                        processed.UngroupedResults.Add(items.First().Result);
                    }
                }

                // Ajouter les résultats sans info de saison aux non-groupés
                var withoutSeason = resultsWithMetadata
                    .Where(r => !r.Metadata.IsSeries || !r.Metadata.SeasonNumber.HasValue)
                    .Select(r => r.Result)
                    .ToList();

                processed.UngroupedResults.AddRange(withoutSeason);
            }
            else
            {
                // Pour les films, pas de groupement
                processed.UngroupedResults = resultsWithMetadata
                    .Select(r => r.Result)
                    .ToList();
            }

            return processed;
        }

        /// <summary>
        /// Mélange les groupes et résultats individuels pour affichage
        /// </summary>
        public static List<object> MergeForDisplay(ProcessedSearchResults processed)
        {
            var merged = new List<object>();

            // Ajouter les groupes de séries
            merged.AddRange(processed.SeriesGroups.Cast<object>());

            // Ajouter les résultats individuels
            merged.AddRange(processed.UngroupedResults.Cast<object>());

            return merged;
        }
    }
}
