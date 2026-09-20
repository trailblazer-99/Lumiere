using System.Collections.Generic;

namespace LumiereMediaPlayer.Services.Streaming;

public interface IStreamingLibraryService
{
    List<SavedStreamingItem> SavedItems { get; }

    void AddItem(SavedStreamingItem item);
    void RemoveItem(string id, StreamingItemType type);
    void Load();
    void Save();
}
