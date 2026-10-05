using System;
using System.Collections.Generic;
using System.Linq;

namespace LumiereMediaPlayer.Helpers
{
    public record RegionQuickActionDescriptor(
        string SurpriseToolTip
    );

    public record RegionQuickFilterDescriptor(
        string TopRatedLabel,
        string TopRatedToolTip,
        string FreeLabel,
        string FreeToolTip
    );

    /// <summary>
    /// Centralized engine providing region-preferential configurations for Movies and TV Shows.
    /// Handles services, genres, access types, ratings, sorting, quick actions, and quick filters.
    /// </summary>
    public static class StreamingRegionPreferences
    {
        // ── 1. SERVICE / PROVIDERS ────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> GlobalProviderIdMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Global / Multi-Region
            { "Netflix", "203" },
            { "Prime Video", "26" },
            { "Disney+", "372" },
            { "Apple TV+", "371" },
            { "Crunchyroll", "376" },
            { "Paramount+", "444" },
            { "YouTube", "398" },

            // US
            { "Max", "387" },
            { "Hulu", "157" },
            { "Peacock", "389" },
            { "Tubi", "73" },
            { "Pluto TV", "300" },

            // India (IN)
            { "JioHotstar", "122" },
            { "Hotstar", "122" },
            { "JioCinema", "445" },
            { "SonyLIV", "381" },
            { "ZEE5", "378" },

            // United Kingdom (GB)
            { "BBC iPlayer", "108" },
            { "ITVX", "113" },
            { "Channel 4", "114" },
            { "NOW", "395" },
            { "Discovery+", "445" },

            // Canada (CA)
            { "Crave", "380" },
            { "CBC Gem", "390" },

            // Australia (AU)
            { "Stan", "383" },
            { "Binge", "443" },
            { "ABC iview", "115" }
        };

        public static List<string> GetProviderOptions(string region)
        {
            string reg = NormalizeRegion(region);
            return reg switch
            {
                "IN" => new List<string>
                {
                    "All Services", "JioHotstar", "JioCinema", "Prime Video", "Netflix", "SonyLIV", "ZEE5", "Apple TV+", "Crunchyroll"
                },
                "GB" => new List<string>
                {
                    "All Services", "BBC iPlayer", "ITVX", "Channel 4", "NOW", "Netflix", "Prime Video", "Disney+", "Apple TV+", "Paramount+", "Crunchyroll"
                },
                "CA" => new List<string>
                {
                    "All Services", "Crave", "CBC Gem", "Netflix", "Prime Video", "Disney+", "Apple TV+", "Paramount+", "Tubi", "Crunchyroll"
                },
                "AU" => new List<string>
                {
                    "All Services", "Stan", "Binge", "ABC iview", "Netflix", "Prime Video", "Disney+", "Apple TV+", "Paramount+", "Crunchyroll"
                },
                _ => new List<string> // US / Default
                {
                    "All Services", "Netflix", "Prime Video", "Disney+", "Max", "Hulu", "Apple TV+", "Paramount+", "Peacock", "Tubi", "Pluto TV", "Crunchyroll"
                }
            };
        }

