using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Interface for managing playback history.
/// </summary>
public interface IHistoryService
{
    ObservableCollection<MediaItem> RecentlyPlayed { get; }

    Task LoadHistoryAsync();
    Task RemoveMissingItemsAsync();
    Task SaveHistoryAsync();
    Task AddToHistoryAsync(MediaItem item);
    Task RemoveFromHistoryAsync(MediaItem item);
    Task RemoveRangeFromHistoryAsync(IEnumerable<MediaItem> items);
    Task ClearHistoryAsync();
}
