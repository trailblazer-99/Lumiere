import re

def fix_music(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_logic = '''var url = $"https://duckduckgo.com/?q=!ducky+Listen+to+{encodedTitle}+by+{encodedArtist}+on+{Uri.EscapeDataString(p.Name)}";'''
    new_logic = '''var q = Uri.EscapeDataString(trackName + " " + artistName);
            Uri searchUri = null;
            switch (p.Name.ToLower())
            {
                case "spotify": searchUri = new Uri($"spotify:search:{q}"); break;
                case "apple music": searchUri = new Uri($"https://music.apple.com/search?term={q}"); break;
                case "youtube music": searchUri = new Uri($"https://music.youtube.com/search?q={q}"); break;
                case "amazon music": searchUri = new Uri($"https://music.amazon.com/search/{q}"); break;
                case "soundcloud": searchUri = new Uri($"https://soundcloud.com/search/sounds?q={q}"); break;
                case "tidal": searchUri = new Uri($"https://listen.tidal.com/search?q={q}"); break;
                case "deezer": searchUri = new Uri($"https://www.deezer.com/search/{q}"); break;
                case "pandora": searchUri = new Uri($"https://www.pandora.com/search/{q}/all"); break;
                default: 
                    string cleanName = p.Name.ToLower().Replace(" ", "");
                    searchUri = new Uri($"https://{cleanName}.com/search?q={q}"); 
                    break;
            }
            var url = searchUri.ToString();'''
    
    content = content.replace(old_logic, new_logic)
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_music('Pages/NowPlayingPage.xaml.cs')

def fix_video(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    old_logic = '''var searchUri = new Uri($"https://duckduckgo.com/?q=!ducky+Watch+{encodedTitle}+on+{encodedProvider}");'''
    new_logic = '''var q = Uri.EscapeDataString(title);
                            Uri searchUri = null;
                            switch (provider.ProviderName.ToLower())
                            {
                                case "netflix": searchUri = new Uri($"https://www.netflix.com/search?q={q}"); break;
                                case "amazon prime video": searchUri = new Uri($"https://www.amazon.com/s?k={q}&i=instant-video"); break;
                                case "disney plus":
                                case "disney+": searchUri = new Uri($"https://www.disneyplus.com/search?q={q}"); break;
                                case "hulu": searchUri = new Uri($"https://www.hulu.com/search?q={q}"); break;
                                case "apple tv plus":
                                case "apple tv":
                                case "apple tv+": searchUri = new Uri($"https://tv.apple.com/search?term={q}"); break;
                                case "hbo max":
                                case "max": searchUri = new Uri($"https://play.max.com/search?q={q}"); break;
                                case "peacock":
                                case "peacock premium": searchUri = new Uri($"https://www.peacocktv.com/watch/search?q={q}"); break;
                                case "paramount plus":
                                case "paramount+": searchUri = new Uri($"https://www.paramountplus.com/search/?q={q}"); break;
                                case "crunchyroll": searchUri = new Uri($"https://www.crunchyroll.com/search?q={q}"); break;
                                case "youtube": searchUri = new Uri($"https://www.youtube.com/results?search_query={q}"); break;
                                default: 
                                    string cleanName = provider.ProviderName.ToLower().Replace(" ", "");
                                    searchUri = new Uri($"https://{cleanName}.com/search?q={q}"); 
                                    break;
                            }'''
    
    content = content.replace(old_logic, new_logic)
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_video('Pages/VideoPage.xaml.cs')

print("Done")
