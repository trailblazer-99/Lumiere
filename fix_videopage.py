import re

with open('Pages/VideoPage.xaml.cs', 'r', encoding='utf-8') as f:
    cs_content = f.read()

# Replace SyncMediaPlayer implementation
pattern_sync = r'(internal void SyncMediaPlayer\(\).*?)(?=\n    private void OnPageLoaded)'

replacement_sync = '''internal void SyncMediaPlayer()
    {
        if (ViewModel.CurrentVideo is not null && AppServices.PlaybackViewModel.IsVideoPlayerActive)
        {
            var globalPlayer = AppServices.PlaybackViewModel.Session.MediaPlayer;
            if (VideoPlayer.MediaPlayer != globalPlayer)
            {
                if (VideoPlayer.MediaPlayer != null)
                {
                    VideoPlayer.MediaPlayer.MediaOpened -= OnMediaOpened;
                }

                VideoPlayer.SetMediaPlayer(globalPlayer);
                globalPlayer.MediaOpened += OnMediaOpened;

                if (globalPlayer.PlaybackSession.NaturalVideoWidth > 0 && globalPlayer.PlaybackSession.NaturalVideoHeight > 0)
                {
                    VideoPlayer.Width = globalPlayer.PlaybackSession.NaturalVideoWidth;
                    VideoPlayer.Height = globalPlayer.PlaybackSession.NaturalVideoHeight;
                }
            }
            return;
        }

        // Restore default layout
        VideoPlayer.SetMediaPlayer(null);
    }
'''

new_cs = re.sub(pattern_sync, replacement_sync, cs_content, flags=re.DOTALL)
if new_cs != cs_content:
    with open('Pages/VideoPage.xaml.cs', 'w', encoding='utf-8') as f:
        f.write(new_cs)
    print("Fixed VideoPage.xaml.cs")
else:
    print("Could not find SyncMediaPlayer to replace.")

with open('Pages/VideoPage.xaml', 'r', encoding='utf-8') as f:
    xaml_content = f.read()

# Update HeaderPanel visibility
xaml_content = xaml_content.replace(
    '<StackPanel x:Name="HeaderPanel" Spacing="6">', 
    '<StackPanel x:Name="HeaderPanel" Spacing="6" Visibility="{x:Bind ViewModel.OverlayVisibility, Mode=OneWay}">'
)

# Set VideoPlayerContainer background to Black
xaml_content = xaml_content.replace(
    'Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"\\n                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"\\n                BorderThickness="1"\\n                CornerRadius="{StaticResource LargeCornerRadius}"\\n                DoubleTapped="OnVideoDoubleTapped"',
    'Background="Black"\\n                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"\\n                BorderThickness="1"\\n                CornerRadius="{StaticResource LargeCornerRadius}"\\n                DoubleTapped="OnVideoDoubleTapped"'
)

with open('Pages/VideoPage.xaml', 'w', encoding='utf-8') as f:
    f.write(xaml_content)
print("Fixed VideoPage.xaml")
