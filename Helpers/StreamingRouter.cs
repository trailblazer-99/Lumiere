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
                    var match = Regex.Match(uri.AbsolutePath, @"/title/(\d+)");
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
                    var match = Regex.Match(uri.AbsolutePath, @"/video/([a-zA-Z0-9-]+)");
                    if (match.Success)
                    {
                        return new Uri($"disneyplus://video/{match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("primevideo.com") || host.Contains("amazon.com"))
                {
                    var match = Regex.Match(uri.AbsolutePath, @"/detail/([a-zA-Z0-9]+)");
                    if (match.Success)
                    {
                        return new Uri($"primevideo://watch?gti={match.Groups[1].Value}");
                    }
                }
                else if (host.Contains("tv.apple.com"))
                {
                    // The Windows Apple TV app often fails to route correctly (falling back to a generic page)
                    // if the deep link contains query parameters. We strip them to ensure a clean path.
                    if (!string.IsNullOrEmpty(uri.Query))
                    {
                        return new Uri(uri.GetLeftPart(UriPartial.Path));
                    }
                }

                return uri;
            }
            catch
            {
                return new Uri(webLink);
            }
        }
    }
}
