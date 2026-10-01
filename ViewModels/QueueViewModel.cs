using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml;

using System;

namespace LumiereMediaPlayer.ViewModels;

public partial class QueueViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackViewModel _playback;
    private readonly EventHandler _stateChangedHandler;

    public QueueViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        _stateChangedHandler = (_, _) => RefreshQueue();
        _playback.Session.StateChanged += _stateChangedHandler;
        RefreshQueue();
    }

    public void Dispose()
    {
        _playback.Session.StateChanged -= _stateChangedHandler;
    }

    public System.Collections.ObjectModel.ObservableCollection<QueueEntry> Entries { get; } = new();

    public bool IsEmpty => Entries.Count == 0;

    public Visibility EmptyMessageVisibility => VisibilityHelper.FromBoolean(IsEmpty);

    [RelayCommand]
    private void PlayEntry(QueueEntry? entry)
    {
        if (entry is not null)
        {
            _playback.PlayQueueItemAt(entry.Index);
        }
    }

    [RelayCommand]
    private void RemoveEntry(QueueEntry? entry)
    {
        if (entry is not null)
        {
            _playback.RemoveFromQueueAt(entry.Index);
        }
    }

    public void SyncOrderFromEntries()
    {
        var newTracks = Entries.Select(e => e.Track).ToList();
        _playback.ReorderQueue(newTracks);
    }

    private void RefreshQueue()
    {
        var newEntries = _playback.Queue
            .Select((track, index) => new QueueEntry
            {
                Track = track,
                Index = index,
                IsCurrent = index == _playback.Session.CurrentIndex
            })
            .ToList();

        Entries.UpdateInPlace(newEntries);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessageVisibility));
    }
}
