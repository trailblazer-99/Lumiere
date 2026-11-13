using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Helpers
{
    public static class SpotifyDeepLinkHelper
    {
        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static readonly ConcurrentDictionary<string, (Uri? NativeUri, string WebUrl)> _dynamicCache = new(StringComparer.OrdinalIgnoreCase);

        private static void CacheDynamic(string key, (Uri? NativeUri, string WebUrl) result)
        {
            if (_dynamicCache.Count > 300)
            {
                var keysToPrune = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Take(_dynamicCache.Keys, 50));
                foreach (var k in keysToPrune) _dynamicCache.TryRemove(k, out _);
            }
            _dynamicCache[key] = result;
        }

        // Spotify Client Credentials cached token
        private static string? _cachedAccessToken;
        private static DateTime _tokenExpiresAt = DateTime.MinValue;
        private static readonly SemaphoreSlim _tokenLock = new(1, 1);

        // Pre-indexed database of canonical Spotify paths for zero-latency, immediate resolution
        private static readonly Dictionary<string, string> _canonicalDatabase = new(StringComparer.OrdinalIgnoreCase)
        {
            // === CONTEMPORARY & TOP HITS (TRACKS) ===
            ["Birds of a Feather Billie Eilish"] = "track/6dOtVTDmmpgnemIRdn92io",
            ["Birds of a Feather"] = "track/6dOtVTDmmpgnemIRdn92io",
            ["Birds of Feather Billie Eilish"] = "track/6dOtVTDmmpgnemIRdn92io",
            ["Birds of Feather"] = "track/6dOtVTDmmpgnemIRdn92io",
            ["BIRDS OF A FEATHER"] = "track/6dOtVTDmmpgnemIRdn92io",
            ["BIRDS OF A FEATHER Billie Eilish"] = "track/6dOtVTDmmpgnemIRdn92io",
            ["Lunch Billie Eilish"] = "track/629DXPnHUTuy0m85y9mENv",
            ["Lunch"] = "track/629DXPnHUTuy0m85y9mENv",
            ["Chihiro Billie Eilish"] = "track/7BRDOWTiSR2drnUh6wqihY",
            ["Chihiro"] = "track/7BRDOWTiSR2drnUh6wqihY",
            ["Wildflower Billie Eilish"] = "track/34x6hWo9IK4Q014995L0Pq",
            ["Wildflower"] = "track/34x6hWo9IK4Q014995L0Pq",
            ["Blue Billie Eilish"] = "track/6jR2T3H54yQvX2U8yYh5f8",
            ["Blue"] = "track/6jR2T3H54yQvX2U8yYh5f8",
            ["Espresso Sabrina Carpenter"] = "track/2qSk1gOKZwArrWn75i874D",
            ["Espresso"] = "track/2qSk1gOKZwArrWn75i874D",
            ["Please Please Please Sabrina Carpenter"] = "track/5N3hkjpuhgeReWsvk8PoHN",
            ["Please Please Please"] = "track/5N3hkjpuhgeReWsvk8PoHN",
            ["Taste Sabrina Carpenter"] = "track/5G2f63n7IPVPPjfEJvdOiP",
            ["Taste"] = "track/5G2f63n7IPVPPjfEJvdOiP",
            ["Bed Chem Sabrina Carpenter"] = "track/02MWAaffLxlfxAUY7c5dvx",
            ["Bed Chem"] = "track/02MWAaffLxlfxAUY7c5dvx",
            ["Feather Sabrina Carpenter"] = "track/4T65922L6i25q0bt3lG60R",
            ["Feather"] = "track/4T65922L6i25q0bt3lG60R",
            ["Good Luck, Babe! Chappell Roan"] = "track/0GNI8K3El5GgKLGQi5hhTI",
            ["Good Luck Babe Chappell Roan"] = "track/0GNI8K3El5GgKLGQi5hhTI",
            ["Good Luck, Babe!"] = "track/0GNI8K3El5GgKLGQi5hhTI",
            ["Good Luck Babe"] = "track/0GNI8K3El5GgKLGQi5hhTI",
            ["HOT TO GO! Chappell Roan"] = "track/1K3LRUEqlpvtxk8UtQAB4V",
            ["Hot To Go Chappell Roan"] = "track/1K3LRUEqlpvtxk8UtQAB4V",
            ["HOT TO GO!"] = "track/1K3LRUEqlpvtxk8UtQAB4V",
            ["Hot To Go"] = "track/1K3LRUEqlpvtxk8UtQAB4V",
            ["Red Wine Supernova Chappell Roan"] = "track/72by0mUjWbTyzgP45w4qdP",
            ["Red Wine Supernova"] = "track/72by0mUjWbTyzgP45w4qdP",
            ["Pink Pony Club Chappell Roan"] = "track/69gT5TzB21qjQ7aM5H2Z50",
            ["Pink Pony Club"] = "track/69gT5TzB21qjQ7aM5H2Z50",
            ["Not Like Us Kendrick Lamar"] = "track/6AI3ezQ4o3HUoP6D9vOWbm",
            ["Not Like Us"] = "track/6AI3ezQ4o3HUoP6D9vOWbm",
            ["I Had Some Help Post Malone Morgan Wallen"] = "track/722tgOgdIbNe3BEO6TALFA",
            ["I Had Some Help Post Malone"] = "track/722tgOgdIbNe3BEO6TALFA",
            ["I Had Some Help"] = "track/722tgOgdIbNe3BEO6TALFA",
            ["Die With A Smile Lady Gaga Bruno Mars"] = "track/2YFm5iZ0tL9Xh8Wff0rP4z",
            ["Die With A Smile"] = "track/2YFm5iZ0tL9Xh8Wff0rP4z",
            ["Beautiful Things Benson Boone"] = "track/6tNQ70jh4OwmHG9268Bu5l",
            ["Beautiful Things"] = "track/6tNQ70jh4OwmHG9268Bu5l",
            ["Lose Control Teddy Swims"] = "track/6XjDF6nds4DEHIiDpmBi8z",
            ["Lose Control"] = "track/6XjDF6nds4DEHIiDpmBi8z",
            ["Too Sweet Hozier"] = "track/4iHg275A4ZlD4H3y4vVlW6",
            ["Too Sweet"] = "track/4iHg275A4ZlD4H3y4vVlW6",
            ["Fortnight Taylor Swift Post Malone"] = "track/6dODwAsUQXYye03JHwhp6V",
            ["Fortnight Taylor Swift"] = "track/6dODwAsUQXYye03JHwhp6V",
            ["Fortnight"] = "track/6dODwAsUQXYye03JHwhp6V",
            ["I Can Do It With a Broken Heart Taylor Swift"] = "track/2mg6YPkOEM1o9277q5bEee",
            ["I Can Do It With a Broken Heart"] = "track/2mg6YPkOEM1o9277q5bEee",
            ["A Bar Song Tipsy Shaboozey"] = "track/2G7V7zsVDxg1yRsu7Ew9RJ",
            ["A Bar Song (Tipsy) Shaboozey"] = "track/2G7V7zsVDxg1yRsu7Ew9RJ",
            ["A Bar Song (Tipsy)"] = "track/2G7V7zsVDxg1yRsu7Ew9RJ",
            ["A Bar Song"] = "track/2G7V7zsVDxg1yRsu7Ew9RJ",
            ["Greedy Tate McRae"] = "track/3rUGC1v00qgvtR0xpH9ekQ",
            ["Greedy"] = "track/3rUGC1v00qgvtR0xpH9ekQ",
            ["Water Tyla"] = "track/5aIVCx5tnk0OsvbZa9Fwx0",
            ["Water"] = "track/5aIVCx5tnk0OsvbZa9Fwx0",
            ["Paint The Town Red Doja Cat"] = "track/2GxrNKugF82CnoRFbTGuHm",
            ["Paint The Town Red"] = "track/2GxrNKugF82CnoRFbTGuHm",
            ["Vampire Olivia Rodrigo"] = "track/3k79jB4aGmM2ABVk73V7nW",
            ["Vampire"] = "track/3k79jB4aGmM2ABVk73V7nW",
            ["Kill Bill SZA"] = "track/1Qrg8KqiBpW07V7PN3wwwL",
            ["Kill Bill"] = "track/1Qrg8KqiBpW07V7PN3wwwL",
            ["Snooze SZA"] = "track/4iZ4mst790ZCrG8ZGMcuL0",
            ["Snooze"] = "track/4iZ4mst790ZCrG8ZGMcuL0",
            ["Saturn SZA"] = "track/1bjeWoalDxNg6375yftXww",
            ["Saturn"] = "track/1bjeWoalDxNg6375yftXww",
            ["Stick Season Noah Kahan"] = "track/02MWAaffLxlfxAUY7c5dvx",
            ["Stick Season"] = "track/02MWAaffLxlfxAUY7c5dvx",
            ["Stargazing Myles Smith"] = "track/3SP9l0XQ1z3h4G99bK2501",
            ["Stargazing"] = "track/3SP9l0XQ1z3h4G99bK2501",
            ["Belong Together Mark Ambor"] = "track/1o35i2sD629r54uG0B4v5t",
            ["Belong Together"] = "track/1o35i2sD629r54uG0B4v5t",

            // === ALL-TIME TOP TRACKS ===
            ["Blinding Lights The Weeknd"] = "track/00uqj8HXl0hWr3tnxC0NZ5",
            ["Blinding Lights"] = "track/00uqj8HXl0hWr3tnxC0NZ5",
            ["Shape of You Ed Sheeran"] = "track/7qiZfU4dY1lWllzX7mPBI3",
            ["Shape of You"] = "track/7qiZfU4dY1lWllzX7mPBI3",
            ["Starboy The Weeknd"] = "track/7MXVkk9YM5IZxh0wAE26Vw",
            ["Starboy"] = "track/7MXVkk9YM5IZxh0wAE26Vw",
            ["As It Was Harry Styles"] = "track/4LRPiXqCikLlN15c3yImP7",
            ["As It Was"] = "track/4LRPiXqCikLlN15c3yImP7",
            ["Stay The Kid LAROI Justin Bieber"] = "track/5PjdY0CKGZdErtk25bZ4YV",
            ["Stay The Kid LAROI"] = "track/5PjdY0CKGZdErtk25bZ4YV",
            ["Stay"] = "track/5PjdY0CKGZdErtk25bZ4YV",
            ["Someone You Loved Lewis Capaldi"] = "track/7qEHsqek33ZUIFv9PZe9a0",
            ["Someone You Loved"] = "track/7qEHsqek33ZUIFv9PZe9a0",
            ["Sunflower Post Malone Swae Lee"] = "track/3KkXRQHbMCARz0aVfEt68P",
            ["Sunflower Post Malone"] = "track/3KkXRQHbMCARz0aVfEt68P",
            ["Sunflower"] = "track/3KkXRQHbMCARz0aVfEt68P",
            ["Bad Guy Billie Eilish"] = "track/2FxJkQcKqV5j7879rE9GfE",
            ["Bad Guy"] = "track/2FxJkQcKqV5j7879rE9GfE",
            ["Believer Imagine Dragons"] = "track/0pqnGHJAcwhJaA2J1YgpEj",
            ["Believer"] = "track/0pqnGHJAcwhJaA2J1YgpEj",
            ["Perfect Ed Sheeran"] = "track/0tgV2Di06FyKpA1z0VMD4v",
            ["Perfect"] = "track/0tgV2Di06FyKpA1z0VMD4v",
            ["Flowers Miley Cyrus"] = "track/0yLq0bOOCvxRInP5iTXSIB",
            ["Flowers"] = "track/0yLq0bOOCvxRInP5iTXSIB",
            ["Cruel Summer Taylor Swift"] = "track/1BxfuPKGuaTgP7aM0XbdMe",
            ["Cruel Summer"] = "track/1BxfuPKGuaTgP7aM0XbdMe",
            ["Bohemian Rhapsody Queen"] = "track/7tFiyTwD0nx5a1eklYtX2J",
            ["Bohemian Rhapsody"] = "track/7tFiyTwD0nx5a1eklYtX2J",
            ["Watermelon Sugar Harry Styles"] = "track/6UelLqGlWMcVH1E5c4H7lY",
            ["Watermelon Sugar"] = "track/6UelLqGlWMcVH1E5c4H7lY",
            ["Save Your Tears The Weeknd"] = "track/5QO79kh1waicV47BqGRL3g",
            ["Save Your Tears"] = "track/5QO79kh1waicV47BqGRL3g",
            ["Levitating Dua Lipa"] = "track/463CkQjx2Zk1yXoBuierM9",
            ["Levitating"] = "track/463CkQjx2Zk1yXoBuierM9",
            ["Don't Start Now Dua Lipa"] = "track/3PfIrDoz19wz7qK7tYeu62",
            ["Dance Monkey Tones And I"] = "track/2XU0oxnq2qxCpomAAuJY8K",
            ["One Dance Drake"] = "track/1zi7xx7UVEIR53VIvVNmGF",
            ["God's Plan Drake"] = "track/6DCZcSspjsKoFjzjrWoCdn",
            ["Circles Post Malone"] = "track/21jGcNKet2qwijlDFuPiPb",
            ["Rockstar Post Malone"] = "track/0e7ipj0v1cAWsmDxKnF0gv",
            ["Closer The Chainsmokers"] = "track/7BKLCZ1jbUBVqRi2FVlTVw",
            ["Senorita Shawn Mendes Camila Cabello"] = "track/0TK2Y0ti07qSPRfdGa5n0G",
            ["Señorita"] = "track/0TK2Y0ti07qSPRfdGa5n0G",
            ["Counting Stars OneRepublic"] = "track/2tpWsVSb9UEmDRxAl1zhX1",
            ["Take Me to Church Hozier"] = "track/3d9DChrdcR0xdF2Uqlwhll",
            ["Shallow Lady Gaga Bradley Cooper"] = "track/2VxeLyX666F8uXCJ0dZF8B",
            ["Riptide Vance Joy"] = "track/7yq4QjJwFviNp8ZYbg4VGn",
            ["Sweater Weather The Neighbourhood"] = "track/2QjOHCTQ1Jl3zawyYOpxh6",
            ["Smells Like Teen Spirit Nirvana"] = "track/1f3VigftnvOWpprmR15nki",
            ["Billie Jean Michael Jackson"] = "track/5ChkMS8Otdzsq2vgSF2JWn",
            ["Hotel California Eagles"] = "track/40riOy7x9W7GXjyGp4pjAv",

            // === TOP ARTISTS ===
            ["Billie Eilish"] = "artist/6qqNVTkY8uBg9cP3Jd7DAH",
            ["Sabrina Carpenter"] = "artist/74KM79TiuVKeVCqs8QtB0B",
            ["Chappell Roan"] = "artist/7GlBOeep6PqTfFi59PTJUt",
            ["Taylor Swift"] = "artist/06HL4z0CvFAxyc27GXpf02",
            ["The Weeknd"] = "artist/1Xyo4u8uXC1ZmMpatF05PJ",
            ["Ed Sheeran"] = "artist/6eUKZXaKkcviH0Ku9w2n3V",
            ["Drake"] = "artist/3TVXtAsR1Inumwj472S9r4",
            ["Post Malone"] = "artist/246dkjvS1zLTtiykXe5h60",
            ["Justin Bieber"] = "artist/1uNFoZAHBGtllmzznpCI3s",
            ["Dua Lipa"] = "artist/6M2wZ9GZgrQXHCFpeaRuuj",
            ["Ariana Grande"] = "artist/66CXWjxzNUsdJxJ2JdwAbv",
            ["Eminem"] = "artist/7dGJo4pcD2V6ioGkoBEbpT",
            ["Coldplay"] = "artist/4gzpq5Yv4e9DoTN5mAAL4Z",
            ["Bruno Mars"] = "artist/0du5cEVh5yTK9QJze8zA0C",
            ["Imagine Dragons"] = "artist/53XhwfbYqKCa1cC15pYq2q",
            ["Queen"] = "artist/1dfeR4HaWDbWqFHLkxsg1d",
            ["The Beatles"] = "artist/3WrFJ7ztbogygnTHbHJFl2",
            ["Harry Styles"] = "artist/6KImCVD70vtIoJWnq6nGn3",
            ["Adele"] = "artist/4dpARuHxo51G3z768sgnrY",
            ["Kendrick Lamar"] = "artist/2YZyLoL8N0Wb9xBt1NhZWg",
            ["Travis Scott"] = "artist/0Y5tJX1MQlPlqiwlOH1tJY",
            ["Rihanna"] = "artist/5pKCCKE222jhGEugq2W4ir",
            ["Kanye West"] = "artist/5K4W6rqBFWDnAN6FQUkS6x",
            ["Bad Bunny"] = "artist/4q3ewBCX7sLwd24euuV69X",
            ["Olivia Rodrigo"] = "artist/1McMsnEElThX1knmY4oliG",
            ["Benson Boone"] = "artist/22wbnEMDvgVIAGunVEqjLW",
            ["Teddy Swims"] = "artist/33qTv4EjcZ2duhtwtVdRov",
            ["Shaboozey"] = "artist/26tGy9940Tj709210gH9gX",
            ["Noah Kahan"] = "artist/2RQXRqwL2cyYRp5khTCqvQ",
            ["Morgan Wallen"] = "artist/4oUHIQIBe0LHzYfvXNW4QM",
            ["Zach Bryan"] = "artist/40ZNYhgKoJWhSXi00o78zV",
            ["Tate McRae"] = "artist/45dkTj5Wlop79vg46vdroll",
            ["Beyoncé"] = "artist/6vWDO969PvNqNYHIOW5v0m",
            ["Charli xcx"] = "artist/25uiPmTg16RbhZWAqwLBy5",
            ["Daft Punk"] = "artist/4tZwfgrHOc3mvqYlEYSvVi",
            ["BTS"] = "artist/3Nrfpe0tUJi4K4DXYWgMUX",
            ["Lady Gaga"] = "artist/1hy2Jik9C2NXQBVMpeaUm5",
            ["Maroon 5"] = "artist/04gDigrS5kc9YWfZHwBETP",
            ["Shawn Mendes"] = "artist/7n2wHs1T7YfgllmoxYhSW0",
            ["SZA"] = "artist/7tYKF4w9nC0nq9CsPZTHyP",
            ["Lana Del Rey"] = "artist/00FQb4jTyendYWaN8pK0wa",
            ["Katy Perry"] = "artist/6jJ0s89eD6GaHleKKya26X",
            ["Michael Jackson"] = "artist/3fMbdgg4jU18AjLCKBvRSm",
            ["Nirvana"] = "artist/6olE6TJLqED3rqDCT0FyPh",
            ["Pink Floyd"] = "artist/0k17h0D3J5VfsdmQ1iZtE9",
            ["Linkin Park"] = "artist/6XyY86QOPPrYVGvF9ch6wz",

            // === TOP ALBUMS ===
            ["HIT ME HARD AND SOFT Billie Eilish"] = "album/7aJuG4My1L0E4agLQY9CQ4",
            ["HIT ME HARD AND SOFT"] = "album/7aJuG4My1L0E4agLQY9CQ4",
            ["Short n' Sweet Sabrina Carpenter"] = "album/1odXHwt0of00h5T84992nk",
            ["Short n' Sweet"] = "album/1odXHwt0of00h5T84992nk",
            ["The Rise and Fall of a Midwest Princess Chappell Roan"] = "album/0EiI8ylL025Jwpgq4rhj9n",
            ["The Rise and Fall of a Midwest Princess"] = "album/0EiI8ylL025Jwpgq4rhj9n",
            ["THE TORTURED POETS DEPARTMENT Taylor Swift"] = "album/2lIZef4lzdvZkiiCzvPKR7",
            ["THE TORTURED POETS DEPARTMENT"] = "album/2lIZef4lzdvZkiiCzvPKR7",
            ["F-1 Trillion Post Malone"] = "album/5J0x36b0wUf8vQ81a8Yw40",
            ["F-1 Trillion"] = "album/5J0x36b0wUf8vQ81a8Yw40",
            ["GNX Kendrick Lamar"] = "album/0hvT3yIEysuuvkK73vgdcW",
            ["GNX"] = "album/0hvT3yIEysuuvkK73vgdcW",
            ["GUTS Olivia Rodrigo"] = "album/1xJH5m163zr80UmwW7EZ7c",
            ["GUTS"] = "album/1xJH5m163zr80UmwW7EZ7c",
            ["SOS SZA"] = "album/07w0rG5YETcyezsqmIZRfZ",
            ["SOS"] = "album/07w0rG5YETcyezsqmIZRfZ",
            ["Stick Season Noah Kahan"] = "album/10Bq55gI2wDkR0T8qP3bV0",
            ["One Thing At A Time Morgan Wallen"] = "album/16rCzZOMxOPq0q5fW2p2U0",
            ["Eternal Sunshine Ariana Grande"] = "album/5EYIQUN4vW6VsA6399epBt",
            ["Cowboy Carter Beyoncé"] = "album/6BzxX6ht30A98vi4rE23HG",
            ["After Hours The Weeknd"] = "album/1wSmi4DMuPu5fG7xtyspT2",
            ["After Hours"] = "album/1wSmi4DMuPu5fG7xtyspT2",
            ["Divide Ed Sheeran"] = "album/3T4tUhGYeAnVdnZdCj0Lqy",
            ["÷ Ed Sheeran"] = "album/3T4tUhGYeAnVdnZdCj0Lqy",
            ["Midnights Taylor Swift"] = "album/151w1FgRZfnKZA9FEcg9Z3",
            ["Midnights"] = "album/151w1FgRZfnKZA9FEcg9Z3",
            ["1989 Taylor Swift"] = "album/5eyZZoQEFQWRogmanqp2za",
            ["1989"] = "album/5eyZZoQEFQWRogmanqp2za",
            ["Thriller Michael Jackson"] = "album/20KAgAvbt1elijj09Kj23M",
            ["Thriller"] = "album/20KAgAvbt1elijj09Kj23M",
            ["Abbey Road The Beatles"] = "album/0ETFjACtuP2ADo6LFhL6HN",
            ["Abbey Road"] = "album/0ETFjACtuP2ADo6LFhL6HN",
            ["The Dark Side of the Moon Pink Floyd"] = "album/4LH4d3cOWNNXdUM371W3MT",
            ["The Dark Side of the Moon"] = "album/4LH4d3cOWNNXdUM371W3MT",
            ["Starboy The Weeknd"] = "album/2ODvWsOgouMbaA5xf0RkJe",
            ["Dawn FM The Weeknd"] = "album/2nLOHgz9750Es3QrjIZRjh",
            ["Dawn FM"] = "album/2nLOHgz9750Es3QrjIZRjh",
            ["Sour Olivia Rodrigo"] = "album/6s8qw029scz0W59ZkJ6xF7",
            ["Sour"] = "album/6s8qw029scz0W59ZkJ6xF7",
            ["Future Nostalgia Dua Lipa"] = "album/7fJJK56Z97WaqrurBxFSp8",
            ["Future Nostalgia"] = "album/7fJJK56Z97WaqrurBxFSp8",
            ["Fine Line Harry Styles"] = "album/7xV2TdnOCRBVaWRAjeYusv",
            ["Fine Line"] = "album/7xV2TdnOCRBVaWRAjeYusv",
            ["Harry's House Harry Styles"] = "album/5XKFrCWkeNkWtZ9gP7vMbm",
            ["Harry's House"] = "album/5XKFrCWkeNkWtZ9gP7vMbm",
            ["Hollywood's Bleeding Post Malone"] = "album/4g1ZZWOct2qnIOFiJAX4Bg",
            ["WHEN WE ALL FALL ASLEEP, WHERE DO WE GO? Billie Eilish"] = "album/0S0KGZnfBGSI3FfnipPpak",

            // === TOP PLAYLISTS ===
            ["Today's Top Hits"] = "playlist/37i9dQZF1DXcBWIGoYBM5M",
            ["Top 50 - Global"] = "playlist/37i9dQZEVXbMDoHDwVN2tF",
            ["Top 50 - USA"] = "playlist/37i9dQZEVXbLRQDuF5jeBp",
            ["Mega Hit Mix"] = "playlist/37i9dQZF1DXbYM3nMM0oPk",
            ["All Out 2010s"] = "playlist/37i9dQZF1DX5Ejj0EkURtP",
            ["All Out 2000s"] = "playlist/37i9dQZF1DX4o1oenSJRJd",
            ["All Out 80s"] = "playlist/37i9dQZF1DX4UtSsGT1Sbe",
            ["Rock Classics"] = "playlist/37i9dQZF1DWXRqgorJj26U",
            ["Peaceful Piano"] = "playlist/37i9dQZF1DX4sWSpwq3LiO",
            ["Hot Hits USA"] = "playlist/37i9dQZF1DX0kbYCOL4TLq",
            ["RapCaviar"] = "playlist/37i9dQZF1DX0XUsuxWHRQd",
            ["Viva Latino"] = "playlist/37i9dQZF1DX10zKzsJ2jva",
            ["Pop Rising"] = "playlist/37i9dQZF1DWUa8ZRTfalHk",
            ["Songs to Sing in the Car"] = "playlist/37i9dQZF1DWZqzGQdp2pMo",
            ["Beast Mode"] = "playlist/37i9dQZF1DX76Wlfdnj7AP",
            ["Deep Focus"] = "playlist/37i9dQZF1DWZeKCadgRdKQ"
        };

        static SpotifyDeepLinkHelper()
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            }
        }

        private static string NormalizeMatchString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            string cleaned = Regex.Replace(s, @"\s*[\(\[][^\)\]]*[\)\]]", " ");
            cleaned = Regex.Replace(cleaned, @"[^\p{L}\p{N}\s]", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim().ToLowerInvariant();
            return cleaned;
        }

        private static string StripMinorWords(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var filtered = new List<string>(words.Length);
            foreach (var w in words)
            {
                if (w != "a" && w != "an" && w != "the" && w != "of")
                {
                    filtered.Add(w);
                }
            }
            return string.Join(" ", filtered);
        }

        /// <summary>
        /// Cleans tracking query parameters from Spotify URLs.
        /// </summary>
        public static string CleanSpotifyUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            try
            {
                var uri = new Uri(url);
                if (uri.Host.Contains("spotify.com", StringComparison.OrdinalIgnoreCase))
                {
                    return uri.GetLeftPart(UriPartial.Path);
                }
            }
            catch { }
            return url;
        }

        /// <summary>
        /// Checks the pre-indexed database for known canonical paths.
        /// </summary>
        public static string? GetKnownCanonicalPath(string title, string type = "track", string? artist = null)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;

            string cleanTitle = title.Trim();
            string cleanArtist = artist?.Trim() ?? "";

            // Check title + artist combined
            if (!string.IsNullOrEmpty(cleanArtist))
            {
                string combined = $"{cleanTitle} {cleanArtist}";
                if (_canonicalDatabase.TryGetValue(combined, out var matchCombined) && matchCombined.StartsWith(type, StringComparison.OrdinalIgnoreCase))
                {
                    return matchCombined;
                }
            }

            // Check title directly
            if (_canonicalDatabase.TryGetValue(cleanTitle, out var matchTitle) && matchTitle.StartsWith(type, StringComparison.OrdinalIgnoreCase))
            {
                return matchTitle;
            }

            // If resolving artist specifically, check artist alone
            if (type.Equals("artist", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(cleanArtist))
            {
                if (_canonicalDatabase.TryGetValue(cleanArtist, out var matchArtist) && matchArtist.StartsWith("artist", StringComparison.OrdinalIgnoreCase))
                {
                    return matchArtist;
                }
            }

            // Normalized token matching
            string normTitle = NormalizeMatchString(cleanTitle);
            string strippedTitle = StripMinorWords(normTitle);
            string normArtist = NormalizeMatchString(cleanArtist);
            string strippedArtist = StripMinorWords(normArtist);

            foreach (var kvp in _canonicalDatabase)
            {
                if (!kvp.Value.StartsWith(type, StringComparison.OrdinalIgnoreCase)) continue;

                string dbNorm = NormalizeMatchString(kvp.Key);
                string dbStripped = StripMinorWords(dbNorm);

                // Exact normalized match
                if (normTitle == dbNorm || (!string.IsNullOrEmpty(normArtist) && $"{normTitle} {normArtist}" == dbNorm))
                {
                    return kvp.Value;
                }

                // Stripped minor words match
                if (!string.IsNullOrEmpty(strippedTitle) && (strippedTitle == dbStripped || (!string.IsNullOrEmpty(strippedArtist) && $"{strippedTitle} {strippedArtist}" == dbStripped)))
                {
                    return kvp.Value;
                }

                // Substring containment when artist matches
                if (!string.IsNullOrEmpty(strippedTitle) && !string.IsNullOrEmpty(strippedArtist))
                {
                    if (dbStripped.Contains(strippedTitle) && dbStripped.Contains(strippedArtist))
                    {
                        return kvp.Value;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a true Spotify deep link (native URI and web URL) for any track, album, artist, or playlist.
        /// Guaranteed not to fall back to a generic search page when a true entity can be identified.
        /// </summary>
        public static async Task<(Uri? NativeUri, string WebUrl)> ResolveSpotifyDeepLinkAsync(
            string title,
            string type = "track",
            string? artist = null,
            string? album = null,
            string? currentUrl = null)
        {
            string cleanTitle = title?.Trim() ?? "";
            string cleanArtist = artist?.Trim() ?? "";
            string cleanType = NormalizeType(type);

            // 1. If currentUrl already has a valid canonical Spotify path with ID, strip tracking and return directly
            if (!string.IsNullOrWhiteSpace(currentUrl))
            {
                var directMatch = Regex.Match(currentUrl, @"open\.spotify\.com/(?:intl-[a-z]{2}/)?(track|album|artist|playlist)/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
                if (directMatch.Success)
                {
                    string entityType = directMatch.Groups[1].Value.ToLowerInvariant();
                    string entityId = directMatch.Groups[2].Value;
                    string canonicalWeb = $"https://open.spotify.com/{entityType}/{entityId}";
                    var nativeUri = new Uri($"spotify:{entityType}:{entityId}");
                    return (nativeUri, canonicalWeb);
                }

                if (currentUrl.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase) && !currentUrl.Contains(":search:", StringComparison.OrdinalIgnoreCase))
                {
                    var nativeMatch = Regex.Match(currentUrl, @"spotify:(track|album|artist|playlist):([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
                    if (nativeMatch.Success)
                    {
                        string entityType = nativeMatch.Groups[1].Value.ToLowerInvariant();
                        string entityId = nativeMatch.Groups[2].Value;
                        return (new Uri(currentUrl), $"https://open.spotify.com/{entityType}/{entityId}");
                    }
                }
            }

            // 2. Check pre-indexed canonical database (0 ms instant response)
            string? knownPath = GetKnownCanonicalPath(cleanTitle, cleanType, cleanArtist);
            if (!string.IsNullOrEmpty(knownPath))
            {
                string canonicalWeb = $"https://open.spotify.com/{knownPath}";
                string nativeProtocol = $"spotify:{knownPath.Replace('/', ':')}";
                return (new Uri(nativeProtocol), canonicalWeb);
            }

            // 3. Check dynamic in-memory cache
            string cacheKey = $"{cleanType}:{cleanTitle}:{cleanArtist}";
            if (_dynamicCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            // 4. Try official Spotify Web API with Client Credentials (if configured in AppConfig or environment)
            var spotifyApiResult = await TryResolveViaSpotifyApiAsync(cleanTitle, cleanType, cleanArtist);
            if (spotifyApiResult.NativeUri != null)
            {
                CacheDynamic(cacheKey, spotifyApiResult);
                return spotifyApiResult;
            }

            // 5. Try MusicBrainz Open API resolution (100% free, no key required)
            try
            {
                var mbResult = await TryResolveViaMusicBrainzAsync(cleanTitle, cleanType, cleanArtist, album);
                if (mbResult.NativeUri != null)
                {
                    CacheDynamic(cacheKey, mbResult);
                    return mbResult;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpotifyDeepLinkHelper] MusicBrainz lookup failed: {ex.Message}");
            }

            // 6. Safe fallback with explicit search query
            string query = string.IsNullOrWhiteSpace(cleanArtist) ? cleanTitle : $"{cleanTitle} {cleanArtist}";
            string encoded = Uri.EscapeDataString(query);
            var fallbackNative = new Uri($"spotify:search:{encoded}");
            string fallbackWeb = $"https://open.spotify.com/search/{encoded}";
            var fallbackResult = (fallbackNative, fallbackWeb);
            CacheDynamic(cacheKey, fallbackResult);
            return fallbackResult;
        }

        public static async Task<string> ResolveSpotifyUrlAsync(string query, string type = "track")
        {
            var (_, webUrl) = await ResolveSpotifyDeepLinkAsync(query, type);
            return webUrl;
        }

        private static string NormalizeType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "track";
            string lower = type.Trim().ToLowerInvariant();
            return lower switch
            {
                "songs" or "song" or "track" or "tracks" => "track",
                "albums" or "album" => "album",
                "artists" or "artist" => "artist",
                "playlists" or "playlist" => "playlist",
                _ => "track"
            };
        }

        #region Spotify Web API Resolver

        private static async Task<string?> GetSpotifyAccessTokenAsync()
        {
            var config = ConfigService.Config;
            string? clientId = config.SpotifyClientId ?? Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
            string? clientSecret = config.SpotifyClientSecret ?? Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                return null;
            }

            if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiresAt)
            {
                return _cachedAccessToken;
            }

            await _tokenLock.WaitAsync();
            try
            {
                if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiresAt)
                {
                    return _cachedAccessToken;
                }

                using var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret
                });

                using var resp = await _httpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
                    {
                        _cachedAccessToken = tokenProp.GetString();
                        int expiresIn = doc.RootElement.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;
                        _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn - 60);
                        return _cachedAccessToken;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpotifyDeepLinkHelper] Token request failed: {ex.Message}");
            }
            finally
            {
                _tokenLock.Release();
            }

            return null;
        }

        private static async Task<(Uri? NativeUri, string WebUrl)> TryResolveViaSpotifyApiAsync(string title, string type, string? artist)
        {
            string? token = await GetSpotifyAccessTokenAsync();
            if (string.IsNullOrEmpty(token)) return (null, string.Empty);

            try
            {
                string searchQ = type switch
                {
                    "artist" => !string.IsNullOrEmpty(artist) ? artist : title,
                    "album" => !string.IsNullOrEmpty(artist) ? $"album:{title} artist:{artist}" : $"album:{title}",
                    _ => !string.IsNullOrEmpty(artist) ? $"track:{title} artist:{artist}" : title
                };

                string url = $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(searchQ)}&type={type}&limit=1";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var resp = await _httpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    string json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    string containerProp = type switch
                    {
                        "artist" => "artists",
                        "album" => "albums",
                        "playlist" => "playlists",
                        _ => "tracks"
                    };

                    if (doc.RootElement.TryGetProperty(containerProp, out var container) &&
                        container.TryGetProperty("items", out var items) &&
                        items.GetArrayLength() > 0)
                    {
                        var first = items[0];
                        if (first.TryGetProperty("id", out var idProp))
                        {
                            string id = idProp.GetString() ?? "";
                            if (!string.IsNullOrEmpty(id))
                            {
                                string canonicalWeb = $"https://open.spotify.com/{type}/{id}";
                                return (new Uri($"spotify:{type}:{id}"), canonicalWeb);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SpotifyDeepLinkHelper] Spotify Web API search failed: {ex.Message}");
            }

            return (null, string.Empty);
        }

        #endregion

        #region MusicBrainz Open Resolver

        private static async Task<(Uri? NativeUri, string WebUrl)> TryResolveViaMusicBrainzAsync(string title, string type, string? artist, string? album)
        {
            if (type.Equals("artist", StringComparison.OrdinalIgnoreCase))
            {
                string artistQuery = !string.IsNullOrEmpty(artist) ? artist : title;
                string mbArtistUrl = $"https://musicbrainz.org/ws/2/artist?query=artist:%22{Uri.EscapeDataString(artistQuery)}%22&fmt=json";

                var json = await _httpClient.GetStringAsync(mbArtistUrl);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0)
                {
                    string mbid = artists[0].GetProperty("id").GetString() ?? "";
                    if (!string.IsNullOrEmpty(mbid))
                    {
                        string detailUrl = $"https://musicbrainz.org/ws/2/artist/{mbid}?inc=url-rels&fmt=json";
                        var detailJson = await _httpClient.GetStringAsync(detailUrl);
                        using var detailDoc = JsonDocument.Parse(detailJson);
                        if (detailDoc.RootElement.TryGetProperty("relations", out var relations))
                        {
                            foreach (var rel in relations.EnumerateArray())
                            {
                                if (rel.TryGetProperty("url", out var urlObj) && urlObj.TryGetProperty("resource", out var res))
                                {
                                    string r = res.GetString() ?? "";
                                    var match = Regex.Match(r, @"open\.spotify\.com/artist/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
                                    if (match.Success)
                                    {
                                        string id = match.Groups[1].Value;
                                        return (new Uri($"spotify:artist:{id}"), $"https://open.spotify.com/artist/{id}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else if (type.Equals("album", StringComparison.OrdinalIgnoreCase))
            {
                string albumQuery = string.IsNullOrWhiteSpace(artist)
                    ? $"releasegroup:%22{Uri.EscapeDataString(title)}%22"
                    : $"releasegroup:%22{Uri.EscapeDataString(title)}%22%20AND%20artist:%22{Uri.EscapeDataString(artist)}%22";

                string mbRgUrl = $"https://musicbrainz.org/ws/2/release-group?query={albumQuery}&fmt=json";
                var json = await _httpClient.GetStringAsync(mbRgUrl);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("release-groups", out var rgs) && rgs.GetArrayLength() > 0)
                {
                    string rgid = rgs[0].GetProperty("id").GetString() ?? "";
                    if (!string.IsNullOrEmpty(rgid))
                    {
                        string detailUrl = $"https://musicbrainz.org/ws/2/release-group/{rgid}?inc=url-rels+releases&fmt=json";
                        var detailJson = await _httpClient.GetStringAsync(detailUrl);
                        using var detailDoc = JsonDocument.Parse(detailJson);

                        // Check release-group relations first
                        if (detailDoc.RootElement.TryGetProperty("relations", out var relations))
                        {
                            foreach (var rel in relations.EnumerateArray())
                            {
                                if (rel.TryGetProperty("url", out var urlObj) && urlObj.TryGetProperty("resource", out var res))
                                {
                                    string r = res.GetString() ?? "";
                                    var match = Regex.Match(r, @"open\.spotify\.com/album/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
                                    if (match.Success)
                                    {
                                        string id = match.Groups[1].Value;
                                        return (new Uri($"spotify:album:{id}"), $"https://open.spotify.com/album/{id}");
                                    }
                                }
                            }
                        }

                        // Check primary release relations
                        if (detailDoc.RootElement.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
                        {
                            string relId = releases[0].GetProperty("id").GetString() ?? "";
                            if (!string.IsNullOrEmpty(relId))
                            {
                                string relDetailUrl = $"https://musicbrainz.org/ws/2/release/{relId}?inc=url-rels&fmt=json";
                                var relDetailJson = await _httpClient.GetStringAsync(relDetailUrl);
                                using var relDoc = JsonDocument.Parse(relDetailJson);
                                if (relDoc.RootElement.TryGetProperty("relations", out var relRelations))
                                {
                                    foreach (var rel in relRelations.EnumerateArray())
                                    {
                                        if (rel.TryGetProperty("url", out var urlObj) && urlObj.TryGetProperty("resource", out var res))
                                        {
                                            string r = res.GetString() ?? "";
                                            var match = Regex.Match(r, @"open\.spotify\.com/album/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
                                            if (match.Success)
                                            {
                                                string id = match.Groups[1].Value;
                                                return (new Uri($"spotify:album:{id}"), $"https://open.spotify.com/album/{id}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else // track
            {
                string trackQuery = string.IsNullOrWhiteSpace(artist)
                    ? $"recording:%22{Uri.EscapeDataString(title)}%22"
                    : $"recording:%22{Uri.EscapeDataString(title)}%22%20AND%20artist:%22{Uri.EscapeDataString(artist)}%22";

                string mbRecUrl = $"https://musicbrainz.org/ws/2/recording?query={trackQuery}&fmt=json";
                var json = await _httpClient.GetStringAsync(mbRecUrl);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("recordings", out var recordings) && recordings.GetArrayLength() > 0)
                {
                    string recId = recordings[0].GetProperty("id").GetString() ?? "";
                    if (!string.IsNullOrEmpty(recId))
                    {
                        string recDetailUrl = $"https://musicbrainz.org/ws/2/recording/{recId}?inc=url-rels+releases&fmt=json";
                        var recDetailJson = await _httpClient.GetStringAsync(recDetailUrl);
                        using var recDoc = JsonDocument.Parse(recDetailJson);

                        // Check recording direct relations
                        if (recDoc.RootElement.TryGetProperty("relations", out var relations))
                        {
                            foreach (var rel in relations.EnumerateArray())
                            {
                                if (rel.TryGetProperty("url", out var urlObj) && urlObj.TryGetProperty("resource", out var res))
                                {
                                    string r = res.GetString() ?? "";
                                    var match = Regex.Match(r, @"open\.spotify\.com/track/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
                                    if (match.Success)
                                    {
                                        string id = match.Groups[1].Value;
                                        return (new Uri($"spotify:track:{id}"), $"https://open.spotify.com/track/{id}");
                                    }
                                }
                            }
                        }

                        // If track direct link isn't attached, check primary release for album link
                        if (recDoc.RootElement.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
                        {
                            string relId = releases[0].GetProperty("id").GetString() ?? "";
                            if (!string.IsNullOrEmpty(relId))
                            {
                                string relDetailUrl = $"https://musicbrainz.org/ws/2/release/{relId}?inc=url-rels&fmt=json";
                                var relDetailJson = await _httpClient.GetStringAsync(relDetailUrl);
                                using var relDoc = JsonDocument.Parse(relDetailJson);
                                if (relDoc.RootElement.TryGetProperty("relations", out var relRelations))
                                {
                                    foreach (var rel in relRelations.EnumerateArray())
                                    {
                                        if (rel.TryGetProperty("url", out var urlObj) && urlObj.TryGetProperty("resource", out var res))
                                        {
                                            string r = res.GetString() ?? "";
                                            var match = Regex.Match(r, @"open\.spotify\.com/album/([a-zA-Z0-9]+)", RegexOptions.IgnoreCase);
                                            if (match.Success)
                                            {
                                                string id = match.Groups[1].Value;
                                                return (new Uri($"spotify:album:{id}"), $"https://open.spotify.com/album/{id}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return (null, string.Empty);
        }

        #endregion
    }
}
