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
    }
}
