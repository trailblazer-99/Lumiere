using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LumiereMediaPlayer.Models.Streaming;
using LumiereMediaPlayer.Services.Streaming;

namespace LumiereMediaPlayer.Helpers
{
    public record QualityBadgeDescriptor(string Text, string Tooltip);

    public class GroupedStreamingSources
    {
        public List<WatchmodeSource> SubscriptionSources { get; set; } = new();
        public List<WatchmodeSource> FreeSources { get; set; } = new();
        public List<WatchmodeSource> PurchaseSources { get; set; } = new();

        public bool HasAnySources =>
            SubscriptionSources.Count > 0 || FreeSources.Count > 0 || PurchaseSources.Count > 0;
    }

    public static class StreamingProviderHelper
    {
        public static int GetFormatPriority(string? format)
        {
            return (format?.ToUpperInvariant()) switch
            {
                "4K" => 3,
                "HD" => 2,
                "SD" => 1,
                _ => 0
            };
        }

        public static bool IsAppleOriginal(WatchmodeDetails? details)
        {
            if (details == null) return false;
            if (details.NetworkNames?.Any(n => n != null && n.Contains("apple", StringComparison.OrdinalIgnoreCase)) == true)
                return true;
            if (details.StudioNames?.Any(s => s != null && s.Contains("apple", StringComparison.OrdinalIgnoreCase)) == true)
                return true;

            string clean = details.Title?.Trim() ?? "";
            var knownAppleOriginals = new[]
            {
                "Presumed Innocent", "Ted Lasso", "Severance", "The Morning Show", "For All Mankind",
                "Slow Horses", "Shrinking", "Silo", "Foundation", "Bad Monkey", "Pachinko",
                "Hijack", "Black Bird", "Dark Matter", "Sugar", "Masters of the Air",
                "Monarch: Legacy of Monsters", "See", "Servant", "Mythic Quest", "Dickinson",
                "Physical", "Invasion", "Lady in the Lake", "Defending Jacob", "Platonic",
                "Palm Royale", "The Afterparty", "Schmigadoon!", "Trying", "Loot",
                "Wolfs", "The Instigators", "Argylle", "Napoleon", "Killers of the Flower Moon",
                "CODA", "Greyhound", "Finch", "Spirited", "Tetris", "Ghosted", "The Family Plan",
                "Fly Me to the Moon", "Sharper", "The Banker", "Cherry"
            };

            return knownAppleOriginals.Any(t => string.Equals(clean, t, StringComparison.OrdinalIgnoreCase) ||
                                                clean.StartsWith(t, StringComparison.OrdinalIgnoreCase));
        }

