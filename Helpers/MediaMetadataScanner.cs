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

            StorageFile? storageFile = null;

            // Try to resolve from FutureAccessList first (most secure & always works for picked files)
            try
            {
                if (Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(item.Id))
                {
                    storageFile = await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFileAsync(item.Id);
                }
            }
            catch { }

            // If not in FutureAccessList, try getting by path
            if (storageFile == null)
            {
                try
                {
                    if (File.Exists(playablePath))
                    {
                        storageFile = await StorageFile.GetFileFromPathAsync(playablePath);
                    }
                }
                catch { }
            }

            long fileSize = 0;
            if (storageFile != null)
            {
                try
                {
                    var basicProps = await storageFile.GetBasicPropertiesAsync();
                    fileSize = (long)basicProps.Size;
                }
                catch { }
            }

            if (fileSize == 0 && File.Exists(playablePath))
            {
                try
                {
                    var fileInfo = new FileInfo(playablePath);
                    fileSize = fileInfo.Length;
                }
                catch { }
            }

            string resolution = "Unknown";
            string codec = "Unknown";
            uint bitrate = 0;
            double frameRate = 0;

            // Attempt to query via UWP Storage APIs
            if (storageFile != null)
            {
                try
                {
                    var videoProps = await storageFile.Properties.GetVideoPropertiesAsync();
                    if (videoProps.Width > 0 && videoProps.Height > 0)
                    {
                        resolution = $"{videoProps.Width}x{videoProps.Height}";
                    }
                    bitrate = videoProps.Bitrate;

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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MediaMetadataScanner] Sandbox UWP storage properties failed: {ex.Message}");
                }
            }

            // Fallback for resolution and codec using TagLib with authorization streams to avoid locks & permissions block
            if (codec == "Unknown" || string.IsNullOrEmpty(codec) || resolution == "Unknown" || resolution == "0x0" || string.IsNullOrEmpty(resolution))
            {
                try
                {
                    Stream? stream = null;
                    if (storageFile != null)
                    {
                        try
                        {
                            var randomAccessStream = await storageFile.OpenAsync(FileAccessMode.Read);
                            stream = randomAccessStream.AsStreamForRead();
                        }
                        catch { }
                    }

                    // Fallback to direct Win32 FileStream if storageFile stream failed
                    if (stream == null && File.Exists(playablePath))
                    {
                        try
                        {
                            stream = new FileStream(playablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        }
                        catch { }
                    }

                    if (stream != null)
                    {
                        using (stream)
                        using (var tagFile = TagLib.File.Create(new StreamFileAbstraction(playablePath, stream)))
                        {
                            if (codec == "Unknown" || string.IsNullOrEmpty(codec))
                            {
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

                            if (resolution == "Unknown" || resolution == "0x0" || string.IsNullOrEmpty(resolution))
                            {
                                int w = tagFile.Properties.VideoWidth;
                                int wHeight = tagFile.Properties.VideoHeight;
                                if (w > 0 && wHeight > 0)
                                {
                                    resolution = $"{w}x{wHeight}";
                                }
                            }
                        }
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
                if (item.FileSize == 0)
                {
                    item.FileSize = fileSize;
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

public class StreamFileAbstraction : TagLib.File.IFileAbstraction
{
    public StreamFileAbstraction(string name, Stream stream)
    {
        Name = name;
        ReadStream = stream;
        WriteStream = stream;
    }

    public string Name { get; }
    public Stream ReadStream { get; }
    public Stream WriteStream { get; }

    public void CloseStream(Stream stream)
    {
        // Handled by calling method using blocks
    }
}
