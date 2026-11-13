using Microsoft.UI.Xaml;

namespace LumiereMediaPlayer.Models.Streaming
{
    public class TreeViewItemContent
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public Visibility SubtitleVisibility => string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DescriptionVisibility => string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible;

        public WatchmodeEpisode? Episode { get; set; }
    }
}