        public static int GetProviderPriority(WatchmodeSource source, WatchmodeDetails? details)
        {
            if (source == null || string.IsNullOrEmpty(source.Name)) return 100;
            var lower = source.Name.ToLowerInvariant();

            bool isAddOnOrChannel = lower.Contains("channel") || lower.Contains("add-on") || lower.Contains("addon") ||
                                    lower.Contains("on prime") || lower.Contains("on roku") || lower.Contains("on apple") ||
                                    string.Equals(source.Type, "addon", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(source.Type, "sub_addon", StringComparison.OrdinalIgnoreCase);

            if (!isAddOnOrChannel && details != null)
            {
                bool isApple = IsAppleOriginal(details);
                bool isNetflix = (details.NetworkNames?.Any(n => n.Contains("netflix", StringComparison.OrdinalIgnoreCase)) ?? false) ||
                                 (details.StudioNames?.Any(s => s.Contains("netflix", StringComparison.OrdinalIgnoreCase)) ?? false);
                bool isPrime = (details.NetworkNames?.Any(n => n.Contains("amazon", StringComparison.OrdinalIgnoreCase) || n.Contains("prime", StringComparison.OrdinalIgnoreCase)) ?? false) ||
                               (details.StudioNames?.Any(s => s.Contains("amazon", StringComparison.OrdinalIgnoreCase) || s.Contains("prime", StringComparison.OrdinalIgnoreCase)) ?? false);
                bool isDisney = (details.NetworkNames?.Any(n => n.Contains("disney", StringComparison.OrdinalIgnoreCase)) ?? false) ||
                                (details.StudioNames?.Any(s => s.Contains("disney", StringComparison.OrdinalIgnoreCase)) ?? false);
                bool isMax = (details.NetworkNames?.Any(n => n.Contains("hbo", StringComparison.OrdinalIgnoreCase) || n.Contains("max", StringComparison.OrdinalIgnoreCase)) ?? false) ||
                             (details.StudioNames?.Any(s => s.Contains("hbo", StringComparison.OrdinalIgnoreCase) || s.Contains("max", StringComparison.OrdinalIgnoreCase)) ?? false);

                if (isApple && lower.Contains("apple")) return 0;
                if (isNetflix && lower.Contains("netflix")) return 0;
                if (isPrime && (lower.Contains("prime") || lower.Contains("amazon"))) return 0;
                if (isDisney && lower.Contains("disney")) return 0;
                if (isMax && (lower.Contains("max") || lower.Contains("hbo"))) return 0;
            }

            int tierOffset = isAddOnOrChannel ? 50 : 0;
            if (string.Equals(source.Type, "rent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Type, "buy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Type, "purchase", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source.Type, "tvod", StringComparison.OrdinalIgnoreCase))
            {
                tierOffset = 80;
            }

            int baseRank = 40;
            if (lower.Contains("apple")) baseRank = 1;
            else if (lower.Contains("netflix")) baseRank = 2;
            else if (lower.Contains("prime") || lower.Contains("amazon")) baseRank = 3;
            else if (lower.Contains("disney")) baseRank = 4;
            else if (lower.Contains("max") || lower.Contains("hbo")) baseRank = 5;
            else if (lower.Contains("hulu")) baseRank = 6;
            else if (lower.Contains("paramount")) baseRank = 7;
            else if (lower.Contains("peacock")) baseRank = 8;
            else if (lower.Contains("youtube") || lower.Contains("google")) baseRank = 9;
            else if (lower.Contains("vudu") || lower.Contains("fandango")) baseRank = 10;
            else if (lower.Contains("tubi")) baseRank = 11;
            else if (lower.Contains("pluto")) baseRank = 12;
            else if (lower.Contains("roku")) baseRank = 13;
            else if (lower.Contains("plex")) baseRank = 14;
            else if (lower.Contains("crunchyroll")) baseRank = 15;

            return tierOffset + baseRank;
        }

        public static string ResolveProviderUrl(WatchmodeSource source, WatchmodeDetails? details)
        {
            string webUrl = source.WebUrl ?? "";

            // If WebUrl is missing or short, check if mobile URLs have a valid deep link
            if (string.IsNullOrWhiteSpace(webUrl) || webUrl.Length < 30)
            {
                if (!string.IsNullOrWhiteSpace(source.AndroidUrl) && source.AndroidUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && source.AndroidUrl.Length > 28)
                    webUrl = source.AndroidUrl;
                else if (!string.IsNullOrWhiteSpace(source.IosUrl) && source.IosUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && source.IosUrl.Length > 28)
                    webUrl = source.IosUrl;
            }

            string name = source.Name?.ToLowerInvariant() ?? "";

            if (name.Contains("crunchyroll"))
            {
                if (string.IsNullOrWhiteSpace(webUrl) ||
                    webUrl.Equals("https://www.crunchyroll.com", StringComparison.OrdinalIgnoreCase) ||
                    webUrl.Equals("http://www.crunchyroll.com", StringComparison.OrdinalIgnoreCase) ||
                    webUrl.Equals("https://crunchyroll.com", StringComparison.OrdinalIgnoreCase))
                {
                    string searchQuery = !string.IsNullOrWhiteSpace(details?.Title) ? details.Title : "";
                    webUrl = !string.IsNullOrWhiteSpace(searchQuery)
                        ? $"https://www.crunchyroll.com/search?q={Uri.EscapeDataString(searchQuery)}"
                        : "https://www.crunchyroll.com";
                }
            }

            if (name.Contains("apple"))
            {
                string targetRegion = AppleTvDeepLinkHelper.GetCurrentRegion();
                string cleanTitle = details?.Title?.Trim() ?? "";

                string? knownPath = AppleTvDeepLinkHelper.GetKnownCanonicalPath(cleanTitle);
                if (!string.IsNullOrEmpty(knownPath))
                {
                    webUrl = $"https://tv.apple.com/{targetRegion}/{knownPath}";
                }
                else if (webUrl.Contains("tv.apple.com", StringComparison.OrdinalIgnoreCase) || webUrl.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
                {
                    webUrl = AppleTvDeepLinkHelper.RewriteUrlRegion(webUrl, targetRegion);
                }
            }

            if (name.Contains("hotstar"))
            {
                var hotstarIdMatch = Regex.Match(webUrl, @"/(\d{6,})");
                if (hotstarIdMatch.Success)
                {
                    string hotstarId = hotstarIdMatch.Groups[1].Value;
                    string titleSlug = "";
                    if (!string.IsNullOrWhiteSpace(details?.Title))
                    {
                        titleSlug = Regex.Replace(details.Title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                    }
                    if (string.IsNullOrEmpty(titleSlug)) titleSlug = "title";

                    string mediaType = (details?.Type?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true) ? "movies" : "shows";
                    webUrl = $"https://www.hotstar.com/in/{mediaType}/{titleSlug}/{hotstarId}";
                    AntiGravityLogger.Log($"Hotstar URL upgraded to canonical deep link: {webUrl}");
                }
            }

            if (name.Contains("jiocinema") || name.Contains("jio cinema"))
            {
                var jioIdMatch = Regex.Match(webUrl, @"/(\d{6,})");
                if (jioIdMatch.Success)
                {
                    string jioId = jioIdMatch.Groups[1].Value;
                    string titleSlug = "";
                    if (!string.IsNullOrWhiteSpace(details?.Title))
                    {
                        titleSlug = Regex.Replace(details.Title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                    }
                    if (string.IsNullOrEmpty(titleSlug)) titleSlug = "title";

                    string mediaType = (details?.Type?.Equals("movie", StringComparison.OrdinalIgnoreCase) == true) ? "movies" : "tv-shows";
                    webUrl = $"https://www.jiocinema.com/{mediaType}/{titleSlug}/{jioId}";
                    AntiGravityLogger.Log($"JioCinema URL upgraded to canonical deep link: {webUrl}");
                }
            }

            // Check if it's missing or just a root domain
            bool isRootDomain = false;
            if (Uri.TryCreate(webUrl, UriKind.Absolute, out var parsedUri))
            {
                var trimmedPath = parsedUri.AbsolutePath.Trim('/');
                isRootDomain = string.IsNullOrEmpty(trimmedPath) || (trimmedPath.Length == 2 && name.Contains("apple"));
            }

            if (string.IsNullOrWhiteSpace(webUrl) || (isRootDomain && string.IsNullOrEmpty(parsedUri?.Query)))
            {
                string query = !string.IsNullOrWhiteSpace(details?.Title) ? details.Title : "";
                string encoded = Uri.EscapeDataString(query);

                if (name.Contains("netflix"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.netflix.com/search?q={encoded}" : "https://www.netflix.com";
                else if (name.Contains("prime") || name.Contains("amazon"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.amazon.com/s?k={encoded}&i=instant-video" : "https://www.primevideo.com";
                else if (name.Contains("disney") && !name.Contains("hotstar"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.disneyplus.com/search?q={encoded}" : "https://www.disneyplus.com";
                else if (name.Contains("hotstar"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.hotstar.com/in/explore?search_query={encoded}" : "https://www.hotstar.com";
                else if (name.Contains("jiocinema") || name.Contains("jio cinema"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.jiocinema.com/search/{encoded}" : "https://www.jiocinema.com";
                else if (name.Contains("hulu"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.hulu.com/search?q={encoded}" : "https://www.hulu.com";
                else if (name.Contains("max") || name.Contains("hbo"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://play.max.com/search?q={encoded}" : "https://play.max.com";
                else if (name.Contains("paramount"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.paramountplus.com/search/?q={encoded}" : "https://www.paramountplus.com";
                else if (name.Contains("peacock"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.peacocktv.com/watch/search?q={encoded}" : "https://www.peacocktv.com";
                else if (name.Contains("youtube"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.youtube.com/results?search_query={encoded}" : "https://www.youtube.com";
                else if (name.Contains("crunchyroll"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.crunchyroll.com/search?q={encoded}" : "https://www.crunchyroll.com";
                else if (name.Contains("vudu") || name.Contains("fandango"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.vudu.com/content/movies/search?minVisible=0&returnUrl=%252F&searchString={encoded}" : "https://www.vudu.com";
                else if (name.Contains("zee5") || name.Contains("zee 5"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.zee5.com/search?q={encoded}" : "https://www.zee5.com";
                else if (name.Contains("sonyliv") || name.Contains("sony liv"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.sonyliv.com/search/{encoded}" : "https://www.sonyliv.com";
                else if (name.Contains("discovery"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://www.discoveryplus.com/search?q={encoded}" : "https://www.discoveryplus.com";
                else if (name.Contains("tubi"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://tubitv.com/search/{encoded}" : "https://tubitv.com";
                else if (name.Contains("pluto"))
                    webUrl = !string.IsNullOrEmpty(query) ? $"https://pluto.tv/search/details?q={encoded}" : "https://pluto.tv";
                else if (name.Contains("apple") || name.Contains("itunes"))
                {
                    string targetRegion = AppleTvDeepLinkHelper.GetCurrentRegion();
                    string? knownPath = AppleTvDeepLinkHelper.GetKnownCanonicalPath(query);
                    webUrl = !string.IsNullOrEmpty(knownPath)
                        ? $"https://tv.apple.com/{targetRegion}/{knownPath}"
                        : (!string.IsNullOrEmpty(query) ? $"https://tv.apple.com/{targetRegion}/search?term={encoded}" : $"https://tv.apple.com/{targetRegion}/");
                }
                else if (!string.IsNullOrEmpty(query))
                    webUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(query + " watch on " + source.Name)}";
            }

            return webUrl;
        }

        public static string GetCurrencySymbol(string regionCode)
        {
            try
            {
                if (!string.IsNullOrEmpty(regionCode))
                {
                    var region = new RegionInfo(regionCode);
                    return region.CurrencySymbol;
                }
            }
            catch { }
            return "$";
        }

        public static string GetProviderIconUrl(WatchmodeSource source)
        {
            string name = source.Name?.ToLowerInvariant() ?? "";
            string domain = "";

            if (name.Contains("netflix")) domain = "netflix.com";
            else if (name.Contains("hulu")) domain = "hulu.com";
            else if (name.Contains("prime") || name.Contains("amazon")) domain = "primevideo.com";
            else if (name.Contains("disney")) domain = "disneyplus.com";
            else if (name.Contains("hotstar")) domain = "hotstar.com";
            else if (name.Contains("max") || name.Contains("hbo")) domain = "max.com";
            else if (name.Contains("apple")) domain = "tv.apple.com";
            else if (name.Contains("peacock")) domain = "peacocktv.com";
            else if (name.Contains("paramount")) domain = "paramountplus.com";
            else if (name.Contains("youtube")) domain = "youtube.com";
            else if (name.Contains("google")) domain = "play.google.com";
            else if (name.Contains("vudu") || name.Contains("fandango")) domain = "vudu.com";
            else if (name.Contains("crunchyroll")) domain = "crunchyroll.com";
            else if (name.Contains("funimation")) domain = "funimation.com";
            else if (name.Contains("plex")) domain = "plex.tv";
            else if (name.Contains("tubi")) domain = "tubitv.com";
            else if (name.Contains("pluto")) domain = "pluto.tv";
            else if (name.Contains("roku")) domain = "roku.com";
            else if (name.Contains("jiocinema")) domain = "jiocinema.com";
            else if (name.Contains("zee5") || name.Equals("zee")) domain = "zee5.com";
            else if (name.Contains("sonyliv")) domain = "sonyliv.com";
            else if (name.Contains("sling")) domain = "sling.com";
            else if (name.Contains("fubo")) domain = "fubo.tv";
            else if (name.Contains("philo")) domain = "philo.com";
            else if (name.Contains("directv")) domain = "directv.com";
            else if (name.Contains("showtime") || name.Equals("sho")) domain = "sho.com";
            else if (name.Contains("starz")) domain = "starz.com";
            else if (name.Contains("mgm") || name.Contains("epix")) domain = "mgmplus.com";
            else if (name.Contains("criterion")) domain = "criterionchannel.com";
            else if (name.Contains("shudder")) domain = "shudder.com";
            else if (name.Contains("britbox")) domain = "britbox.com";
            else if (name.Contains("acorn")) domain = "acorn.tv";
            else if (name.Contains("kanopy")) domain = "kanopy.com";
            else if (name.Contains("hoopla")) domain = "hoopladigital.com";
            else if (name.Contains("iplayer") || name.Contains("bbc")) domain = "bbc-iplayer.co.uk";
            else if (name.Contains("itv")) domain = "itv.com";
            else if (name.Contains("my5")) domain = "channel5.com";
            else if (name.Contains("microsoft")) domain = "microsoft.com";
            else if (name.Contains("playstation")) domain = "playstation.com";

            if (string.IsNullOrEmpty(domain) && !string.IsNullOrEmpty(source.WebUrl))
            {
                try
                {
                    string url = source.WebUrl;
                    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        url = "https://" + url;
                    }
                    var uri = new Uri(url);
                    domain = uri.Host.ToLowerInvariant();
                    if (domain.StartsWith("www.")) domain = domain.Substring(4);
                }
                catch { }
            }

            if (string.IsNullOrEmpty(domain))
            {
                domain = "netflix.com";
            }

            return $"https://www.google.com/s2/favicons?domain={domain}&sz=128";
        }

        public static List<QualityBadgeDescriptor> ComputeQualityBadges(List<WatchmodeSource>? sources, WatchmodeDetails? details)
        {
            var badges = new List<QualityBadgeDescriptor>();
            if (sources == null || sources.Count == 0) return badges;

            var formats = sources
                .Select(s => s.Format?.ToUpperInvariant() ?? "")
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct()
                .ToList();

            string highestFormat = "";
            if (formats.Contains("4K")) highestFormat = "4K";
            else if (formats.Contains("HD")) highestFormat = "HD";
            else if (formats.Contains("SD")) highestFormat = "SD";

            if (string.IsNullOrEmpty(highestFormat))
            {
                highestFormat = (details?.Year >= 2000) ? "HD" : "SD";
            }

            // 1. Resolution Badge
            if (highestFormat == "4K")
            {
                badges.Add(new QualityBadgeDescriptor("4K UHD", "4K Ultra High Definition"));
            }
            else if (highestFormat == "HD")
            {
                badges.Add(new QualityBadgeDescriptor("HD", "High Definition (1080p/720p)"));
            }
            else
            {
                badges.Add(new QualityBadgeDescriptor("SD", "Standard Definition"));
            }

            // 2. HDR / Dolby Vision Badge
            if (highestFormat == "4K")
            {
                if (details?.Year >= 2017)
                {
                    badges.Add(new QualityBadgeDescriptor("Dolby Vision", "Dolby Vision High Dynamic Range"));
                }
                else
                {
                    badges.Add(new QualityBadgeDescriptor("HDR", "High Dynamic Range"));
                }
            }

            // 3. Audio Badge
            if (details?.Year >= 1995)
            {
                if (highestFormat == "4K" && details?.Year >= 2015)
                {
                    badges.Add(new QualityBadgeDescriptor("Dolby Atmos", "Dolby Atmos Spatial Audio"));
                }
                else if (details?.Year >= 2005)
                {
                    badges.Add(new QualityBadgeDescriptor("Dolby Audio 5.1", "Dolby Digital Surround Sound"));
                }
                else
                {
                    badges.Add(new QualityBadgeDescriptor("Surround Sound", "Multi-channel Surround Sound"));
                }
            }
            else
            {
                badges.Add(new QualityBadgeDescriptor("Stereo", "Two-channel Stereo Sound"));
            }

            return badges;
        }

        public static GroupedStreamingSources GroupAndFilterSources(
            List<WatchmodeSource>? sources,
            string targetRegion,
            WatchmodeDetails? details)
        {
            var grouped = new GroupedStreamingSources();
            if (sources == null || sources.Count == 0) return grouped;

            string regionKey = (!string.IsNullOrEmpty(targetRegion) ? targetRegion : "US").ToUpperInvariant();
            var regionalSources = sources
                .Where(s => string.Equals(s.Region, regionKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Synthesize Apple TV+ source if Apple Original is omitted
            if (IsAppleOriginal(details))
            {
                bool hasDirectAppleTvSub = regionalSources.Any(s =>
                    s.Name != null &&
                    (string.Equals(s.Name, "Apple TV+", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(s.Name, "Apple TV", StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(s.Type, "sub", StringComparison.OrdinalIgnoreCase) &&
                    !s.Name.Contains("Amazon", StringComparison.OrdinalIgnoreCase) &&
                    !s.Name.Contains("Channel", StringComparison.OrdinalIgnoreCase) &&
                    !s.Name.Contains("Roku", StringComparison.OrdinalIgnoreCase));

                if (!hasDirectAppleTvSub)
                {
                    string cleanTitle = details?.Title?.Trim() ?? "";
                    string? canonicalPath = AppleTvDeepLinkHelper.GetKnownCanonicalPath(cleanTitle);
                    string targetUrl = !string.IsNullOrEmpty(canonicalPath)
                        ? $"https://tv.apple.com/{regionKey.ToLowerInvariant()}/{canonicalPath}"
                        : $"https://tv.apple.com/{regionKey.ToLowerInvariant()}/search?term={Uri.EscapeDataString(cleanTitle)}";

                    regionalSources.Add(new WatchmodeSource
                    {
                        SourceId = 350,
                        Name = "Apple TV+",
                        Type = "sub",
                        Region = regionKey,
                        WebUrl = targetUrl,
                        Format = "4K"
                    });
                }
            }

            if (regionalSources.Count == 0) return grouped;

            // Deduplicate: group by Name and category
            var deduped = regionalSources
                .GroupBy(s =>
                {
                    string t = s.Type?.ToLowerInvariant() ?? "";
                    int cat = (t == "free" || t == "free_with_ads" || t == "avod") ? 1 :
                              (t == "sub" || t == "sub_addon" || t == "tve" || t == "subscription") ? 2 : 3;
                    string normalizedName = s.Name?.ToLowerInvariant().Replace(" ", "").Replace("+", "") ?? "";
                    return (normalizedName, cat);
                })
                .Select(g => g.OrderByDescending(s => !string.IsNullOrWhiteSpace(s.WebUrl) && s.WebUrl.Length > 28)
                              .ThenByDescending(s => GetFormatPriority(s.Format))
                              .First())
                .ToList();

            grouped.SubscriptionSources = deduped
                .Where(s => s.Type == "sub" || s.Type == "sub_addon" || s.Type == "tve" || s.Type == "subscription")
                .OrderBy(s => GetProviderPriority(s, details))
                .ThenBy(s => s.Name)
                .ToList();

            grouped.FreeSources = deduped
                .Where(s => s.Type == "free" || s.Type == "free_with_ads" || s.Type == "avod")
                .OrderBy(s => GetProviderPriority(s, details))
                .ThenBy(s => s.Name)
                .ToList();

            grouped.PurchaseSources = deduped
                .Where(s => s.Type == "purchase" || s.Type == "rent" || s.Type == "buy" || s.Type == "tvod")
                .OrderBy(s => GetProviderPriority(s, details))
                .ThenBy(s => s.Name)
                .ToList();

            // Catch-all: unexpected access types go to subscription
            var accounted = new HashSet<WatchmodeSource>(
                grouped.SubscriptionSources.Concat(grouped.FreeSources).Concat(grouped.PurchaseSources));
            var remaining = deduped
                .Where(s => !accounted.Contains(s))
                .OrderBy(s => GetProviderPriority(s, details))
                .ThenBy(s => s.Name)
                .ToList();

            if (remaining.Count > 0)
            {
                grouped.SubscriptionSources.AddRange(remaining);
            }

            return grouped;
        }
    }
}
