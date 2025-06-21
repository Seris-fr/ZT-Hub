using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace ZTHubApp.Services
{
    public static class HttpService
    {
        private static readonly HttpClient _client = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static HttpService()
        {
            _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        public static async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            try
            {
                return await _client.GetStringAsync(url, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Erreur réseau lors de l'accès à {url}: {ex.Message}");
            }
        }

        public static async Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct)
        {
            return await _client.GetByteArrayAsync(url, ct);
        }
    }
}