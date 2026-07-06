using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentMediaPlayer.Models.Streaming;

namespace FluentMediaPlayer.Services.Streaming
{
    public static class TwitchExtractionService
    {
        private static readonly HttpClient HttpClient = new();
        private const string ClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko"; // Twitch public Client-ID

        public static async Task<string?> GetLiveStreamUrlAsync(string channelName, CancellationToken cancellationToken = default)
        {
            try
            {
                var requestPayload = new TwitchGqlRequest
                {
                    Variables = new TwitchGqlVariables
                    {
                        IsLive = true,
                        Login = channelName.ToLowerInvariant(),
                        IsVod = false,
                        VodID = string.Empty,
                        PlayerType = "site"
                    }
                };

                string jsonContent = JsonSerializer.Serialize(requestPayload, TwitchJsonSerializerContext.Default.TwitchGqlRequest);
                var request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
                {
                    Content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Client-ID", ClientId);

                var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var gqlResponse = await JsonSerializer.DeserializeAsync(responseStream, TwitchJsonSerializerContext.Default.TwitchGqlResponse, cancellationToken).ConfigureAwait(false);

                if (gqlResponse?.Data?.StreamPlaybackAccessToken == null)
                {
                    return null;
                }

                var token = gqlResponse.Data.StreamPlaybackAccessToken.Value;
                var sig = gqlResponse.Data.StreamPlaybackAccessToken.Signature;

                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sig))
                {
                    return null;
                }

                var random = new Random();
                string usherUrl = $"https://usher.ttvnw.net/api/channel/hls/{channelName.ToLowerInvariant()}.m3u8" +
                                  $"?allow_source=true" +
                                  $"&fast_bread=true" +
                                  $"&p={random.Next(1000000, 9999999)}" +
                                  $"&play_sessions_id=null" +
                                  $"&player_backend=mediaplayer" +
                                  $"&playlist_include_framerate=true" +
                                  $"&reassignments_supported=true" +
                                  $"&sig={sig}" +
                                  $"&supported_codecs=avc1" +
                                  $"&token={Uri.EscapeDataString(token)}";

                return usherUrl;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Twitch Extraction Error: {ex.Message}");
                return null;
            }
        }
    }
}
