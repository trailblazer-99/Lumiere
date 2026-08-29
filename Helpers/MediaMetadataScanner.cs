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

        // Skip if already fully scanned with rich container metadata
        if (!string.IsNullOrEmpty(item.Resolution) && item.Resolution != "Unknown" &&
            !string.IsNullOrEmpty(item.Codec) && item.Codec != "Unknown" &&
            item.Bitrate > 0 &&
            !string.IsNullOrEmpty(item.AudioTracksSummary) && item.AudioTracksSummary != "Unknown")
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
        string? posterUrl = item.PosterUrl;

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
                if (extraProps.TryGetValue("System.Video.FourCC", out var fourCcVal) && fourCcVal is string fourCcStr && !string.IsNullOrWhiteSpace(fourCcStr) && !fourCcStr.Equals("und", StringComparison.OrdinalIgnoreCase))
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
                    if (codec == "Unknown" || codec == "und" || string.IsNullOrEmpty(codec))
                    {
                        var videoCodec = tagFile.Properties.Codecs.FirstOrDefault(c => c.MediaTypes == TagLib.MediaTypes.Video)?.Description;
                        if (!string.IsNullOrEmpty(videoCodec) && !videoCodec.Equals("und", StringComparison.OrdinalIgnoreCase))
                        {
                            codec = videoCodec;
                        }
                        else if (!string.IsNullOrEmpty(tagFile.Properties.Description) && !tagFile.Properties.Description.Equals("und", StringComparison.OrdinalIgnoreCase))
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

                    // Extract container tracks and advanced stream metadata
                    var containerTracks = MediaTrackFormatHelper.GetContainerTracks(playablePath);
                    var videoTrack = containerTracks.Find(t => t.TrackType == 1);
                    if (videoTrack != null)
                    {
                        if (videoTrack.CodecId.Contains("HEVC") || videoTrack.CodecId.Contains("H265")) codec = "HEVC (H.265)";
                        else if (videoTrack.CodecId.Contains("AVC") || videoTrack.CodecId.Contains("H264")) codec = "AVC (H.264)";
                        else if (videoTrack.CodecId.Contains("AV1") || videoTrack.CodecId.Contains("AV01")) codec = "AV1";
                        else if (videoTrack.CodecId.Contains("VP9")) codec = "VP9";
                    }

                    if (codec == "Unknown" || codec == "und" || string.IsNullOrEmpty(codec))
                    {
                        string fNameLower = Path.GetFileName(playablePath).ToLowerInvariant();
                        if (fNameLower.Contains("x265") || fNameLower.Contains("hevc") || fNameLower.Contains("h265")) codec = "HEVC (H.265)";
                        else if (fNameLower.Contains("x264") || fNameLower.Contains("h264") || fNameLower.Contains("avc")) codec = "AVC (H.264)";
                        else if (fNameLower.Contains("av1") || fNameLower.Contains("av01")) codec = "AV1";
                        else if (fNameLower.Contains("vp9")) codec = "VP9";
                    }

                    // Extract embedded album art if poster is missing
                    if (string.IsNullOrEmpty(posterUrl) && tagFile.Tag?.Pictures?.Length > 0)
                    {
                        try
                        {
                            var pic = tagFile.Tag.Pictures[0];
                            if (pic.Data?.Data != null && pic.Data.Data.Length > 0)
                            {
                                var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                                var thumbDir = Path.Combine(localFolder, "Thumbnails");
                                Directory.CreateDirectory(thumbDir);
                                var thumbPath = Path.Combine(thumbDir, $"{item.Id}.jpg");
                                await File.WriteAllBytesAsync(thumbPath, pic.Data.Data);
                                posterUrl = thumbPath;
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        // If poster is still missing and storageFile is available, extract thumbnail via Windows Storage
        if (string.IsNullOrEmpty(posterUrl) && storageFile != null)
        {
            try
            {
                var thumbMode = item.Kind == MediaKind.Video 
                    ? Windows.Storage.FileProperties.ThumbnailMode.VideosView 
                    : Windows.Storage.FileProperties.ThumbnailMode.MusicView;
                using var thumb = await storageFile.GetThumbnailAsync(thumbMode, 300, Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);
                if (thumb != null && thumb.Size > 0)
                {
                    var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                    var thumbDir = Path.Combine(localFolder, "Thumbnails");
                    Directory.CreateDirectory(thumbDir);
                    var thumbPath = Path.Combine(thumbDir, $"{item.Id}.jpg");
                    using var outStream = File.Create(thumbPath);
                    using var inStream = thumb.AsStreamForRead();
                    await inStream.CopyToAsync(outStream);
                    posterUrl = thumbPath;
                }
            }
            catch { }
        }

        // Apply results on main dispatcher to update UI cleanly
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(item.Resolution) || item.Resolution == "Unknown")
                {
                    item.Resolution = resolution;
                }
                if (string.IsNullOrEmpty(item.Codec) || item.Codec == "Unknown" || item.Codec.Equals("und", StringComparison.OrdinalIgnoreCase))
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
                if (string.IsNullOrEmpty(item.PosterUrl) && !string.IsNullOrEmpty(posterUrl))
                {
                    item.PosterUrl = posterUrl;
                }

                // Populate container properties
                var cTracks = MediaTrackFormatHelper.GetContainerTracks(playablePath);
                var aTracks = cTracks.FindAll(t => t.TrackType == 2);
                var sTracks = cTracks.FindAll(t => t.TrackType == 17);
                var vTrack = cTracks.Find(t => t.TrackType == 1);

                if (aTracks.Count > 0)
                {
                    var pAudio = aTracks[0];
                    string pCodec = pAudio.CodecId.Contains("TRUEHD") ? "Dolby TrueHD" :
                                    pAudio.CodecId.Contains("EAC3") ? "Dolby Digital Plus" :
                                    pAudio.CodecId.Contains("AC3") ? "Dolby Digital" :
                                    pAudio.CodecId.Contains("DTS") ? "DTS" :
                                    pAudio.CodecId.Contains("FLAC") ? "FLAC" :
                                    pAudio.CodecId.Contains("AAC") ? "AAC" : pAudio.CodecId;
                    string pChannels = pAudio.Channels == 8 ? "7.1" : pAudio.Channels == 6 ? "5.1" : pAudio.Channels == 2 ? "2.0" : $"{pAudio.Channels}ch";
                    item.AudioFormat = $"{pCodec} {pChannels}";
                    if (!string.IsNullOrEmpty(pAudio.Name) && !pAudio.Name.Equals(pAudio.Language, StringComparison.OrdinalIgnoreCase))
                    {
                        item.AudioFormat = $"{pAudio.Name} ({pCodec})";
                    }

                    var names = new List<string>();
                    foreach (var a in aTracks.Take(3))
                    {
                        string cName = a.CodecId.Contains("TRUEHD") ? "TrueHD" :
                                       a.CodecId.Contains("EAC3") ? "E-AC3" :
                                       a.CodecId.Contains("AC3") ? "AC3" :
                                       a.CodecId.Contains("DTS") ? "DTS" : a.CodecId;
                        if (a.IsCommentary || (!string.IsNullOrEmpty(a.Name) && a.Name.Contains("Commentary", StringComparison.OrdinalIgnoreCase)))
                            names.Add("Commentary");
                        else if (!string.IsNullOrEmpty(a.Name) && a.Name.Contains("Atmos", StringComparison.OrdinalIgnoreCase))
                            names.Add($"{cName} Atmos");
                        else
                            names.Add(cName);
                    }
                    item.AudioTracksSummary = $"{aTracks.Count} Streams ({string.Join(", ", names)})";
                }

                if (sTracks.Count > 0)
                {
                    item.SubtitlesSummary = $"{sTracks.Count} Streams (PGS, SRT, UTF8)";
                }

                if (vTrack != null && !string.IsNullOrEmpty(vTrack.Name))
                {
                    item.Encoder = vTrack.Name;
                }

                string fName = Path.GetFileName(playablePath);
                if (fName.Contains("DV", StringComparison.OrdinalIgnoreCase) || fName.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase))
                {
                    item.HdrFormat = fName.Contains("HDR", StringComparison.OrdinalIgnoreCase) ? "Dolby Vision / HDR10" : "Dolby Vision";
                    item.BitDepth = "10-bit";
                }
                else if (fName.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                {
                    item.HdrFormat = fName.Contains("HDR10+", StringComparison.OrdinalIgnoreCase) ? "HDR10+" : "HDR10";
                    item.BitDepth = "10-bit";
                }

                if (fName.Contains("10bit", StringComparison.OrdinalIgnoreCase) || fName.Contains("10-bit", StringComparison.OrdinalIgnoreCase))
                {
                    item.BitDepth = "10-bit";
                }

                if (fName.Contains("IMAX", StringComparison.OrdinalIgnoreCase))
                {
                    item.AspectRatio = "1.90:1 (IMAX Enhanced)";
                }

                item.ContainerFormat = Path.GetExtension(playablePath).ToUpperInvariant() switch
                {
                    ".MKV" => "Matroska Video (.mkv)",
                    ".MP4" => "MPEG-4 Video (.mp4)",
                    ".M4V" => "iTunes Video (.m4v)",
                    ".MOV" => "QuickTime Video (.mov)",
                    ".WEBM" => "WebM Video (.webm)",
                    ".AVI" => "Audio Video Interleave (.avi)",
                    ".WMV" => "Windows Media Video (.wmv)",
                    ".FLAC" => "Free Lossless Audio (.flac)",
                    ".MP3" => "MPEG Layer-3 Audio (.mp3)",
                    ".AAC" => "Advanced Audio Coding (.aac)",
                    _ => Path.GetExtension(playablePath).ToUpperInvariant()
                };

                // Auto save changes back to cache json debounced
                Services.SampleMediaLibrary.RequestDebouncedSave();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaMetadataScanner] UI update error: {ex.Message}");
            }
        });
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[MediaMetadataScanner] Error scanning: {ex.Message}");
    }
}

