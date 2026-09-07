using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services
{
    public class HistoryService
    {
        private const int MaxHistoryItems = 50;
        private static readonly string HistoryFilePath = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "playback_history.json");

        public ObservableCollection<MediaItem> RecentlyPlayed { get; } = new();

        public async Task LoadHistoryAsync()
        {
            if (File.Exists(HistoryFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(HistoryFilePath);
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
                            .ToArray();

                        foreach (var item in validItems)
                        {
                            item.IsSelected = false;
                        }

                        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (RecentlyPlayed.Count == validItems.Length)
                            {
                                for (int i = 0; i < validItems.Length; i++)
                                {
                                    RecentlyPlayed[i] = validItems[i];
                                }
                            }
                            else
                            {
                                RecentlyPlayed.Clear();
                                foreach (var item in validItems)
                                {
                                    RecentlyPlayed.Add(item);
                                }
                            }
                        });

                        if (validItems.Length != loaded.Length)
                        {
                            await SaveHistoryAsync();
                        }
                    }
                }
                catch { }
            }
        }

        public async Task RemoveMissingItemsAsync()
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatched = App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var toRemove = RecentlyPlayed
                        .Where(item => !string.IsNullOrEmpty(item.SourcePath) && Path.IsPathRooted(item.SourcePath) && !File.Exists(item.SourcePath))
                        .ToList();

                    if (toRemove.Count > 0)
                    {
                        foreach (var item in toRemove)
                        {
                            RecentlyPlayed.Remove(item);
                        }
                        tcs.TrySetResult(true);
                    }
                    else
                    {
                        tcs.TrySetResult(false);
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (dispatched == true)
            {
                bool removed = await tcs.Task;
                if (removed)
                {
                    await SaveHistoryAsync();
                }
            }
        }

        public async Task SaveHistoryAsync()
        {
            try
            {
                var items = RecentlyPlayed.ToArray();
                var json = JsonSerializer.Serialize(items);
                await File.WriteAllTextAsync(HistoryFilePath, json);
            }
            catch { }
        }

        public async Task AddToHistoryAsync(MediaItem item)
        {
            if (item == null) return;
            item.IsSelected = false;

            // Await UI thread update before serializing to ensure data consistency
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatched = App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var existing = RecentlyPlayed.FirstOrDefault(x => x.Id == item.Id || x.SourcePath == item.SourcePath);
                    if (existing != null)
                    {
                        RecentlyPlayed.Remove(existing);
                    }

                    RecentlyPlayed.Insert(0, item);

                    while (RecentlyPlayed.Count > MaxHistoryItems)
                    {
                        RecentlyPlayed.RemoveAt(RecentlyPlayed.Count - 1);
                    }
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (dispatched != true)
            {
                tcs.TrySetResult(false);
            }

            await tcs.Task;
            await SaveHistoryAsync();
        }

        public async Task RemoveFromHistoryAsync(MediaItem item)
        {
            if (item == null) return;

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatched = App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var matches = RecentlyPlayed.Where(x =>
                        (!string.IsNullOrEmpty(item.Id) && x.Id == item.Id) ||
                        (!string.IsNullOrEmpty(item.SourcePath) && !string.IsNullOrEmpty(x.SourcePath) && string.Equals(x.SourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(item.Title) && !string.IsNullOrEmpty(x.Title) && string.Equals(x.Title, item.Title, StringComparison.OrdinalIgnoreCase) &&
                         (string.IsNullOrEmpty(item.Artist) || string.IsNullOrEmpty(x.Artist) || string.Equals(x.Artist, item.Artist, StringComparison.OrdinalIgnoreCase))))
                        .ToList();

                    bool removed = false;
                    foreach (var m in matches)
                    {
                        RecentlyPlayed.Remove(m);
                        removed = true;
                    }

                    if (!removed)
                    {
                        removed = RecentlyPlayed.Remove(item);
                    }

                    tcs.TrySetResult(removed);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (dispatched == true)
            {
                try
                {
                    bool removed = await tcs.Task;
                    if (removed)
                    {
                        await SaveHistoryAsync();
                    }
                }
                catch { }
            }
            else
            {
                // Fallback direct removal if dispatcher unavailable
                var match = RecentlyPlayed.FirstOrDefault(x =>
                    (!string.IsNullOrEmpty(item.Id) && x.Id == item.Id) ||
                    (!string.IsNullOrEmpty(item.SourcePath) && !string.IsNullOrEmpty(x.SourcePath) && string.Equals(x.SourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(item.Title) && !string.IsNullOrEmpty(x.Title) && string.Equals(x.Title, item.Title, StringComparison.OrdinalIgnoreCase)));
                if (match != null) RecentlyPlayed.Remove(match);
                else RecentlyPlayed.Remove(item);
                await SaveHistoryAsync();
            }
        }

        public async Task RemoveRangeFromHistoryAsync(IEnumerable<MediaItem> items)
        {
            if (items == null) return;
            var list = items.ToList();
            if (list.Count == 0) return;

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatched = App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    bool anyRemoved = false;
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
                            RecentlyPlayed.Remove(m);
                            anyRemoved = true;
                        }

                        if (matches.Count == 0)
                        {
                            if (RecentlyPlayed.Remove(it)) anyRemoved = true;
                        }
                    }

                    tcs.TrySetResult(anyRemoved);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (dispatched == true)
            {
                try
                {
                    bool removed = await tcs.Task;
                    if (removed)
                    {
                        await SaveHistoryAsync();
                    }
                }
                catch { }
            }
            else
            {
                foreach (var it in list)
                {
                    var match = RecentlyPlayed.FirstOrDefault(x =>
                        (!string.IsNullOrEmpty(it.Id) && x.Id == it.Id) ||
                        (!string.IsNullOrEmpty(it.SourcePath) && !string.IsNullOrEmpty(x.SourcePath) && string.Equals(x.SourcePath, it.SourcePath, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(it.Title) && !string.IsNullOrEmpty(x.Title) && string.Equals(x.Title, it.Title, StringComparison.OrdinalIgnoreCase)));
                    if (match != null) RecentlyPlayed.Remove(match);
                    else RecentlyPlayed.Remove(it);
                }
                await SaveHistoryAsync();
            }
        }

        public async Task ClearHistoryAsync()
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatched = App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    RecentlyPlayed.Clear();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (dispatched == true)
            {
                try { await tcs.Task; } catch { }
            }
            else
            {
                RecentlyPlayed.Clear();
            }

            await SaveHistoryAsync();
        }
    }
}
