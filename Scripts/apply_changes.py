import re

# 1. TmdbService.cs
with open('Services/Streaming/TmdbService.cs', 'r', encoding='utf-8') as f:
    tmdb_content = f.read()

tmdb_content = tmdb_content.replace(
'''        public async Task<List<TmdbMedia>> GetPopularMoviesAsync(int page = 1)
        {
            var url = $"{BaseUrl}/movie/popular?api_key={ApiKey}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> GetPopularTvShowsAsync(int page = 1)
        {
            var url = $"{BaseUrl}/tv/popular?api_key={ApiKey}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> DiscoverMoviesAsync(int genreId, string sortBy = "popularity.desc", int page = 1)
        {
            var url = $"{BaseUrl}/discover/movie?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }
        
        public async Task<List<TmdbMedia>> DiscoverTvShowsAsync(int genreId, string sortBy = "popularity.desc", int page = 1)
        {
            var url = $"{BaseUrl}/discover/tv?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }''',
'''        public async Task<List<TmdbMedia>> GetPopularMoviesAsync(int page = 1)
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
        }

        public async Task<List<TmdbMedia>> GetPopularTvShowsAsync(int page = 1)
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
        }

        public async Task<List<TmdbMedia>> DiscoverMoviesAsync(int genreId, string sortBy = "popularity.desc", int page = 1)
        {
            int startPage = (page - 1) * 3 + 1;
            var tasks = new[]
            {
                FetchMediaListAsync($"{BaseUrl}/discover/movie?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={startPage}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/discover/movie?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={startPage + 1}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/discover/movie?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={startPage + 2}&region={Region}")
            };
            var results = await Task.WhenAll(tasks);
            var list = new List<TmdbMedia>();
            foreach (var r in results) list.AddRange(r);
            return list;
        }
        
        public async Task<List<TmdbMedia>> DiscoverTvShowsAsync(int genreId, string sortBy = "popularity.desc", int page = 1)
        {
            int startPage = (page - 1) * 3 + 1;
            var tasks = new[]
            {
                FetchMediaListAsync($"{BaseUrl}/discover/tv?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={startPage}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/discover/tv?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={startPage + 1}&region={Region}"),
                FetchMediaListAsync($"{BaseUrl}/discover/tv?api_key={ApiKey}&with_genres={genreId}&sort_by={sortBy}&page={startPage + 2}&region={Region}")
            };
            var results = await Task.WhenAll(tasks);
            var list = new List<TmdbMedia>();
            foreach (var r in results) list.AddRange(r);
            return list;
        }

        public async Task<List<TmdbMedia>> SearchMoviesAsync(string query, int page = 1)
        {
            var url = $"{BaseUrl}/search/movie?api_key={ApiKey}&query={Uri.EscapeDataString(query)}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }

        public async Task<List<TmdbMedia>> SearchTvShowsAsync(string query, int page = 1)
        {
            var url = $"{BaseUrl}/search/tv?api_key={ApiKey}&query={Uri.EscapeDataString(query)}&page={page}&region={Region}";
            return await FetchMediaListAsync(url);
        }'''
)
with open('Services/Streaming/TmdbService.cs', 'w', encoding='utf-8') as f:
    f.write(tmdb_content)

print("TmdbService patched.")
