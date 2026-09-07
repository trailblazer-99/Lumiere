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
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"netflix:search?q={qMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("spotify.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(track|album|artist|playlist|show|episode)/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"spotify:{match.Groups[1].Value}:{match.Groups[2].Value}");
                    }

                    var matchSearch = Regex.Match(uri.AbsolutePath, @"/search/(.+)");
                    if (matchSearch.Success)
                    {
                        return new Uri($"spotify:search:{matchSearch.Groups[1].Value}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"spotify:search:{qMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("disneyplus.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:video|play|movies|series)/(?:[a-zA-Z0-9-]+/)?([a-zA-Z0-9-]+)");
                    if (match.Success)
                    {
                        return new Uri($"disneyplus://video/{match.Groups[1].Value}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"disneyplus://search?q={qMatch.Groups[1].Value}");
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
                        var q = query["q"] ?? query["k"] ?? query["phrase"];
                        if (!string.IsNullOrEmpty(q))
                        {
                            return new Uri($"primevideo://search?q={Uri.EscapeDataString(q)}");
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
                    var matchSeries = Regex.Match(uri.AbsolutePath, @"/(?:series|movie)/(?:[a-zA-Z0-9-]+-)?([a-zA-Z0-9-]+)");
                    if (matchSeries.Success)
                    {
                        return new Uri($"hulu://series/{matchSeries.Groups[1].Value}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"hulu://search?q={qMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("max.com") || host.Contains("hbomax.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:movie|show|video|page)?/?([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}|[a-zA-Z0-9]+)$");
                    if (match.Success && !uri.AbsolutePath.Contains("search"))
                    {
                        var id = match.Groups[1].Value;
                        if (host.Contains("hbomax"))
                            return new Uri($"hbomax://page/urn:hbo:page:{id}");
                        else
                            return new Uri($"max://page/{id}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"max://search?q={qMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("paramountplus.com"))
                {
                    var matchMovie = Regex.Match(uri.AbsolutePath, @"/movies/(?:video/|[^/]+/)?([a-zA-Z0-9]+)");
                    if (matchMovie.Success)
                    {
                        return new Uri($"paramountplus://movies/{matchMovie.Groups[1].Value}");
                    }
                    var matchShows = Regex.Match(uri.AbsolutePath, @"/shows/([a-zA-Z0-9_-]+)");
                    if (matchShows.Success)
                    {
                        return new Uri($"paramountplus://shows/{matchShows.Groups[1].Value}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"paramountplus://search/?q={qMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("peacocktv.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/watch/(?:playback/vod|asset/[^/]+)/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"peacock://watch/{match.Groups[1].Value}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"peacock://search?q={qMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("tubitv.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:movies|series|tv-shows)/([0-9]+)");
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
                    if (webLink.Contains("/search", StringComparison.OrdinalIgnoreCase))
                    {
                        var qMatch = System.Text.RegularExpressions.Regex.Match(uri.Query, @"[?&]term=([^&]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (qMatch.Success)
                        {
                            // Route directly to the native Apple TV Windows app's internal search page
                            return new Uri($"videos://search?term={qMatch.Groups[1].Value}");
                        }
                        return uri;
                    }

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
                    string cleanUrl = CleanFallbackUrl(webLink);
                    if (cleanUrl.Contains("/movie/") || cleanUrl.Contains("/tv-season/"))
                    {
                        // Route to Apple TV Windows app
                        string nativeUrl = cleanUrl.Replace("https://", "videos://", StringComparison.OrdinalIgnoreCase)
                                                   .Replace("http://", "videos://", StringComparison.OrdinalIgnoreCase);
                        return new Uri(nativeUrl);
                    }
                    else
                    {
                        // The Apple Music/iTunes Windows app registers the custom protocol scheme 'itunes://'
                        string nativeUrl = cleanUrl.Replace("https://", "itunes://", StringComparison.OrdinalIgnoreCase)
                                                   .Replace("http://", "itunes://", StringComparison.OrdinalIgnoreCase);
                        return new Uri(nativeUrl);
                    }
                }
                else if (host.Contains("crunchyroll.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:[a-z]{2}(?:-[a-z]{2})?/)?(?:series|watch)/([a-zA-Z0-9_-]+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return new Uri($"crunchyroll://series/{match.Groups[1].Value}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"crunchyroll://search?q={qMatch.Groups[1].Value}");
                    }
                    var slug = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault();
                    if (!string.IsNullOrEmpty(slug) && slug.Length > 2 && !slug.Equals("search", StringComparison.OrdinalIgnoreCase))
                    {
                        return new Uri($"crunchyroll://search?q={Uri.EscapeDataString(slug.Replace('-', ' '))}");
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
                else if (host.Contains("hotstar.com"))
                {
                    var qMatch = Regex.Match(uri.Query, @"[?&](?:q|search_query)=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"hotstar://search?q={qMatch.Groups[1].Value}");
                    }
                    var idMatch = Regex.Match(uri.AbsolutePath, @"(?:/in)?/(?:movies|shows|sports|watch)?(?:/[^/]+)?/(\d+)");
                    if (idMatch.Success)
                    {
                        return new Uri($"hotstar://content/{idMatch.Groups[1].Value}");
                    }
                    var rawIdMatch = Regex.Match(uri.AbsolutePath, @"/(\d{5,})");
                    if (rawIdMatch.Success)
                    {
                        return new Uri($"hotstar://content/{rawIdMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("jiocinema.com"))
                {
                    var searchMatch = Regex.Match(uri.AbsolutePath, @"/search/(.+)");
                    if (searchMatch.Success)
                    {
                        return new Uri($"jiocinema://search?q={searchMatch.Groups[1].Value}");
                    }
                    var idMatch = Regex.Match(uri.AbsolutePath, @"/(?:movies|tv-shows|tv|watch)?(?:/[^/]+)?/(\d+)");
                    if (idMatch.Success)
                    {
                        return new Uri($"jiocinema://content/{idMatch.Groups[1].Value}");
                    }
                    var rawIdMatch = Regex.Match(uri.AbsolutePath, @"/(\d{5,})");
                    if (rawIdMatch.Success)
                    {
                        return new Uri($"jiocinema://content/{rawIdMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("zee5.com"))
                {
                    var idMatch = Regex.Match(uri.AbsolutePath, @"/([0-9a-zA-Z-]+)$");
                    if (idMatch.Success && !uri.AbsolutePath.Contains("search"))
                    {
                        return new Uri($"zee5://content/{idMatch.Groups[1].Value}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"zee5://search?q={qMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("sonyliv.com"))
                {
                    var idMatch = Regex.Match(uri.AbsolutePath, @"/(\d{6,})");
                    if (idMatch.Success)
                    {
                        return new Uri($"sonyliv://watch/{idMatch.Groups[1].Value}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&]q=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"sonyliv://search?q={qMatch.Groups[1].Value}");
                    }
                }
                else if (host.Contains("youtube.com") || host.Contains("youtu.be"))
                {
                    var vMatch = Regex.Match(uri.Query, @"[?&]v=([^&]+)", RegexOptions.IgnoreCase);
                    if (vMatch.Success)
                    {
                        return new Uri($"vnd.youtube://{vMatch.Groups[1].Value}");
                    }
                    var qMatch = Regex.Match(uri.Query, @"[?&](?:q|search_query)=([^&]+)", RegexOptions.IgnoreCase);
                    if (qMatch.Success)
                    {
                        return new Uri($"vnd.youtube://search?q={qMatch.Groups[1].Value}");
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
                else if (host.Contains("dazn.com"))
                {
                    return new Uri("dazn://");
                }
                else if (host.Contains("f1tv.formula1.com") || host.Contains("formula1.com"))
                {
                    return new Uri("f1tv://");
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
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:track|album|artist|video)/([0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"tidal://{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("music.amazon.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:albums|tracks|artists)/([a-zA-Z0-9_]+)");
                    if (match.Success)
                    {
                        return new Uri($"amzn-music://play?asin={match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("deezer.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/(?:track|album|artist)/([0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"deezer://www.deezer.com/track/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("soundcloud.com"))
                {
                    return new Uri($"soundcloud://{uri.AbsolutePath.Trim('/')}");
                }
                else if (host.Contains("plex.tv"))
                {
                    return new Uri("plex://");
                }

                return uri;
            }
            catch
            {
                return null;
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
                        if (uri.Query.Contains("action=play", StringComparison.OrdinalIgnoreCase))
                            return uri.GetLeftPart(UriPartial.Path) + "?action=play";
                        else if (host.Contains("tv.apple.com"))
                            return uri.GetLeftPart(UriPartial.Path) + "?ctx_brand=tvs.sbd.4000";
                        else
                            return uri.GetLeftPart(UriPartial.Path);
                    }
                    else if (host.Contains("tv.apple.com") && !uri.AbsolutePath.Contains("/search", StringComparison.OrdinalIgnoreCase))
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
            if (nativeUri != null && nativeUri.Scheme == "videos" && nativeUri.Query.Contains("term=", StringComparison.OrdinalIgnoreCase))
            {
                var qMatch = Regex.Match(nativeUri.Query, @"[?&]term=([^&]+)", RegexOptions.IgnoreCase);
                if (qMatch.Success)
                {
                    string rawTerm = Uri.UnescapeDataString(qMatch.Groups[1].Value);
                    try
                    {
                        string targetRegion = AppleTvDeepLinkHelper.GetCurrentRegion();
                        string canonicalUrl = await AppleTvDeepLinkHelper.ResolveAppleTvUrlAsync(rawTerm, "tvShow", null, targetRegion);
                        if (!string.IsNullOrEmpty(canonicalUrl) && !canonicalUrl.Contains("/search", StringComparison.OrdinalIgnoreCase))
                        {
                            nativeUri = GetNativeUri(canonicalUrl);
                            fallbackCleanUrl = canonicalUrl;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StreamingRouter] Apple TV API resolve failed: {ex.Message}");
                    }
                }
            }

            if (!string.IsNullOrEmpty(fallbackCleanUrl) &&
                (fallbackCleanUrl.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                 fallbackCleanUrl.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    App.MainWindowInstance?.NavigateToYouTube(fallbackCleanUrl);
                    return;
                }
                catch { }
            }

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

