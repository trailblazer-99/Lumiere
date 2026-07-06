import re

def fix_movies_tv(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_method = '''        private Uri GetProviderSearchUri(string providerName, string title)
        {
            var encodedTitle = Uri.EscapeDataString(title);
            if (providerName != null)
            {
                return new Uri($"https://duckduckgo.com/?q=!ducky+Watch+{encodedTitle}+on+{Uri.EscapeDataString(providerName)}");
            }
            return null;
        }'''
        
    new_method = '''        private Uri GetProviderSearchUri(string providerName, string title)
        {
            if (string.IsNullOrEmpty(providerName) || string.IsNullOrEmpty(title)) return null;
            var q = Uri.EscapeDataString(title);
            
            return providerName.ToLower() switch
            {
                "netflix" => new Uri($"https://www.netflix.com/search?q={q}"),
                "amazon prime video" => new Uri($"https://www.amazon.com/s?k={q}&i=instant-video"),
                "disney plus" or "disney+" => new Uri($"https://www.disneyplus.com/search?q={q}"),
                "hulu" => new Uri($"https://www.hulu.com/search?q={q}"),
                "apple tv plus" or "apple tv" or "apple tv+" => new Uri($"https://tv.apple.com/search?term={q}"),
                "hbo max" or "max" => new Uri($"https://play.max.com/search?q={q}"),
                "peacock" or "peacock premium" => new Uri($"https://www.peacocktv.com/watch/search?q={q}"),
                "paramount plus" or "paramount+" => new Uri($"https://www.paramountplus.com/search/?q={q}"),
                "crunchyroll" => new Uri($"https://www.crunchyroll.com/search?q={q}"),
                "youtube" => new Uri($"https://www.youtube.com/results?search_query={q}"),
                _ => new Uri($"https://www.themoviedb.org/search?query={q}")
            };
        }'''

    content = content.replace(old_method, new_method)
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_movies_tv('Pages/StreamingMoviesPage.xaml.cs')
fix_movies_tv('Pages/StreamingTvShowsPage.xaml.cs')

def fix_music(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Replace Wikimedia Commons Icons
    content = content.replace('https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/apple-music.png', 'https://upload.wikimedia.org/wikipedia/commons/thumb/5/5f/Apple_Music_icon.svg/200px-Apple_Music_icon.svg.png')
    content = content.replace('https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/youtube-music.png', 'https://upload.wikimedia.org/wikipedia/commons/thumb/6/6b/YouTube_Music_logo.svg/200px-YouTube_Music_logo.svg.png')
    content = content.replace('https://cdn.jsdelivr.net/gh/homarr-labs/dashboard-icons/png/amazon-music.png', 'https://upload.wikimedia.org/wikipedia/commons/thumb/8/86/Amazon_Music_logo.svg/200px-Amazon_Music_logo.svg.png')
    
    # Replace DuckDuckGo search links with native/web search endpoints
    # Wait, in StreamingMusicPage.xaml.cs, the search URL was generated per-provider.
    # Let's find the btn.Click handler.
    # Actually, let's just do a string replacement for the searchUri logic.
    old_logic = '''var searchUri = new Uri($"https://duckduckgo.com/?q=!ducky+Listen+to+{Uri.EscapeDataString(titleToPass)}+on+{Uri.EscapeDataString(provider.ProviderName)}");'''
    new_logic = '''var q = Uri.EscapeDataString(titleToPass);
                        Uri searchUri = null;
                        switch (provider.ProviderName.ToLower())
                        {
                            case "spotify": searchUri = new Uri($"spotify:search:{q}"); break;
                            case "apple music": searchUri = new Uri($"https://music.apple.com/search?term={q}"); break;
                            case "youtube music": searchUri = new Uri($"https://music.youtube.com/search?q={q}"); break;
                            case "amazon music": searchUri = new Uri($"https://music.amazon.com/search/{q}"); break;
                            case "soundcloud": searchUri = new Uri($"https://soundcloud.com/search/sounds?q={q}"); break;
                            default: searchUri = new Uri($"https://duckduckgo.com/?q=!ducky+{q}"); break;
                        }'''
    content = content.replace(old_logic, new_logic)
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_music('Pages/StreamingMusicPage.xaml.cs')
fix_music('Pages/NowPlayingPage.xaml.cs')
print("Done")
