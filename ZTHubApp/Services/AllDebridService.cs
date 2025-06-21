using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZTHubApp.Services
{
    public class AllDebridApiException : Exception
    {
        public string ErrorCode { get; }
        public AllDebridApiException(string code, string message) : base(message) { ErrorCode = code; }
    }
    
    public class AllDebridService
    {
        private readonly string _apiKey;
        private const string BASE_URL = "https://api.alldebrid.com/v4";
        private readonly HttpClient _client;

        public AllDebridService(string apiKey)
        {
            _apiKey = apiKey;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _client.DefaultRequestHeaders.Add("User-Agent", "ZTHubApp");
        }

        public async Task<string> UnlockLinkAsync(string link)
        {
            try
            {
                var data = await MakeRequestAsync("link/unlock", new Dictionary<string, string> { { "link", link } });
                if (data.TryGetProperty("link", out var finalLink))
                {
                    return finalLink.GetString() ?? throw new Exception("Lien débridé vide.");
                }
                throw new Exception("La réponse de débridage ne contient pas de lien.");
            }
            catch (AllDebridApiException ex) when (ex.ErrorCode == "LINK_HOST_NOT_SUPPORTED")
            {
                var redirectorData = await MakeRequestAsync("link/redirector", new Dictionary<string, string> { { "link", link } });

                if (redirectorData.TryGetProperty("links", out var linksArray) && linksArray.ValueKind == JsonValueKind.Array)
                {
                    var extractedLinks = linksArray.EnumerateArray().Select(e => e.GetString()).ToList();
                    if (extractedLinks.Any() && !string.IsNullOrEmpty(extractedLinks[0]))
                    {
                        return await UnlockLinkAsync(extractedLinks[0]);
                    }
                }
                throw new Exception("Le redirecteur n'a retourné aucun lien valide.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Erreur AllDebrid: {ex.Message}");
            }
        }

        private async Task<JsonElement> MakeRequestAsync(string endpoint, Dictionary<string, string> parameters)
        {
            var url = $"{BASE_URL}/{endpoint}";
            var content = new FormUrlEncodedContent(parameters);
            
            var response = await _client.PostAsync(url, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Erreur API AllDebrid (HTTP {response.StatusCode})");

            var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
            {
                return root.GetProperty("data");
            }

            if (root.TryGetProperty("error", out var errorProp) && errorProp.TryGetProperty("code", out var codeProp) && errorProp.TryGetProperty("message", out var messageProp))
            {
                 throw new AllDebridApiException(codeProp.GetString() ?? "UNKNOWN_CODE", messageProp.GetString() ?? "Unknown error");
            }
            
            throw new Exception("Réponse inconnue de l'API AllDebrid.");
        }
    }
}