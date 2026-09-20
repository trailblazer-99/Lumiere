using System;
using System.Collections.Generic;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Manages the playlist queue, track progression, shuffle state, and current index.
/// </summary>
public interface IQueueManager
{
    IReadOnlyList<MediaItem> Items { get; }
    int CurrentIndex { get; set; }
    MediaItem? CurrentTrack { get; }
    bool HasNext { get; }
    bool HasPrevious { get; }

    event EventHandler? QueueChanged;

    void SetQueue(IEnumerable<MediaItem> items, int startIndex = 0);
    void Add(MediaItem item);
    void RemoveAt(int index);
    void Enqueue(MediaItem item);
    void EnqueueRange(IEnumerable<MediaItem> items);
    void InsertNext(MediaItem item);
    void InsertNextRange(IEnumerable<MediaItem> items);
    bool MoveNext();
    bool MovePrevious();
    void Clear();
    void Shuffle();
}
