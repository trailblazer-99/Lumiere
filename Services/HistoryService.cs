using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentMediaPlayer.Models;

namespace FluentMediaPlayer.Services
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
                        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                        {
                            RecentlyPlayed.Clear();
                            foreach (var item in loaded)
                            {
                                RecentlyPlayed.Add(item);
                            }
                        });
                    }
                }
                catch { }
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


            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
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
            });
            
            await SaveHistoryAsync();
        }

        public async Task ClearHistoryAsync()
        {
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                RecentlyPlayed.Clear();
            });
            await SaveHistoryAsync();
        }
    }
}
