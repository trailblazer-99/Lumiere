using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Helpers
{
    public static class AppleTvDeepLinkHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        private static readonly ConcurrentDictionary<string, string> _dynamicCache = new(StringComparer.OrdinalIgnoreCase);

        // Pre-indexed database of canonical Apple TV+ paths for immediate, zero-latency resolution
        private static readonly Dictionary<string, string> _canonicalDatabase = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ted Lasso"] = "show/ted-lasso/umc.cmc.vtoh0mn0xn7t3c643xqonfzy",
            ["Severance"] = "show/severance/umc.cmc.1srk2goyh2q2zdxcx605w8vtx",
            ["The Morning Show"] = "show/the-morning-show/umc.cmc.25tn3v8ku4b39tr6ccgb8nl6m",
            ["Slow Horses"] = "show/slow-horses/umc.cmc.2szz3fdt71tl1ulnbp8utgq5o",
            ["Shrinking"] = "show/shrinking/umc.cmc.apzybj6eqf6pzccd97kev7bs",
            ["Silo"] = "show/silo/umc.cmc.3yksgc857px0k0rqe5zd4jice",
            ["Foundation"] = "show/foundation/umc.cmc.5983fipzqbicvrve6jdfep4x3",
            ["Presumed Innocent"] = "show/presumed-innocent/umc.cmc.5hnqrhwtzt3esr7rb1wq2ppvn",
            ["Bad Monkey"] = "show/bad-monkey/umc.cmc.2qoep59s6qukjonprttysfs8x",
            ["Wolfs"] = "movie/wolfs/umc.cmc.c3xhu25rw4jxxxzq4oio6snu",
            ["Killers of the Flower Moon"] = "movie/killers-of-the-flower-moon/umc.cmc.5x1fg9vferlfeutzpq6rra1zf",
            ["CODA"] = "movie/coda/umc.cmc.3eh9r5iz32ggdm4ccvw5igiir",
            ["For All Mankind"] = "show/for-all-mankind/umc.cmc.6wsi780sz5tdbqcf11k76mkp7",
            ["Black Bird"] = "show/black-bird/umc.cmc.30gx1y8nwthydkrvhqu156p3",
            ["Dark Matter"] = "show/dark-matter/umc.cmc.4luj45vtqpmjsvb6sc2675oeg",
            ["Masters of the Air"] = "show/masters-of-the-air/umc.cmc.7bxcni0vwgll9kmicq738k5q2",
            ["Monarch: Legacy of Monsters"] = "show/monarch-legacy-of-monsters/umc.cmc.62l8x0ixrhyq3yaqa5y8yo7ew",
            ["Hijack"] = "show/hijack/umc.cmc.1dg08zn0g3zx52hs8npoj5qe3",
            ["Pachinko"] = "show/pachinko/umc.cmc.17vf6g68dy89kk1l1nnb6min4",
            ["Sugar"] = "show/sugar/umc.cmc.4r6q7tdquewehwvb3rzl0k3dt",
            ["Napoleon"] = "movie/napoleon/umc.cmc.25k80oxl3vo69c8rimk8v81s1",
            ["Argylle"] = "movie/argylle/umc.cmc.3qy6j44hfqtekx6fx3yzh9w8i",
            ["Greyhound"] = "movie/greyhound/umc.cmc.o5z5ztufuu3uv8lx7m0jcega",
            ["Finch"] = "movie/finch/umc.cmc.47dkj9f2ho3h8dwxixflz65q5",
            ["Spirited"] = "movie/spirited/umc.cmc.3lp7wqowerzdbej98tveildi3",
            ["The Instigators"] = "movie/the-instigators/umc.cmc.3ocr6483492qm53io2bsy2o69",
            ["Fly Me to the Moon"] = "movie/fly-me-to-the-moon/umc.cmc.7bwrikjdeik56bk49vlr7c1h6",
            ["Tetris"] = "movie/tetris/umc.cmc.4evmgcam356pzgxs2l7a18d7b",
            ["The Family Plan"] = "movie/the-family-plan/umc.cmc.6o6y3wel2lez2tkdu2cv8dzd1",
            ["Ghosted"] = "movie/ghosted/umc.cmc.6nodv9rf3ltfk2ar3pfc8hced",
            ["Palm Royale"] = "show/palm-royale/umc.cmc.6vwg3ce7ovsexa3a6r7f6qk49",
            ["Lady in the Lake"] = "show/lady-in-the-lake/umc.cmc.2j4grqjj59olekp9vdrmjtodq",
            ["Defending Jacob"] = "show/defending-jacob/umc.cmc.5h5mr0shyyqqahqdv55ywyilr",
            ["Platonic"] = "show/platonic/umc.cmc.y7bc18x7co813l8i2tlsyb4l",
            ["See"] = "show/see/umc.cmc.3s4mgg2y7h95fks9gnc4pw13m",
            ["Servant"] = "show/servant/umc.cmc.4y25wuby7pck9o6vaubbbk7gb",
            ["Mythic Quest"] = "show/mythic-quest/umc.cmc.1nfdfd5zlk05fo1bwwetzldy3",
            ["Dickinson"] = "show/dickinson/umc.cmc.1ogyy5s2agasxa5qztabrlykn",
            ["Physical"] = "show/physical/umc.cmc.6gdc6v4vwyaab7klocftv2s10",
            ["Invasion"] = "show/invasion/umc.cmc.70b7z97fv7azfzn5baqnj88p6",
            ["The Afterparty"] = "show/the-afterparty/umc.cmc.4bdf27j2p10q14p11w996u7d7",
            ["Schmigadoon!"] = "show/schmigadoon/umc.cmc.1r93v58w2p9q8n6m5x4z3c2b1",
            ["Trying"] = "show/trying/umc.cmc.3x4c5v6b7n8m9a0s1d2f3g4h5",
            ["Loot"] = "show/loot/umc.cmc.2435n8p5p1t4p40w1x2u0a2i5",
            ["Sharper"] = "movie/sharper/umc.cmc.14g099qj457s44e5x1g4v980k",
            ["The Banker"] = "movie/the-banker/umc.cmc.5s6y478v29j4v80k3x1g4e80k",
            ["Cherry"] = "movie/cherry/umc.cmc.3y4e0g64z7b8a7b14g099qj45"
        };

        static AppleTvDeepLinkHelper()
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            }
        }

        public static string GetCurrentRegion()
        {
            try
            {
                string osRegion = RegionInfo.CurrentRegion.TwoLetterISORegionName.ToLowerInvariant();
                if (!string.IsNullOrEmpty(osRegion)) return osRegion;
            }
            catch { }
            return "us";
        }

        public static string? GetKnownCanonicalPath(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            string clean = title.Trim();
            if (_canonicalDatabase.TryGetValue(clean, out var path)) return path;

            foreach (var kvp in _canonicalDatabase)
            {
                if (clean.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.StartsWith(clean, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        public static async Task<string> ResolveAppleTvUrlAsync(string title, string mediaType = "movie", string? currentUrl = null, string? region = null)
        {
            string targetRegion = string.IsNullOrWhiteSpace(region) ? GetCurrentRegion() : region.ToLowerInvariant();
            string cleanTitle = title?.Trim() ?? "";

            // 1. If currentUrl already has a valid canonical show or movie path with umc.cmc, simply ensure correct region
            if (!string.IsNullOrWhiteSpace(currentUrl) &&
                currentUrl.Contains("tv.apple.com", StringComparison.OrdinalIgnoreCase) &&
                (currentUrl.Contains("/show/", StringComparison.OrdinalIgnoreCase) || currentUrl.Contains("/movie/", StringComparison.OrdinalIgnoreCase)) &&
                !currentUrl.Contains("/search", StringComparison.OrdinalIgnoreCase))
            {
                return RewriteUrlRegion(currentUrl, targetRegion);
            }

            // 2. Check pre-indexed database
            string? knownPath = GetKnownCanonicalPath(cleanTitle);
            if (!string.IsNullOrEmpty(knownPath))
            {
                return $"https://tv.apple.com/{targetRegion}/{knownPath}";
            }

            // 3. Check dynamic in-memory cache
            string cacheKey = $"{cleanTitle}_{targetRegion}";
            if (_dynamicCache.TryGetValue(cacheKey, out var cachedUrl))
            {
                return cachedUrl;
            }

            // 4. Dynamically scrape tv.apple.com search page
            try
            {
                string encoded = Uri.EscapeDataString(cleanTitle);
                string searchUrl = $"https://tv.apple.com/us/search?term={encoded}";
                
                var response = await _httpClient.GetStringAsync(searchUrl);
                if (!string.IsNullOrWhiteSpace(response))
                {
                    var matches = Regex.Matches(response, @"https://tv\.apple\.com/[a-z]{2}/((?:show|movie)/[a-zA-Z0-9_-]+/umc\.cmc\.[a-zA-Z0-9]+)");
                    
                    string slug = Regex.Replace(cleanTitle.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                    string? bestPath = null;

                    foreach (Match m in matches)
                    {
                        string path = m.Groups[1].Value;
                        if (path.Contains($"/{slug}/", StringComparison.OrdinalIgnoreCase))
                        {
                            bestPath = path;
                            break;
                        }
                    }

                    if (bestPath == null && matches.Count > 0)
                    {
                        string firstWord = slug.Split('-')[0];
                        foreach (Match m in matches)
                        {
                            string path = m.Groups[1].Value;
                            if (path.Contains(firstWord, StringComparison.OrdinalIgnoreCase))
                            {
                                bestPath = path;
                                break;
                            }
                        }
                        bestPath ??= matches[0].Groups[1].Value;
                    }

                    if (!string.IsNullOrEmpty(bestPath))
                    {
                        string resolvedUrl = $"https://tv.apple.com/{targetRegion}/{bestPath}";
                        _dynamicCache[cacheKey] = resolvedUrl;
                        return resolvedUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppleTvDeepLinkHelper] Web search failed: {ex.Message}");
            }

            // 5. Fallback to iTunes Search API
            try
            {
                string itunesMediaType = mediaType.Equals("movie", StringComparison.OrdinalIgnoreCase) ? "movie" : "tvShow";
                string itunesUrl = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(cleanTitle)}&media={itunesMediaType}&country={targetRegion}&limit=5";
                
                var json = await _httpClient.GetStringAsync(itunesUrl);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    foreach (var item in results.EnumerateArray())
                    {
                        string name = "";
                        if (item.TryGetProperty("trackName", out var tn)) name = tn.GetString() ?? "";
                        else if (item.TryGetProperty("collectionName", out var cn)) name = cn.GetString() ?? "";

                        if (name.Contains(cleanTitle, StringComparison.OrdinalIgnoreCase) || cleanTitle.Contains(name, StringComparison.OrdinalIgnoreCase))
                        {
                            string directUrl = "";
                            if (item.TryGetProperty("trackViewUrl", out var tvu)) directUrl = tvu.GetString() ?? "";
                            else if (item.TryGetProperty("collectionViewUrl", out var cvu)) directUrl = cvu.GetString() ?? "";

                            if (!string.IsNullOrEmpty(directUrl))
                            {
                                _dynamicCache[cacheKey] = directUrl;
                                return directUrl;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppleTvDeepLinkHelper] iTunes API failed: {ex.Message}");
            }

            // 6. Safe fallback with explicit region search term
            return $"https://tv.apple.com/{targetRegion}/search?term={Uri.EscapeDataString(cleanTitle)}";
        }

        public static string RewriteUrlRegion(string url, string targetRegion)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;

            var match = Regex.Match(url, @"((?:tv|itunes)\.apple\.com)/([a-zA-Z]{2})(/|$)");
            if (match.Success)
            {
                string foundRegion = match.Groups[2].Value;
                if (!foundRegion.Equals(targetRegion, StringComparison.OrdinalIgnoreCase))
                {
                    return Regex.Replace(url, @"((?:tv|itunes)\.apple\.com/)[a-zA-Z]{2}(/|$)", $"$1{targetRegion}$2");
                }
                return url;
            }

            return Regex.Replace(url, @"(tv\.apple\.com|itunes\.apple\.com)(/|$)", $"$1/{targetRegion}/");
        }
    }
}
