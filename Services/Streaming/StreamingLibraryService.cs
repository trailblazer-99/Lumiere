using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LumiereMediaPlayer.Services.Streaming
{
    public enum StreamingItemType
    {
        Music,
        Movie,
        TvShow
    }

    public partial class SavedStreamingItem : ObservableObject
    {
        [ObservableProperty] public partial string Id { get; set; } = string.Empty;
        [ObservableProperty] public partial string Title { get; set; } = string.Empty;
        [ObservableProperty] public partial string Subtitle { get; set; } = string.Empty; // Artist or Year
        [ObservableProperty] public partial string? PosterUrl { get; set; }
        [ObservableProperty] public partial StreamingItemType Type { get; set; }
        [ObservableProperty] public partial string Watchlist { get; set; } = "Watchlist";
    }

    public class StreamingLibraryService : IStreamingLibraryService
    {
        private readonly object _lock = new();
        private readonly string _filePath;
        private List<SavedStreamingItem> _savedItems = new();

        public List<SavedStreamingItem> SavedItems
        {
            get
            {
                lock (_lock)
                {
                    return new List<SavedStreamingItem>(_savedItems);
                }
            }
        }

        public StreamingLibraryService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "LumiereMediaPlayer");
            Directory.CreateDirectory(appFolder);
            _filePath = Path.Combine(appFolder, "streaming_library.json");

            Load();
        }

        public void AddItem(SavedStreamingItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) return;
            lock (_lock)
            {
                if (!_savedItems.Exists(i => i.Id == item.Id && i.Type == item.Type))
                {
                    _savedItems.Add(item);
                    SaveInternal();
                }
            }
        }

        public void RemoveItem(string id, StreamingItemType type)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (_lock)
            {
                var item = _savedItems.Find(i => i.Id == id && i.Type == type);
                if (item != null)
                {
                    _savedItems.Remove(item);
                    SaveInternal();
                }
            }
        }

        public void Load()
        {
            lock (_lock)
            {
                if (File.Exists(_filePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_filePath);
                        if (string.IsNullOrWhiteSpace(json))
                        {
                            _savedItems = new List<SavedStreamingItem>();
                            return;
                        }
                        _savedItems = JsonSerializer.Deserialize<List<SavedStreamingItem>>(json) ?? new List<SavedStreamingItem>();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[StreamingLibraryService] Load error: {ex.Message}");
                        _savedItems = new List<SavedStreamingItem>();
                    }
                }
                else
                {
                    _savedItems = new List<SavedStreamingItem>();
                }
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                SaveInternal();
            }
        }

        private void SaveInternal()
        {
            string? tempFile = null;
            try
            {
                var json = JsonSerializer.Serialize(_savedItems, new JsonSerializerOptions { WriteIndented = true });
                var dir = Path.GetDirectoryName(_filePath) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                tempFile = Path.Combine(dir, $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, _filePath, overwrite: true);
                tempFile = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingLibraryService] Save error: {ex.Message}");
            }
            finally
            {
                if (tempFile != null && File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }
}
