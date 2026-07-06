import re

def apply_tv_deep():
    filepath = 'Pages/StreamingTvShowsPage.xaml.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    orig_providers = '''        private Uri GetProviderSearchUri(string providerName, string title)
        {
            var encodedTitle = Uri.EscapeDataString(title);
            if (providerName.Contains("Netflix", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.netflix.com/search?q={encodedTitle}");
            if (providerName.Contains("Amazon", StringComparison.OrdinalIgnoreCase) || providerName.Contains("Prime", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.amazon.com/s?k={encodedTitle}&i=instant-video");
            if (providerName.Contains("Disney", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.disneyplus.com/search?q={encodedTitle}");
            if (providerName.Contains("Hulu", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.hulu.com/search?q={encodedTitle}");
            if (providerName.Contains("Apple", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://tv.apple.com/search?q={encodedTitle}");
            if (providerName.Contains("Max", StringComparison.OrdinalIgnoreCase) || providerName.Contains("HBO", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://play.max.com/search?q={encodedTitle}");
            if (providerName.Contains("Peacock", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.peacocktv.com/search?q={encodedTitle}");
            if (providerName.Contains("Paramount", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.paramountplus.com/search?q={encodedTitle}");
            if (providerName.Contains("JioCinema", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.jiocinema.com/search/{encodedTitle}");
            if (providerName.Contains("Hotstar", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.hotstar.com/in/explore?searchQuery={encodedTitle}");
            if (providerName.Contains("Zee5", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.zee5.com/search?q={encodedTitle}");
            if (providerName.Contains("SonyLIV", StringComparison.OrdinalIgnoreCase) || providerName.Contains("Sony LIV", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.sonyliv.com/search?q={encodedTitle}");
            if (providerName.Contains("Crunchyroll", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.crunchyroll.com/search?q={encodedTitle}");
            return null;
        }'''
    
    new_providers = '''        private Uri GetProviderSearchUri(string providerName, string title)
        {
            var encodedTitle = Uri.EscapeDataString(title);
            if (providerName != null)
            {
                return new Uri($"https://www.google.com/search?btnI=1&q=Watch+{encodedTitle}+on+{Uri.EscapeDataString(providerName)}");
            }
            return null;
        }'''
    
    content = content.replace(orig_providers, new_providers)
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

def apply_movies_deep():
    filepath = 'Pages/StreamingMoviesPage.xaml.cs'
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    orig_providers = '''        private Uri GetProviderSearchUri(string providerName, string title)
        {
            var encodedTitle = Uri.EscapeDataString(title);
            if (providerName.Contains("Netflix", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.netflix.com/search?q={encodedTitle}");
            if (providerName.Contains("Amazon", StringComparison.OrdinalIgnoreCase) || providerName.Contains("Prime", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.amazon.com/s?k={encodedTitle}&i=instant-video");
            if (providerName.Contains("Disney", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.disneyplus.com/search?q={encodedTitle}");
            if (providerName.Contains("Hulu", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.hulu.com/search?q={encodedTitle}");
            if (providerName.Contains("Apple", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://tv.apple.com/search?q={encodedTitle}");
            if (providerName.Contains("Max", StringComparison.OrdinalIgnoreCase) || providerName.Contains("HBO", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://play.max.com/search?q={encodedTitle}");
            if (providerName.Contains("Peacock", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.peacocktv.com/search?q={encodedTitle}");
            if (providerName.Contains("Paramount", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.paramountplus.com/search?q={encodedTitle}");
            if (providerName.Contains("JioCinema", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.jiocinema.com/search/{encodedTitle}");
            if (providerName.Contains("Hotstar", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.hotstar.com/in/explore?searchQuery={encodedTitle}");
            if (providerName.Contains("Zee5", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.zee5.com/search?q={encodedTitle}");
            if (providerName.Contains("SonyLIV", StringComparison.OrdinalIgnoreCase) || providerName.Contains("Sony LIV", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.sonyliv.com/search?q={encodedTitle}");
            if (providerName.Contains("Crunchyroll", StringComparison.OrdinalIgnoreCase)) return new Uri($"https://www.crunchyroll.com/search?q={encodedTitle}");
            return null;
        }'''
    
    new_providers = '''        private Uri GetProviderSearchUri(string providerName, string title)
        {
            var encodedTitle = Uri.EscapeDataString(title);
            if (providerName != null)
            {
                return new Uri($"https://www.google.com/search?btnI=1&q=Watch+{encodedTitle}+on+{Uri.EscapeDataString(providerName)}");
            }
            return null;
        }'''
    
    content = content.replace(orig_providers, new_providers)
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

apply_tv_deep()
apply_movies_deep()
print("Deep links updated")
