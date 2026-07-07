using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml;

namespace LumiereMediaPlayer.ViewModels;

public partial class QueueViewModel : ObservableObject
{
    private readonly PlaybackViewModel _playback;

    public QueueViewModel(PlaybackViewModel playback)
    {
        _playback = playback;
        _playback.Session.StateChanged += (_, _) => RefreshQueue();
        RefreshQueue();
    }

    public IReadOnlyList<QueueEntry> Entries { get; private set; } = [];

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

    private void RefreshQueue()
    {
        Entries = _playback.Queue
            .Select((track, index) => new QueueEntry
            {
                Track = track,
                Index = index,
                IsCurrent = index == _playback.Session.CurrentIndex
            })
            .ToList();

        OnPropertyChanged(nameof(Entries));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessageVisibility));
    }
}
