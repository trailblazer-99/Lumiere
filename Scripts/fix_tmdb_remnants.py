import re

def fix_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Replace the lambda-style switch with a block switch
    # Actually, the switch is in an expression-bodied return:
    # return providerName.ToLower() switch { ... _ => new Uri(...) };
    # Let's just replace the default case to use an inline condition if possible,
    # or rewrite the method.
    
    old_method = '''        private Uri GetProviderSearchUri(string providerName, string title)
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
        
    new_method = '''        private Uri GetProviderSearchUri(string providerName, string title)
        {
            if (string.IsNullOrEmpty(providerName) || string.IsNullOrEmpty(title)) return null;
            var q = Uri.EscapeDataString(title);
            
            switch (providerName.ToLower())
            {
                case "netflix": return new Uri($"https://www.netflix.com/search?q={q}");
                case "amazon prime video": return new Uri($"https://www.amazon.com/s?k={q}&i=instant-video");
                case "disney plus":
                case "disney+": return new Uri($"https://www.disneyplus.com/search?q={q}");
                case "hulu": return new Uri($"https://www.hulu.com/search?q={q}");
                case "apple tv plus":
                case "apple tv":
                case "apple tv+": return new Uri($"https://tv.apple.com/search?term={q}");
                case "hbo max":
                case "max": return new Uri($"https://play.max.com/search?q={q}");
                case "peacock":
                case "peacock premium": return new Uri($"https://www.peacocktv.com/watch/search?q={q}");
                case "paramount plus":
                case "paramount+": return new Uri($"https://www.paramountplus.com/search/?q={q}");
                case "crunchyroll": return new Uri($"https://www.crunchyroll.com/search?q={q}");
                case "youtube": return new Uri($"https://www.youtube.com/results?search_query={q}");
                default:
                    string cleanName = providerName.ToLower().Replace(" ", "");
                    return new Uri($"https://{cleanName}.com/search?q={q}");
            }
        }'''
        
    if old_method in content:
        content = content.replace(old_method, new_method)
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Fixed {filepath}")
    else:
        print(f"Could not find exact old_method in {filepath}")

fix_file('Pages/StreamingMoviesPage.xaml.cs')
fix_file('Pages/StreamingTvShowsPage.xaml.cs')
