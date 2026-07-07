using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models.Streaming;

namespace LumiereMediaPlayer.Services.Streaming
{
    public class StreamingAvailabilityService
    {
        private const string ApiKey = "a2c6f4f0a7msh19d39dd60e7baa6p1af0cajsn90e826cfd4b7";
        private const string Host = "streaming-availability.p.rapidapi.com";
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            client.DefaultRequestHeaders.Add("X-RapidAPI-Key", ApiKey);
            client.DefaultRequestHeaders.Add("X-RapidAPI-Host", Host);
            return client;
        }

        public async Task<List<StreamingOption>> GetStreamingOptionsAsync(string type, int tmdbId)
        {
            if (string.IsNullOrEmpty(type)) return new List<StreamingOption>();
            try
            {
                var url = $"https://streaming-availability.p.rapidapi.com/shows/{type}/{tmdbId}";
                var json = await HttpClient.GetStringAsync(url);
                var result = JsonSerializer.Deserialize<StreamingAvailabilityResponse>(json, JsonOptions);

                if (result?.StreamingOptions != null)
                {
                    var region = await AntiGravityLocationEngine.GetCountryCodeAsync();
                    if (result.StreamingOptions.TryGetValue(region.ToLower(), out var regionOptions))
                    {
                        return regionOptions;
                    }
                    
                    if (result.StreamingOptions.TryGetValue("us", out var usOptions))
                    {
                        return usOptions;
                    }

                    foreach (var options in result.StreamingOptions.Values)
                    {
                        if (options != null && options.Count > 0)
                        {
                            return options;
                        }
                    }
                }

                return new List<StreamingOption>();
            }
            catch
            {
                return new List<StreamingOption>();
            }
        }
    }
}
