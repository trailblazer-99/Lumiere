import re

def fix_music_page(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Update the providers array
    old_providers = '''                var providers = new[]
                {
                    new { Name = "Spotify", Icon = "https://storage.googleapis.com/pr-newsroom-wp/1/2018/11/Spotify_Logo_RGB_Green.png" },
                    new { Name = "Apple Music", Icon = "https://upload.wikimedia.org/wikipedia/commons/thumb/d/df/Apple_Music_logo.svg/512px-Apple_Music_logo.svg.png" },
                    new { Name = "YouTube Music", Icon = "https://upload.wikimedia.org/wikipedia/commons/thumb/6/69/YouTube_Music.svg/512px-YouTube_Music.svg.png" }
                };'''
    new_providers = '''                var providers = new[]
                {
                    new { Name = "Spotify", Icon = "https://raw.githubusercontent.com/WalkxCode/dashboard-icons/main/png/spotify.png" },
                    new { Name = "Apple Music", Icon = "https://raw.githubusercontent.com/WalkxCode/dashboard-icons/main/png/apple-music.png" },
                    new { Name = "YouTube Music", Icon = "https://raw.githubusercontent.com/WalkxCode/dashboard-icons/main/png/youtube-music.png" }
                };'''
    
    content = content.replace(old_providers, new_providers)

    # Update the url logic
    old_logic = '''var encodedTitle = Uri.EscapeDataString(track.TrackName);
                    var encodedArtist = Uri.EscapeDataString(track.ArtistName);
                    var url = $"https://www.google.com/search?btnI=1&q=Listen+to+{encodedTitle}+by+{encodedArtist}+on+{Uri.EscapeDataString(p.Name)}";'''
    
    new_logic = '''var q = Uri.EscapeDataString(track.TrackName + " " + track.ArtistName);
                    Uri searchUri = null;
                    switch (p.Name.ToLower())
                    {
                        case "spotify": searchUri = new Uri($"spotify:search:{q}"); break;
                        case "apple music": searchUri = new Uri($"https://music.apple.com/search?term={q}"); break;
                        case "youtube music": searchUri = new Uri($"https://music.youtube.com/search?q={q}"); break;
                        default: 
                            string cleanName = p.Name.ToLower().Replace(" ", "");
                            searchUri = new Uri($"https://{cleanName}.com/search?q={q}"); 
                            break;
                    }
                    var url = searchUri.ToString();'''
    
    content = content.replace(old_logic, new_logic)
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

fix_music_page('Pages/StreamingMusicPage.xaml.cs')

print("Done")
