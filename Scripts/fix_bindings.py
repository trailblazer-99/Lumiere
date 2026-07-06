import re

def fix_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Movies
    content = content.replace(
        '<DataTemplate x:DataType="models:TmdbMedia">',
        '<DataTemplate>'
    ).replace(
        '{Binding DisplayTitle}',
        '{Binding Title}'
    ).replace(
        '{Binding DisplayDate}',
        '{Binding Subtitle}'
    ).replace(
        '<DataTemplate x:DataType="models:ITunesTrack">',
        '<DataTemplate>'
    ).replace(
        '{Binding TrackName}',
        '{Binding Title}'
    ).replace(
        '{Binding ArtistName}',
        '{Binding Subtitle}'
    ).replace(
        '{Binding HighResArtworkUrl}',
        '{Binding PosterUrl}'
    )

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

# wait, if I replace all DataTemplates it will break the Discover tab!
# I ONLY want to replace it in the Library PivotItem.