public static async Task<string?> ExtractThumbnailAsync(MediaItem item)
{
    if (item == null || string.IsNullOrEmpty(item.SourcePath)) return null;
    if (!string.IsNullOrEmpty(item.PosterUrl)) return item.PosterUrl;

    try
    {
        string path = item.SourcePath;
        StorageFile? storageFile = null;

        try
        {
            if (Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(item.Id))
            {
                storageFile = await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFileAsync(item.Id);
            }
        }
        catch { }

        if (storageFile == null && File.Exists(path))
        {
            try { storageFile = await StorageFile.GetFileFromPathAsync(path); } catch { }
        }

        // 1. Try TagLib
        if (File.Exists(path))
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var tagFile = TagLib.File.Create(new StreamFileAbstraction(path, stream));
                if (tagFile.Tag?.Pictures?.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    if (pic.Data?.Data != null && pic.Data.Data.Length > 0)
                    {
                        var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                        var thumbDir = Path.Combine(localFolder, "Thumbnails");
                        Directory.CreateDirectory(thumbDir);
                        var thumbPath = Path.Combine(thumbDir, $"{item.Id}.jpg");
                        await File.WriteAllBytesAsync(thumbPath, pic.Data.Data);
                        return thumbPath;
                    }
                }
            }
            catch { }
        }

        // 2. Try Windows Shell Thumbnail
        if (storageFile != null)
        {
            try
            {
                var thumbMode = item.Kind == MediaKind.Video 
                    ? Windows.Storage.FileProperties.ThumbnailMode.VideosView 
                    : Windows.Storage.FileProperties.ThumbnailMode.MusicView;
                using var thumb = await storageFile.GetThumbnailAsync(thumbMode, 300, Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);
                if (thumb != null && thumb.Size > 0)
                {
                    var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                    var thumbDir = Path.Combine(localFolder, "Thumbnails");
                    Directory.CreateDirectory(thumbDir);
                    var thumbPath = Path.Combine(thumbDir, $"{item.Id}.jpg");
                    using var outStream = File.Create(thumbPath);
                    using var inStream = thumb.AsStreamForRead();
                    await inStream.CopyToAsync(outStream);
                    return thumbPath;
                }
            }
            catch { }
        }
    }
    catch { }
    return null;
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
