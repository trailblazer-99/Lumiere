using System;
using System.Text.RegularExpressions;

namespace LumiereMediaPlayer.Helpers
{
    public static class StreamingRouter
    {
        public static Uri? GetNativeUri(string webLink)
        {
            if (string.IsNullOrEmpty(webLink))
                return null;

            try
            {
                var uri = new Uri(webLink);
                var host = uri.Host.ToLower();

                if (host.Contains("netflix.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:title|watch)/(\d+)");
                    if (match.Success)
                    {
                        return new Uri($"netflix://title/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("spotify.com"))
                {
                    var matchTrack = Regex.Match(uri.AbsolutePath, @"/track/([a-zA-Z0-9]+)");
                    if (matchTrack.Success)
                    {
                        return new Uri($"spotify:track:{matchTrack.Groups[1].Value}");
                    }

                    var matchSearch = Regex.Match(uri.AbsolutePath, @"/search/(.+)");
                    if (matchSearch.Success)
                    {
                        return new Uri($"spotify:search:{matchSearch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("disneyplus.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:video|play|movies|series)/(?:[a-zA-Z0-9-]+/)?([a-zA-Z0-9-]+)");
                    if (match.Success)
                    {
                        return new Uri($"disneyplus://video/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("primevideo.com") || host.Contains("amazon.com") || host.Contains("amazon."))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"(?:/detail/|/gp/video/detail/|/gp/product/|/dp/)/?([a-zA-Z0-9_]{8,20})");
                    if (match.Success)
                    {
                        var id = match.Groups[1].Value;
                        if (host.Contains("primevideo.com"))
                        {
                            return new Uri($"primevideo://watch?gti={id}");
                        }
                        else
                        {
                            return new Uri($"amazonvideo://watch?asin={id}");
                        }
                    }
                    else
                    {
                        // Parse query parameters as fallback
                        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                        var gti = query["gti"];
                        var asin = query["asin"];
                        if (!string.IsNullOrEmpty(gti))
                        {
                            return new Uri($"primevideo://watch?gti={gti}");
                        }
                        if (!string.IsNullOrEmpty(asin))
                        {
                            return new Uri($"amazonvideo://watch?asin={asin}");
                        }
                    }
                }
                else if (host.Contains("hulu.com"))
                {
                    var matchWatch = Regex.Match(uri.AbsolutePath, @"/watch/([a-zA-Z0-9-]+)");
                    if (matchWatch.Success)
                    {
                        return new Uri($"hulu://w/{matchWatch.Groups[1].Value}");
                    }
                    var matchSeries = Regex.Match(uri.AbsolutePath, @"/series/(?:[a-zA-Z0-9-]+-)?([a-zA-Z0-9-]+)");
                    if (matchSeries.Success)
                    {
                        return new Uri($"hulu://series/{matchSeries.Groups[1].Value}");
                    }
                }
                else if (host.Contains("max.com") || host.Contains("hbomax.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}|[a-zA-Z0-9]+)$");
                    if (match.Success)
                    {
                        var id = match.Groups[1].Value;
                        if (host.Contains("hbomax"))
                            return new Uri($"hbomax://page/urn:hbo:page:{id}");
                        else
                            return new Uri($"max://page/{id}");
                    }
                }
                else if (host.Contains("paramountplus.com"))
                {
                    var matchMovie = Regex.Match(uri.AbsolutePath, @"/movies/[^/]+/([a-zA-Z0-9]+)");
                    if (matchMovie.Success)
                    {
                        return new Uri($"paramountplus://movies/{matchMovie.Groups[1].Value}");
                    }
                }
                else if (host.Contains("peacocktv.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/watch/(?:playback/vod|asset/[^/]+)/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"peacock://watch/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("tubitv.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:movies|series)/([0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"tubitv://show/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("pluto.tv"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/on-demand/(?:movies|series)/[^/]+/([a-zA-Z0-9-]+)");
                    if (match.Success)
                    {
                        return new Uri($"plutotv://vod/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("tv.apple.com"))
                {
                    // The Apple TV Windows app registers the custom protocol scheme 'videos://'
                    // Replaces https:// with videos:// so the app navigates directly to the designated title page
                    string cleanUrl = CleanFallbackUrl(webLink);
                    string nativeUrl = cleanUrl.Replace("https://", "videos://", StringComparison.OrdinalIgnoreCase)
                                               .Replace("http://", "videos://", StringComparison.OrdinalIgnoreCase);
                    return new Uri(nativeUrl);
                }
                else if (host.Contains("music.apple.com"))
                {
                    // The Apple Music Windows app registers the custom protocol scheme 'musics://'
                    string cleanUrl = CleanFallbackUrl(webLink);
                    string nativeUrl = cleanUrl.Replace("https://", "musics://", StringComparison.OrdinalIgnoreCase)
                                               .Replace("http://", "musics://", StringComparison.OrdinalIgnoreCase);
                    return new Uri(nativeUrl);
                }
                else if (host.Contains("itunes.apple.com"))
                {
                    // The Apple Music/iTunes Windows app registers the custom protocol scheme 'itunes://'
                    string cleanUrl = CleanFallbackUrl(webLink);
                    string nativeUrl = cleanUrl.Replace("https://", "itunes://", StringComparison.OrdinalIgnoreCase)
                                               .Replace("http://", "itunes://", StringComparison.OrdinalIgnoreCase);
                    return new Uri(nativeUrl);
                }
                else if (host.Contains("crunchyroll.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:series|watch)/([a-zA-Z0-9_-]+)");
                    if (match.Success)
                    {
                        return new Uri($"crunchyroll://series/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("vudu.com") || host.Contains("fandango.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:content|movies|watch)/[a-zA-Z0-9_-]+/([0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"vudu://watch/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("discoveryplus.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:show|video)/([a-zA-Z0-9_-]+)");
                    if (match.Success)
                    {
                        return new Uri($"discoveryplus://show/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("bbc.co.uk") && uri.AbsolutePath.Contains("iplayer"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/episode/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"bbc-iplayer://episode/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("jiocinema.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:watch|movies|tv)/[a-zA-Z0-9_-]+/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"jiocinema://watch/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("hotstar.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:watch|movies|shows)/[a-zA-Z0-9_-]+/([0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"hotstar://watch/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("tv.youtube.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/watch/([a-zA-Z0-9_-]+)");
                    if (match.Success)
                    {
                        return new Uri($"youtubetv://watch/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("tidal.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:track|album)/([0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"tidal://{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("music.amazon.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/albums/([a-zA-Z0-9_]+)");
                    if (match.Success)
                    {
                        return new Uri($"amzn-music://play?asin={match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("deezer.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:track|album)/([0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"deezer://www.deezer.com/track/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("plex.tv"))
                {
                    return new Uri("plex://");
                }

                return uri;
            }
            catch
            {
                return new Uri(webLink);
            }
        }

        public static string CleanFallbackUrl(string webLink)
        {
            if (string.IsNullOrEmpty(webLink))
                return webLink;

            try
            {
                var uri = new Uri(webLink);
                var host = uri.Host.ToLower();

                if (host.Contains("tv.apple.com") || host.Contains("music.apple.com") || host.Contains("itunes.apple.com"))
                {
                    if (!string.IsNullOrEmpty(uri.Query) && !uri.AbsolutePath.Contains("/search", StringComparison.OrdinalIgnoreCase))
                    {
                        return uri.GetLeftPart(UriPartial.Path);
                    }
                }

                return webLink;
            }
            catch
            {
                return webLink;
            }
        }

        public static async System.Threading.Tasks.Task LaunchStreamUriAsync(Uri? nativeUri, string fallbackCleanUrl)
        {
            bool launched = false;
            if (nativeUri != null && !string.Equals(nativeUri.ToString(), fallbackCleanUrl, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var support = await Windows.System.Launcher.QueryUriSupportAsync(nativeUri, Windows.System.LaunchQuerySupportType.Uri);
                    if (support == Windows.System.LaunchQuerySupportStatus.Available)
                    {
                        launched = await Windows.System.Launcher.LaunchUriAsync(nativeUri);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StreamingRouter] QueryUriSupportAsync failed: {ex.Message}");
                }
            }

            if (!launched && !string.IsNullOrEmpty(fallbackCleanUrl))
            {
                try
                {
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(fallbackCleanUrl));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StreamingRouter] Fallback HTTPS launch failed: {ex.Message}");
                }
            }
        }
    }
}
