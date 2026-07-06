using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FluentMediaPlayer.Services.Streaming
{
    public enum StreamingItemType
    {
        Music,
        Movie,
        TvShow
    }

    public class SavedStreamingItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty; // Artist or Year
        public string? PosterUrl { get; set; }
        public StreamingItemType Type { get; set; }
        public string Watchlist { get; set; } = "Watchlist";
    }

    public class StreamingLibraryService
    {
        private readonly string _filePath;
        public List<SavedStreamingItem> SavedItems { get; private set; }

        public StreamingLibraryService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "FluentMediaPlayer");
            Directory.CreateDirectory(appFolder);
            _filePath = Path.Combine(appFolder, "streaming_library.json");
            
            SavedItems = new List<SavedStreamingItem>();
            Load();
        }

        public void AddItem(SavedStreamingItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id)) return;
            if (!SavedItems.Exists(i => i.Id == item.Id && i.Type == item.Type))
            {
                SavedItems.Add(item);
                Save();
            }
        }

        public void RemoveItem(string id, StreamingItemType type)
        {
            if (string.IsNullOrEmpty(id)) return;
            var item = SavedItems.Find(i => i.Id == id && i.Type == type);
            if (item != null)
            {
                SavedItems.Remove(item);
                Save();
            }
        }

        public void Load()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        SavedItems = new List<SavedStreamingItem>();
                        return;
                    }
                    SavedItems = JsonSerializer.Deserialize<List<SavedStreamingItem>>(json) ?? new List<SavedStreamingItem>();
                }
                catch
                {
                    SavedItems = new List<SavedStreamingItem>();
                }
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(SavedItems, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Ignore save errors
            }
        }
    }
}
