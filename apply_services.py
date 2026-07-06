import re

def fix_music_service():
    filepath = 'Services/Streaming/MusicStreamingService.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    if 'public struct OdesliProvider' not in content:
        content = content.replace('    public class MusicStreamingService', '''    public struct OdesliProvider
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    public class MusicStreamingService''')

    if 'GetStreamingLinksAsync' not in content:
        method = '''
        public async Task<List<OdesliProvider>> GetStreamingLinksAsync(string trackViewUrl)
        {
            var providers = new List<OdesliProvider>();
            try
            {
                var encodedUrl = Uri.EscapeDataString(trackViewUrl);
                var odesliUrl = $"https://api.song.link/v1-alpha.1/links?url={encodedUrl}";
                var response = await _httpClient.GetStringAsync(odesliUrl);

                using var document = System.Text.Json.JsonDocument.Parse(response);
                if (document.RootElement.TryGetProperty("linksByPlatform", out var linksByPlatform))
                {
                    string[] platforms = { "spotify", "youtube", "appleMusic", "soundcloud", "youtubeMusic", "amazonMusic", "tidal" };
                    foreach (var platform in platforms)
                    {
                        if (linksByPlatform.TryGetProperty(platform, out var platformElement) && 
                            platformElement.TryGetProperty("url", out var urlElement))
                        {
                            providers.Add(new OdesliProvider 
                            { 
                                Name = platform, 
                                Url = urlElement.GetString() 
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Odesli Links Error: {ex.Message}");
            }
            return providers;
        }'''
        
        # Insert before the last two closing braces
        idx = content.rfind('    }\n}')
        if idx != -1:
            content = content[:idx] + method + '\n' + content[idx:]
        
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)

def fix_tmdb_service():
    filepath = 'Services/Streaming/TmdbService.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    orig_movies = '''        public async Task<List<TmdbMedia>> GetPopularMoviesAsync(int page = 1)
        {
            int startPage = (page - 1) * 3 + 1;
            var tasks = new[]
            {
                FetchMediaListAsync($"{BaseUrl}/movie/popular?api_key={ApiKey}&page={startPage}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/movie/popular?api_key={ApiKey}&page={startPage + 1}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/movie/popular?api_key={ApiKey}&page={startPage + 2}&region={Region}")
            };
            var results = await Task.WhenAll(tasks);
            var list = new List<TmdbMedia>();
            foreach (var r in results) list.AddRange(r);
            return list;
        }'''
    
    new_movies = '''        public async Task<List<TmdbMedia>> GetPopularMoviesAsync(int page = 1)
        {
            int startPage = (page - 1) * 3 + 1;
            var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var tasks = new[]
            {
                FetchMediaListAsync($"{BaseUrl}/discover/movie?api_key={ApiKey}&sort_by=popularity.desc&vote_count.gte=100&primary_release_date.lte={dateStr}&page={startPage}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/discover/movie?api_key={ApiKey}&sort_by=popularity.desc&vote_count.gte=100&primary_release_date.lte={dateStr}&page={startPage + 1}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/discover/movie?api_key={ApiKey}&sort_by=popularity.desc&vote_count.gte=100&primary_release_date.lte={dateStr}&page={startPage + 2}&region={Region}")
            };
            var results = await Task.WhenAll(tasks);
            var list = new List<TmdbMedia>();
            var seenIds = new HashSet<int>();
            foreach (var r in results)
            {
                if (r == null) continue;
                foreach (var item in r)
                {
                    if (seenIds.Add(item.Id))
                    {
                        list.Add(item);
                    }
                }
            }
            return list;
        }'''
    
    orig_tv = '''        public async Task<List<TmdbMedia>> GetPopularTvShowsAsync(int page = 1)
        {
            int startPage = (page - 1) * 3 + 1;
            var tasks = new[]
            {
                FetchMediaListAsync($"{BaseUrl}/tv/popular?api_key={ApiKey}&page={startPage}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/tv/popular?api_key={ApiKey}&page={startPage + 1}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/tv/popular?api_key={ApiKey}&page={startPage + 2}&region={Region}")
            };
            var results = await Task.WhenAll(tasks);
            var list = new List<TmdbMedia>();
            foreach (var r in results) list.AddRange(r);
            return list;
        }'''
    
    new_tv = '''        public async Task<List<TmdbMedia>> GetPopularTvShowsAsync(int page = 1)
        {
            int startPage = (page - 1) * 3 + 1;
            var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var tasks = new[]
            {
                FetchMediaListAsync($"{BaseUrl}/discover/tv?api_key={ApiKey}&sort_by=popularity.desc&vote_count.gte=100&first_air_date.lte={dateStr}&page={startPage}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/discover/tv?api_key={ApiKey}&sort_by=popularity.desc&vote_count.gte=100&first_air_date.lte={dateStr}&page={startPage + 1}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/discover/tv?api_key={ApiKey}&sort_by=popularity.desc&vote_count.gte=100&first_air_date.lte={dateStr}&page={startPage + 2}&region={Region}")
            };
            var results = await Task.WhenAll(tasks);
            var list = new List<TmdbMedia>();
            var seenIds = new HashSet<int>();
            foreach (var r in results)
            {
                if (r == null) continue;
                foreach (var item in r)
                {
                    if (seenIds.Add(item.Id))
                    {
                        list.Add(item);
                    }
                }
            }
            return list;
        }'''
    
    content = content.replace(orig_movies, new_movies).replace(orig_tv, new_tv)
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_music_service()
fix_tmdb_service()
print("Services updated")
