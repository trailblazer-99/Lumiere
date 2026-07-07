using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;
using Windows.Storage;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Helpers;

public static class MediaMetadataScanner
{
    public static async Task ScanMetadataAsync(MediaItem item)
    {
        if (item == null || string.IsNullOrEmpty(item.SourcePath)) return;

        // Skip if already scanned
        if (!string.IsNullOrEmpty(item.Resolution) && item.Resolution != "Unknown" &&
            !string.IsNullOrEmpty(item.Codec) && item.Codec != "Unknown" &&
            item.Bitrate > 0)
        {
            return;
        }

        try
        {
            string path = item.SourcePath;
            // Get playable path in case it is transcode-redirected
            string? playablePath = await AudioPipelineHelper.GetPlayableFileAsync(path);
            if (string.IsNullOrEmpty(playablePath)) playablePath = path;

            if (!File.Exists(playablePath)) return;

            var storageFile = await StorageFile.GetFileFromPathAsync(playablePath);
            
            // Query video properties via Windows Storage APIs
            var videoProps = await storageFile.Properties.GetVideoPropertiesAsync();
            string resolution = $"{videoProps.Width}x{videoProps.Height}";
            uint bitrate = videoProps.Bitrate;

            // Query extra properties like FrameRate and FourCC
            string codec = "Unknown";
            double frameRate = 0;

            try
            {
                var extraProps = await storageFile.Properties.RetrievePropertiesAsync(new[] { "System.Video.FourCC", "System.Video.FrameRate" });
                if (extraProps.TryGetValue("System.Video.FourCC", out var fourCcVal) && fourCcVal is string fourCcStr)
                {
                    codec = fourCcStr;
                }
                if (extraProps.TryGetValue("System.Video.FrameRate", out var frVal) && frVal is uint frUint)
                {
                    frameRate = frUint / 1000.0;
                }
            }
            catch { }

            // Fallback for codec using TagLib
            if (codec == "Unknown" || string.IsNullOrEmpty(codec))
            {
                try
                {
                    using var tagFile = TagLib.File.Create(playablePath);
                    var videoCodec = tagFile.Properties.Codecs.FirstOrDefault(c => c.MediaTypes == TagLib.MediaTypes.Video)?.Description;
                    if (!string.IsNullOrEmpty(videoCodec))
                    {
                        codec = videoCodec;
                    }
                    else if (!string.IsNullOrEmpty(tagFile.Properties.Description))
                    {
                        codec = tagFile.Properties.Description;
                    }
                }
                catch { }
            }

            // Apply results on main dispatcher to update UI cleanly
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(async () =>
            {
                if (string.IsNullOrEmpty(item.Resolution) || item.Resolution == "Unknown")
                {
                    item.Resolution = resolution;
                }
                if (string.IsNullOrEmpty(item.Codec) || item.Codec == "Unknown")
                {
                    item.Codec = codec;
                }
                if (item.Bitrate == 0)
                {
                    item.Bitrate = bitrate;
                }
                if (item.FrameRate == 0)
                {
                    item.FrameRate = frameRate;
                }

                // Auto save changes back to cache json
                await Services.SampleMediaLibrary.SaveLibraryAsync();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MediaMetadataScanner] Error scanning: {ex.Message}");
        }
    }
}
