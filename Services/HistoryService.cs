using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services
{
    public class HistoryService : IHistoryService
    {
        private const int MaxHistoryItems = 50;
        private static readonly string HistoryFilePath = GetHistoryFilePath();
        private readonly object _collectionLock = new();
        private static readonly System.Threading.SemaphoreSlim _fileLock = new(1, 1);

        private static string GetHistoryFilePath()
        {
            try
            {
                return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "playback_history.json");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryService] LocalFolder access failed, using fallback: {ex.Message}");
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = Path.Combine(appData, "LumiereMediaPlayer");
                try { Directory.CreateDirectory(dir); } catch (Exception dirEx) { System.Diagnostics.Debug.WriteLine($"[HistoryService] CreateDirectory failed: {dirEx.Message}"); }
                return Path.Combine(dir, "playback_history.json");
            }
        }

        public ObservableCollection<MediaItem> RecentlyPlayed { get; } = new();
        public bool IsLoaded { get; private set; }
        public event EventHandler? HistoryLoaded;

        public async Task LoadHistoryAsync()
        {
            string? json = null;
            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (File.Exists(HistoryFilePath))
                {
                    json = await File.ReadAllTextAsync(HistoryFilePath).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryService] Read HistoryFilePath error: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }

            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<MediaItem[]>(json);
                    if (loaded != null)
                    {
                        var validItems = loaded
                            .Where(item =>
                            {
                                if (!string.IsNullOrEmpty(item.SourcePath) && Path.IsPathRooted(item.SourcePath))
                                {
                                    return File.Exists(item.SourcePath);
                                }
                                return true;
                            })
                            .Take(MaxHistoryItems)
                            .ToArray();

                        foreach (var item in validItems)
                        {
                            item.IsSelected = false;
                        }

                        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                        var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

                        void Populate()
                        {
                            try
                            {
                                lock (_collectionLock)
                                {
                                    RecentlyPlayed.UpdateInPlace(validItems);
                                }
                                IsLoaded = true;
                                HistoryLoaded?.Invoke(this, EventArgs.Empty);
                                tcs.TrySetResult(true);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[HistoryService] Populate error: {ex.Message}");
                                tcs.TrySetResult(false);
                            }
                        }

                        if (dispatcher?.HasThreadAccess == true)
                        {
                            Populate();
                        }
                        else if (dispatcher != null)
                        {
                            if (!dispatcher.TryEnqueue(Populate))
                            {
                                tcs.TrySetResult(false);
                            }
                        }
                        else
                        {
                            Populate();
                        }

                        try { await tcs.Task; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] Populate await error: {ex.Message}"); }

                        if (validItems.Length != loaded.Length)
                        {
                            await SaveHistoryAsync();
                        }
                        return;
                    }
                    else
                    {
                        MarkLoaded();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HistoryService] LoadHistoryAsync error: {ex.Message}");
                    MarkLoaded();
                    return;
                }
            }

            MarkLoaded();
        }

        private void MarkLoaded()
        {
            void SetLoaded()
            {
                IsLoaded = true;
                HistoryLoaded?.Invoke(this, EventArgs.Empty);
            }

            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher != null && !dispatcher.HasThreadAccess)
            {
                dispatcher.TryEnqueue(SetLoaded);
            }
            else
            {
                SetLoaded();
            }
        }

        public async Task RemoveMissingItemsAsync()
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

            void Action()
            {
                try
                {
                    List<MediaItem> toRemove;
                    lock (_collectionLock)
                    {
                        toRemove = RecentlyPlayed
                            .Where(item => !string.IsNullOrEmpty(item.SourcePath) && Path.IsPathRooted(item.SourcePath) && !File.Exists(item.SourcePath))
                            .ToList();

                        if (toRemove.Count > 0)
                        {
                            foreach (var item in toRemove)
                            {
                                try { RecentlyPlayed.Remove(item); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] Remove missing item failed: {ex.Message}"); }
                            }
                        }
                    }

                    tcs.TrySetResult(toRemove.Count > 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HistoryService] RemoveMissingItemsAsync error: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            }

            if (dispatcher?.HasThreadAccess == true)
            {
                Action();
            }
            else if (dispatcher != null)
            {
                if (!dispatcher.TryEnqueue(Action))
                {
                    tcs.TrySetResult(false);
                }
            }
            else
            {
                Action();
            }

            try
            {
                bool removed = await tcs.Task;
                if (removed)
                {
                    await SaveHistoryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryService] RemoveMissingItemsAsync await error: {ex.Message}");
            }
        }

        public async Task SaveHistoryAsync()
        {
            try
            {
                MediaItem[] items;
                lock (_collectionLock)
                {
                    items = RecentlyPlayed.ToArray();
                }

                await _fileLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    var json = JsonSerializer.Serialize(items);
                    await File.WriteAllTextAsync(HistoryFilePath, json).ConfigureAwait(false);
                }
                finally
                {
                    _fileLock.Release();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryService] SaveHistoryAsync error: {ex.Message}");
            }
        }

        public async Task AddToHistoryAsync(MediaItem item)
        {
            if (item == null) return;
            item.IsSelected = false;

            // Await UI thread update before serializing to ensure data consistency
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

            void Mutation()
            {
                try
                {
                    lock (_collectionLock)
                    {
                        var existing = RecentlyPlayed.FirstOrDefault(x => x.Id == item.Id || x.SourcePath == item.SourcePath);
                        if (existing != null)
                        {
                            try { RecentlyPlayed.Remove(existing); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] Remove existing failed: {ex.Message}"); }
                        }

                        try { RecentlyPlayed.Insert(0, item); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] Insert item failed: {ex.Message}"); }

                        while (RecentlyPlayed.Count > MaxHistoryItems)
                        {
                            try { RecentlyPlayed.RemoveAt(RecentlyPlayed.Count - 1); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] Trim item failed: {ex.Message}"); break; }
                        }
                    }
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HistoryService] AddToHistoryAsync mutation error: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            }

            if (dispatcher?.HasThreadAccess == true)
            {
                Mutation();
            }
            else if (dispatcher != null)
            {
                if (!dispatcher.TryEnqueue(Mutation))
                {
                    tcs.TrySetResult(false);
                }
            }
            else
            {
                Mutation();
            }

            try { await tcs.Task; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] AddToHistoryAsync await error: {ex.Message}"); }
            await SaveHistoryAsync();
        }

        public async Task RemoveFromHistoryAsync(MediaItem item)
        {
            if (item == null) return;

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

            void Mutation()
            {
                try
                {
                    bool removed = false;
                    lock (_collectionLock)
                    {
                        var matches = RecentlyPlayed.Where(x =>
                            (!string.IsNullOrEmpty(item.Id) && x.Id == item.Id) ||
                            (!string.IsNullOrEmpty(item.SourcePath) && !string.IsNullOrEmpty(x.SourcePath) && string.Equals(x.SourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(item.Title) && !string.IsNullOrEmpty(x.Title) && string.Equals(x.Title, item.Title, StringComparison.OrdinalIgnoreCase) &&
                             (string.IsNullOrEmpty(item.Artist) || string.IsNullOrEmpty(x.Artist) || string.Equals(x.Artist, item.Artist, StringComparison.OrdinalIgnoreCase))))
                            .ToList();

                        foreach (var m in matches)
                        {
                            try { if (RecentlyPlayed.Remove(m)) removed = true; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] Remove match failed: {ex.Message}"); }
                        }

                        if (!removed)
                        {
                            try { removed = RecentlyPlayed.Remove(item); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] Remove item fallback failed: {ex.Message}"); }
                        }
                    }

                    tcs.TrySetResult(removed);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HistoryService] RemoveFromHistoryAsync mutation error: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            }

            if (dispatcher?.HasThreadAccess == true)
            {
                Mutation();
            }
            else if (dispatcher != null)
            {
                if (!dispatcher.TryEnqueue(Mutation))
                {
                    tcs.TrySetResult(false);
                }
            }
            else
            {
                Mutation();
            }

            try
            {
                bool removed = await tcs.Task;
                if (removed)
                {
                    await SaveHistoryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryService] RemoveFromHistoryAsync await error: {ex.Message}");
            }
        }

        public async Task RemoveRangeFromHistoryAsync(IEnumerable<MediaItem> items)
        {
            if (items == null) return;
            var list = items.ToList();
            if (list.Count == 0) return;

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

            void Mutation()
            {
                try
                {
                    bool anyRemoved = false;
                    lock (_collectionLock)
                    {
                        foreach (var it in list)
                        {
                            var matches = RecentlyPlayed.Where(x =>
                                (!string.IsNullOrEmpty(it.Id) && x.Id == it.Id) ||
                                (!string.IsNullOrEmpty(it.SourcePath) && !string.IsNullOrEmpty(x.SourcePath) && string.Equals(x.SourcePath, it.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(it.Title) && !string.IsNullOrEmpty(x.Title) && string.Equals(x.Title, it.Title, StringComparison.OrdinalIgnoreCase) &&
                                 (string.IsNullOrEmpty(it.Artist) || string.IsNullOrEmpty(x.Artist) || string.Equals(x.Artist, it.Artist, StringComparison.OrdinalIgnoreCase))))
                                .ToList();

                            foreach (var m in matches)
                            {
                                try { if (RecentlyPlayed.Remove(m)) anyRemoved = true; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] Remove range match failed: {ex.Message}"); }
                            }

                            if (matches.Count == 0)
                            {
                                try { if (RecentlyPlayed.Remove(it)) anyRemoved = true; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] Remove range item fallback failed: {ex.Message}"); }
                            }
                        }
                    }

                    tcs.TrySetResult(anyRemoved);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HistoryService] RemoveRangeFromHistoryAsync mutation error: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            }

            if (dispatcher?.HasThreadAccess == true)
            {
                Mutation();
            }
            else if (dispatcher != null)
            {
                if (!dispatcher.TryEnqueue(Mutation))
                {
                    tcs.TrySetResult(false);
                }
            }
            else
            {
                Mutation();
            }

            try
            {
                bool removed = await tcs.Task;
                if (removed)
                {
                    await SaveHistoryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HistoryService] RemoveRangeFromHistoryAsync await error: {ex.Message}");
            }
        }

        public async Task ClearHistoryAsync()
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

            void Mutation()
            {
                try
                {
                    lock (_collectionLock)
                    {
                        RecentlyPlayed.Clear();
                    }
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HistoryService] ClearHistoryAsync mutation error: {ex.Message}");
                    tcs.TrySetResult(false);
                }
            }

            if (dispatcher?.HasThreadAccess == true)
            {
                Mutation();
            }
            else if (dispatcher != null)
            {
                if (!dispatcher.TryEnqueue(Mutation))
                {
                    tcs.TrySetResult(false);
                }
            }
            else
            {
                Mutation();
            }

            try { await tcs.Task; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HistoryService] ClearHistoryAsync await error: {ex.Message}"); }
            await SaveHistoryAsync();
        }
    }
}