        public static string? GetProviderId(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName) || providerName.Equals("All Services", StringComparison.OrdinalIgnoreCase))
                return null;

            if (GlobalProviderIdMap.TryGetValue(providerName, out var id))
                return id;

            return null;
        }

        public static int GetRegionalProviderPriority(string providerName, string? region)
        {
            if (string.IsNullOrWhiteSpace(providerName)) return 50;
            string lower = providerName.ToLowerInvariant();
            string reg = NormalizeRegion(region);

            if (reg == "IN")
            {
                if (lower.Contains("hotstar")) return 1;
                if (lower.Contains("jiocinema") || lower.Contains("jio cinema")) return 2;
                if (lower.Contains("prime") || lower.Contains("amazon")) return 3;
                if (lower.Contains("netflix")) return 4;
                if (lower.Contains("sonyliv") || lower.Contains("sony liv")) return 5;
                if (lower.Contains("zee5") || lower.Contains("zee 5")) return 6;
                if (lower.Contains("apple")) return 7;
                if (lower.Contains("crunchyroll")) return 8;
                return 30;
            }
            if (reg == "GB")
            {
                if (lower.Contains("iplayer") || lower.Contains("bbc")) return 1;
                if (lower.Contains("itv")) return 2;
                if (lower.Contains("channel 4") || lower.Contains("all 4")) return 3;
                if (lower.Contains("now")) return 4;
                if (lower.Contains("netflix")) return 5;
                if (lower.Contains("prime") || lower.Contains("amazon")) return 6;
                if (lower.Contains("disney")) return 7;
                if (lower.Contains("apple")) return 8;
                if (lower.Contains("paramount")) return 9;
                return 30;
            }
            if (reg == "CA")
            {
                if (lower.Contains("crave")) return 1;
                if (lower.Contains("cbc") || lower.Contains("gem")) return 2;
                if (lower.Contains("netflix")) return 3;
                if (lower.Contains("prime") || lower.Contains("amazon")) return 4;
                if (lower.Contains("disney")) return 5;
                if (lower.Contains("apple")) return 6;
                if (lower.Contains("paramount")) return 7;
                return 30;
            }
            if (reg == "AU")
            {
                if (lower.Contains("stan")) return 1;
                if (lower.Contains("binge")) return 2;
                if (lower.Contains("iview") || lower.Contains("abc")) return 3;
                if (lower.Contains("netflix")) return 4;
                if (lower.Contains("prime") || lower.Contains("amazon")) return 5;
                if (lower.Contains("disney")) return 6;
                if (lower.Contains("apple")) return 7;
                return 30;
            }

            // US / Default
            if (lower.Contains("apple")) return 1;
            if (lower.Contains("netflix")) return 2;
            if (lower.Contains("prime") || lower.Contains("amazon")) return 3;
            if (lower.Contains("disney")) return 4;
            if (lower.Contains("max") || lower.Contains("hbo")) return 5;
            if (lower.Contains("hulu")) return 6;
            if (lower.Contains("paramount")) return 7;
            if (lower.Contains("peacock")) return 8;
            if (lower.Contains("youtube")) return 9;
            if (lower.Contains("vudu") || lower.Contains("fandango")) return 10;
            if (lower.Contains("tubi")) return 11;
            if (lower.Contains("pluto")) return 12;

            return 40;
        }

        // ── 2. TV NETWORKS ────────────────────────────────────────────────────────────────────

        private static readonly Dictionary<string, string> GlobalNetworkIdMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "HBO", "4" },
            { "Netflix", "233" },
            { "AMC", "1" },
            { "FX", "33" },
            { "BBC One", "6" },
            { "BBC Two", "7" },
            { "Showtime", "15" },
            { "CBS", "13" },
            { "ABC", "10" },
            { "NBC", "12" },
            { "The CW", "17" },
            { "Fox", "14" },
            { "Syfy", "27" },
            { "ITV", "8" },
            { "Channel 4", "9" },
            { "Sky Atlantic", "18" },
            { "Channel 5", "19" },
            { "Star Plus", "210" },
            { "Zee TV", "211" },
            { "Sony Entertainment", "212" },
            { "Colors", "213" },
            { "CTV", "34" },
            { "CBC", "35" },
            { "Global TV", "36" },
            { "SBS", "38" },
            { "Nine Network", "39" },
            { "Seven Network", "40" },
            { "Network 10", "41" }
        };

        public static List<string> GetNetworkOptions(string region)
        {
            string reg = NormalizeRegion(region);
            return reg switch
            {
                "IN" => new List<string>
                {
                    "All Networks", "Star Plus", "Zee TV", "Sony Entertainment", "Colors", "Netflix", "HBO", "BBC One"
                },
                "GB" => new List<string>
                {
                    "All Networks", "BBC One", "BBC Two", "ITV", "Channel 4", "Sky Atlantic", "Channel 5", "Netflix", "HBO"
                },
                "CA" => new List<string>
                {
                    "All Networks", "CTV", "CBC", "Global TV", "HBO", "Netflix", "AMC", "FX"
                },
                "AU" => new List<string>
                {
                    "All Networks", "ABC", "SBS", "Nine Network", "Seven Network", "Network 10", "HBO", "Netflix"
                },
                _ => new List<string> // US / Default
                {
                    "All Networks", "HBO", "Netflix", "AMC", "FX", "CBS", "NBC", "ABC", "Fox", "Showtime", "The CW", "Syfy"
                }
            };
        }

        public static string? GetNetworkId(string networkName)
        {
            if (string.IsNullOrWhiteSpace(networkName) || networkName.Equals("All Networks", StringComparison.OrdinalIgnoreCase))
                return null;

            if (GlobalNetworkIdMap.TryGetValue(networkName, out var id))
                return id;

            return null;
        }

        // ── 3. GENRES (ORDERED BY REGIONAL PREFERENCE) ──────────────────────────────────────────

        public static readonly Dictionary<string, int> GenreMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Action", 1 },
            { "Adventure", 2 },
            { "Animation", 3 },
            { "Comedy", 4 },
            { "Crime", 5 },
            { "Documentary", 6 },
            { "Drama", 7 },
            { "Family", 8 },
            { "Fantasy", 9 },
            { "History", 10 },
            { "Horror", 11 },
            { "Music", 12 },
            { "Mystery", 13 },
            { "Romance", 14 },
            { "Science Fiction", 15 },
            { "Thriller", 17 },
            { "War", 18 },
            { "Western", 19 }
        };

        public static List<string> GetGenreOptions(string region)
        {
            string reg = NormalizeRegion(region);
            return reg switch
            {
                "IN" => new List<string>
                {
                    "All Genres", "Drama", "Action", "Comedy", "Romance", "Thriller", "Crime", "Music", "Family", "Fantasy", "Mystery", "Animation", "Adventure", "History", "Horror", "Science Fiction", "War", "Documentary", "Western"
                },
                "GB" => new List<string>
                {
                    "All Genres", "Crime", "Drama", "Mystery", "Comedy", "Documentary", "History", "Thriller", "Action", "Adventure", "Science Fiction", "Romance", "Family", "Animation", "War", "Horror", "Fantasy", "Music", "Western"
                },
                "CA" => new List<string>
                {
                    "All Genres", "Drama", "Comedy", "Action", "Documentary", "Science Fiction", "Thriller", "Crime", "Animation", "Adventure", "Family", "Horror", "Mystery", "Romance", "History", "Fantasy", "War", "Music", "Western"
                },
                "AU" => new List<string>
                {
                    "All Genres", "Action", "Drama", "Comedy", "Crime", "Thriller", "Documentary", "Science Fiction", "Adventure", "Horror", "Mystery", "Animation", "Family", "Romance", "War", "History", "Fantasy", "Music", "Western"
                },
                _ => new List<string> // US / Default
                {
                    "All Genres", "Action", "Comedy", "Science Fiction", "Drama", "Horror", "Animation", "Thriller", "Adventure", "Crime", "Fantasy", "Family", "Mystery", "Romance", "Documentary", "War", "History", "Music", "Western"
                }
            };
        }

        // ── 4. ACCESS TYPES ───────────────────────────────────────────────────────────────────

        public static List<string> GetAccessTypeOptions(string region)
        {
            string reg = NormalizeRegion(region);
            return reg switch
            {
                "IN" => new List<string>
                {
                    "All Access Types", "Free (AVOD)", "Subscription", "Rent or Buy"
                },
                "GB" => new List<string>
                {
                    "All Access Types", "Subscription", "Free to Stream", "Rent or Buy"
                },
                "CA" or "AU" => new List<string>
                {
                    "All Access Types", "Subscription", "Free to Stream", "Rent or Buy"
                },
                _ => new List<string> // US / Default
                {
                    "All Access Types", "Subscription", "Free with Ads", "Rent or Buy"
                }
            };
        }

        public static string MapAccessTypeToWatchmodeParam(string accessType)
        {
            if (string.IsNullOrWhiteSpace(accessType) || accessType.StartsWith("All", StringComparison.OrdinalIgnoreCase))
                return "sub,free,rent,buy";

            if (accessType.Contains("Free", StringComparison.OrdinalIgnoreCase))
                return "free";

            if (accessType.Equals("Subscription", StringComparison.OrdinalIgnoreCase))
                return "sub";

            if (accessType.Contains("Rent", StringComparison.OrdinalIgnoreCase) || accessType.Contains("Buy", StringComparison.OrdinalIgnoreCase))
                return "rent,buy";

            return "sub,free,rent,buy";
        }

        // ── 5. RATINGS (SCORE & REGIONAL CERTIFICATION) ────────────────────────────────────────

        public static List<string> GetRatingOptions(string region, bool isTvShow = false)
        {
            string reg = NormalizeRegion(region);
            var baseOptions = new List<string>
            {
                "All Ratings",
                "⭐ Top Rated (8.0+)",
                "⭐ Highly Rated (7.0+)",
                "⭐ Popular (6.0+)"
            };

            switch (reg)
            {
                case "IN":
                    baseOptions.Add("Universal (U)");
                    baseOptions.Add("Guidance (UA 7+ / 13+)");
                    baseOptions.Add("Young Adults (UA 16+)");
                    baseOptions.Add("Adults Only (A)");
                    break;

                case "GB":
                    baseOptions.Add("Universal (U / PG)");
                    baseOptions.Add("Teens (12 / 12A)");
                    baseOptions.Add("Older Teens (15)");
                    baseOptions.Add("Adults (18)");
                    break;

                case "CA":
                    baseOptions.Add("General (G / PG)");
                    baseOptions.Add("Teens (14A)");
                    baseOptions.Add("Mature (18A / R)");
                    break;

                case "AU":
                    baseOptions.Add("General (G / PG)");
                    baseOptions.Add("Mature Guidance (M)");
                    baseOptions.Add("Restricted (MA 15+ / R 18+)");
                    break;

                default: // US
                    if (isTvShow)
                    {
                        baseOptions.Add("Family (TV-G / TV-PG)");
                        baseOptions.Add("Teens (TV-14)");
                        baseOptions.Add("Mature (TV-MA)");
                    }
                    else
                    {
                        baseOptions.Add("Family (G / PG)");
                        baseOptions.Add("Teens (PG-13)");
                        baseOptions.Add("Mature (R / NC-17)");
                    }
                    break;
            }

            return baseOptions;
        }

        public static double? ParseMinUserRating(string ratingOption)
        {
            if (string.IsNullOrWhiteSpace(ratingOption)) return null;

            if (ratingOption.Contains("8.0")) return 8.0;
            if (ratingOption.Contains("7.0")) return 7.0;
            if (ratingOption.Contains("6.0")) return 6.0;

            return null;
        }

        public static bool MatchesCertification(string? ageRating, string selectedOption, string region)
        {
            if (string.IsNullOrWhiteSpace(selectedOption) || selectedOption.StartsWith("All", StringComparison.OrdinalIgnoreCase) || selectedOption.Contains("⭐"))
                return true;

            if (string.IsNullOrWhiteSpace(ageRating))
                return true; // Don't exclude items with missing rating data

            string clean = ageRating.ToUpperInvariant().Trim();
            string opt = selectedOption.ToLowerInvariant();

            // Universal / Family / General
            if (opt.Contains("universal") || opt.Contains("family") || opt.Contains("general"))
            {
                return clean.Contains("G") || clean.Contains("PG") || clean.Contains("TV-Y") || clean.Contains("TV-G") || clean.Contains("U");
            }

            // Teens / Guidance / 12 / 14
            if (opt.Contains("teens") || opt.Contains("guidance") || opt.Contains("12") || opt.Contains("14"))
            {
                return clean.Contains("PG-13") || clean.Contains("TV-14") || clean.Contains("12") || clean.Contains("14") || clean.Contains("UA") || clean.Contains("M");
            }

            // Older Teens / 15 / 16
            if (opt.Contains("older teens") || opt.Contains("15") || opt.Contains("16"))
            {
                return clean.Contains("15") || clean.Contains("16") || clean.Contains("MA");
            }

            // Mature / Adults / Restricted / 18
            if (opt.Contains("mature") || opt.Contains("adults") || opt.Contains("restricted") || opt.Contains("18"))
            {
                return clean.Contains("R") || clean.Contains("NC-17") || clean.Contains("TV-MA") || clean.Contains("18") || clean.Contains("A");
            }

            return true;
        }

        // ── 6. SORT BY ────────────────────────────────────────────────────────────────────────

        public static List<string> GetSortOptions(string region = "")
        {
            return new List<string>
            {
                "Popularity", "Trending Now", "New Releases", "Highest Rated", "Classic Releases"
            };
        }

        public static string MapSortOptionToWatchmodeParam(string sortOption)
        {
            if (string.IsNullOrWhiteSpace(sortOption)) return "popularity_desc";
            string lower = sortOption.ToLowerInvariant();

            if (lower.Contains("new") || lower.Contains("release date")) return "release_date_desc";
            if (lower.Contains("highest") || lower.Contains("user rating")) return "user_rating_desc";
            if (lower.Contains("classic")) return "release_date_asc";

            return "popularity_desc";
        }

        // ── 7. QUICK ACTIONS & QUICK FILTERS ──────────────────────────────────────────────────

        public static RegionQuickActionDescriptor GetQuickActions(string region, bool isTvShow = false)
        {
            string contentType = isTvShow ? "Show" : "Movie";
            string reg = NormalizeRegion(region);
            string regionName = reg switch
            {
                "IN" => "Indian / Regional",
                "GB" => "UK / British",
                "CA" => "Canadian",
                "AU" => "Australian",
                _ => ""
            };

            string tooltip = string.IsNullOrEmpty(regionName)
                ? $"Surprise Me (Pick a Random {contentType})"
                : $"Surprise Me (Pick a Random {regionName} {contentType})";

            return new RegionQuickActionDescriptor(SurpriseToolTip: tooltip);
        }

        public static RegionQuickFilterDescriptor GetQuickFilters(string region, bool isTvShow = false)
        {
            string reg = NormalizeRegion(region);
            string contentPlural = isTvShow ? "Shows" : "Movies";

            return reg switch
            {
                "IN" => new RegionQuickFilterDescriptor(
                    TopRatedLabel: "Top Rated",
                    TopRatedToolTip: $"Show 8.0+ Rated {contentPlural}",
                    FreeLabel: "Free Streams",
                    FreeToolTip: $"Show Free to Stream (AVOD) {contentPlural} in India"
                ),
                "GB" => new RegionQuickFilterDescriptor(
                    TopRatedLabel: "Top Rated",
                    TopRatedToolTip: $"Show 8.0+ Rated {contentPlural}",
                    FreeLabel: "Free on iPlayer",
                    FreeToolTip: $"Show Free to Stream {contentPlural} in the UK"
                ),
                "CA" => new RegionQuickFilterDescriptor(
                    TopRatedLabel: "Top Rated",
                    TopRatedToolTip: $"Show 8.0+ Rated {contentPlural}",
                    FreeLabel: "Free on Gem",
                    FreeToolTip: $"Show Free to Stream {contentPlural} in Canada"
                ),
                "AU" => new RegionQuickFilterDescriptor(
                    TopRatedLabel: "Top Rated",
                    TopRatedToolTip: $"Show 8.0+ Rated {contentPlural}",
                    FreeLabel: "Free on iview",
                    FreeToolTip: $"Show Free to Stream {contentPlural} in Australia"
                ),
                _ => new RegionQuickFilterDescriptor(
                    TopRatedLabel: "Top Rated",
                    TopRatedToolTip: $"Show 8.0+ Rated {contentPlural}",
                    FreeLabel: "Free with Ads",
                    FreeToolTip: $"Show Free to Stream (AVOD) {contentPlural}"
                )
            };
        }

        public static string NormalizeRegion(string? region)
        {
            if (string.IsNullOrWhiteSpace(region)) return "US";
            var upper = region.Trim().ToUpperInvariant();
            if (upper is "US" or "GB" or "IN" or "CA" or "AU") return upper;
            return "US";
        }
    }
}
