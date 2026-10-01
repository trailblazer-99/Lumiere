using System;
using System.Collections.Generic;
using System.Linq;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Thread-safe playlist queue and track progression manager.
/// </summary>
public class QueueManager : IQueueManager
{
    private readonly List<MediaItem> _items = [];
    private readonly object _syncRoot = new();
    private int _currentIndex = -1;

    public IReadOnlyList<MediaItem> Items
    {
        get
        {
            lock (_syncRoot)
            {
                return _items.ToList();
            }
        }
    }

    public int CurrentIndex
    {
        get
        {
            lock (_syncRoot) return _currentIndex;
        }
        set
        {
            lock (_syncRoot)
            {
                if (_currentIndex != value)
                {
                    _currentIndex = (_items.Count == 0) ? -1 : Math.Clamp(value, 0, _items.Count - 1);
                }
            }
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public MediaItem? CurrentTrack
    {
        get
        {
            lock (_syncRoot)
            {
                if (_currentIndex >= 0 && _currentIndex < _items.Count)
                {
                    return _items[_currentIndex];
                }
                return null;
            }
        }
    }

    private PlaybackRepeatMode _repeatMode = PlaybackRepeatMode.Off;
    public PlaybackRepeatMode RepeatMode
    {
        get
        {
            lock (_syncRoot) return _repeatMode;
        }
        set
        {
            lock (_syncRoot)
            {
                if (_repeatMode != value)
                {
                    _repeatMode = value;
                }
            }
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasNext
    {
        get
        {
            lock (_syncRoot)
            {
                if (_items.Count == 0 || _currentIndex < 0) return false;
                if (_repeatMode != PlaybackRepeatMode.Off) return true;
                return _currentIndex < _items.Count - 1;
            }
        }
    }

    public bool HasPrevious
    {
        get
        {
            lock (_syncRoot)
            {
                if (_items.Count == 0 || _currentIndex < 0) return false;
                if (_repeatMode == PlaybackRepeatMode.All) return true;
                return _currentIndex > 0;
            }
        }
    }

    public event EventHandler? QueueChanged;

    public void SetQueue(IEnumerable<MediaItem> items, int startIndex = 0)
    {
        lock (_syncRoot)
        {
            _items.Clear();
            if (items != null)
            {
                _items.AddRange(items);
            }
            _currentIndex = _items.Count == 0 ? -1 : Math.Clamp(startIndex, 0, _items.Count - 1);
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Add(MediaItem item)
    {
        if (item == null) return;
        lock (_syncRoot)
        {
            _items.Add(item);
            if (_currentIndex == -1 && _items.Count > 0)
            {
                _currentIndex = 0;
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveAt(int index)
    {
        lock (_syncRoot)
        {
            if (index < 0 || index >= _items.Count) return;

            _items.RemoveAt(index);
            if (_items.Count == 0)
            {
                _currentIndex = -1;
            }
            else if (index < _currentIndex)
            {
                _currentIndex--;
            }
            else if (index == _currentIndex)
            {
                _currentIndex = Math.Min(_currentIndex, _items.Count - 1);
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Enqueue(MediaItem item) => Add(item);

    public void EnqueueRange(IEnumerable<MediaItem> items)
    {
        if (items == null) return;
        var list = items.ToList();
        if (list.Count == 0) return;

        lock (_syncRoot)
        {
            _items.AddRange(list);
            if (_currentIndex == -1 && _items.Count > 0)
            {
                _currentIndex = 0;
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void InsertNext(MediaItem item)
    {
        if (item == null) return;
        lock (_syncRoot)
        {
            if (_items.Count == 0 || _currentIndex < 0)
            {
                _items.Add(item);
                _currentIndex = 0;
            }
            else
            {
                _items.Insert(_currentIndex + 1, item);
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void InsertNextRange(IEnumerable<MediaItem> items)
    {
        if (items == null) return;
        var list = items.ToList();
        if (list.Count == 0) return;

        lock (_syncRoot)
        {
            if (_items.Count == 0 || _currentIndex < 0)
            {
                _items.AddRange(list);
                _currentIndex = 0;
            }
            else
            {
                _items.InsertRange(_currentIndex + 1, list);
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool MoveNext()
    {
        bool changed = false;
        lock (_syncRoot)
        {
            if (_items.Count == 0) return false;

            if (_repeatMode == PlaybackRepeatMode.One)
            {
                changed = true;
            }
            else if (_currentIndex + 1 < _items.Count)
            {
                _currentIndex++;
                changed = true;
            }
            else if (_repeatMode == PlaybackRepeatMode.All)
            {
                _currentIndex = 0;
                changed = true;
            }
        }
        if (changed)
        {
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        return changed;
    }

    public bool MovePrevious()
    {
        bool changed = false;
        lock (_syncRoot)
        {
            if (_items.Count == 0) return false;

            if (_currentIndex > 0)
            {
                _currentIndex--;
                changed = true;
            }
            else if (_repeatMode == PlaybackRepeatMode.All)
            {
                _currentIndex = _items.Count - 1;
                changed = true;
            }
        }
        if (changed)
        {
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }
        return changed;
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            _items.Clear();
            _currentIndex = -1;
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Shuffle()
    {
        lock (_syncRoot)
        {
            if (_items.Count <= 1) return;

            var current = CurrentTrack;
            var rng = new Random();
            int n = _items.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                (_items[k], _items[n]) = (_items[n], _items[k]);
            }

            if (current != null)
            {
                int newIndex = _items.IndexOf(current);
                if (newIndex >= 0)
                {
                    _currentIndex = newIndex;
                }
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Move(int oldIndex, int newIndex)
    {
        lock (_syncRoot)
        {
            if (oldIndex < 0 || oldIndex >= _items.Count || newIndex < 0 || newIndex >= _items.Count || oldIndex == newIndex)
                return;

            var item = _items[oldIndex];
            _items.RemoveAt(oldIndex);
            _items.Insert(newIndex, item);

            if (_currentIndex == oldIndex)
            {
                _currentIndex = newIndex;
            }
            else if (oldIndex < _currentIndex && newIndex >= _currentIndex)
            {
                _currentIndex--;
            }
            else if (oldIndex > _currentIndex && newIndex <= _currentIndex)
            {
                _currentIndex++;
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reorder(IEnumerable<MediaItem> items)
    {
        if (items == null) return;
        lock (_syncRoot)
        {
            var current = CurrentTrack;
            _items.Clear();
            _items.AddRange(items);
            if (current != null)
            {
                int newIndex = _items.IndexOf(current);
                if (newIndex >= 0)
                {
                    _currentIndex = newIndex;
                }
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }
}
