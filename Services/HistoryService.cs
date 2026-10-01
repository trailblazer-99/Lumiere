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

        private static string GetHistoryFilePath()
        {
            try
            {
                return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "playback_history.json");
            }
            catch
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = Path.Combine(appData, "LumiereMediaPlayer");
                try { Directory.CreateDirectory(dir); } catch { }
                return Path.Combine(dir, "playback_history.json");
            }
        }

        public ObservableCollection<MediaItem> RecentlyPlayed { get; } = new();
        public bool IsLoaded { get; private set; }
        public event EventHandler? HistoryLoaded;

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
                            .Take(MaxHistoryItems)
                            .ToArray();

                        foreach (var item in validItems)
                        {
                            item.IsSelected = false;
                        }

                        void Populate()
                        {
                            RecentlyPlayed.UpdateInPlace(validItems);
                            IsLoaded = true;
                            HistoryLoaded?.Invoke(this, EventArgs.Empty);
                        }

                        var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
                        if (dispatcher != null && !dispatcher.HasThreadAccess)
                        {
                            dispatcher.TryEnqueue(Populate);
                        }
                        else
                        {
                            Populate();
                        }

                        if (validItems.Length != loaded.Length)
                        {
                            await SaveHistoryAsync();
                        }
                    }
                    else
                    {
                        MarkLoaded();
                    }
                }
                catch
                {
                    MarkLoaded();
                }
            }
            else
            {
                MarkLoaded();
            }
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
            var dispatched = dispatcher?.TryEnqueue(() =>
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
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

            Action mutation = () =>
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
            };

            if (dispatcher?.HasThreadAccess == true)
            {
                mutation();
            }
            else if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => mutation());
            }
            else
            {
                tcs.TrySetResult(false);
            }

            try { await tcs.Task; } catch { }
            await SaveHistoryAsync();
        }

        public async Task RemoveFromHistoryAsync(MediaItem item)
        {
            if (item == null) return;

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

            Action mutation = () =>
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
            };

            if (dispatcher?.HasThreadAccess == true)
            {
                mutation();
            }
            else if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => mutation());
            }
            else
            {
                tcs.TrySetResult(false);
            }

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

        public async Task RemoveRangeFromHistoryAsync(IEnumerable<MediaItem> items)
        {
            if (items == null) return;
            var list = items.ToList();
            if (list.Count == 0) return;

            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

            Action mutation = () =>
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
            };

            if (dispatcher?.HasThreadAccess == true)
            {
                mutation();
            }
            else if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => mutation());
            }
            else
            {
                tcs.TrySetResult(false);
            }

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

        public async Task ClearHistoryAsync()
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;

            Action mutation = () =>
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
            };

            if (dispatcher?.HasThreadAccess == true)
            {
                mutation();
            }
            else if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => mutation());
            }
            else
            {
                tcs.TrySetResult(false);
            }

            try { await tcs.Task; } catch { }
            await SaveHistoryAsync();
        }
    }
}
