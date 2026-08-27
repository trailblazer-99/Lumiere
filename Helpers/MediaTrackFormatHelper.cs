using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Helpers;

public class ContainerTrackInfo
{
    public int Number;
    public int TrackType; // 1=video, 2=audio, 17=subtitle
    public string CodecId = "";
    public string Name = "";
    public string Language = "";
    public int Channels = 0;
    public double SampleRate = 0;
    public int BitDepth = 0;
    public bool IsCommentary;
    public bool IsHearingImpaired;
    public bool IsVisualImpaired;
    public bool IsForced;
    public bool IsDefault;
}

/// <summary>
/// Formats raw WinRT AudioTrack and TimedMetadataTrack objects into rich, informative human-readable labels.
/// </summary>
public static class MediaTrackFormatHelper
{
    private static readonly Dictionary<string, (DateTime Loaded, List<ContainerTrackInfo> Tracks)> ContainerCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    private static readonly Dictionary<string, string> IsoLanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "en", "English" }, { "eng", "English" },
        { "es", "Spanish" }, { "spa", "Spanish" },
        { "fr", "French" }, { "fre", "French" }, { "fra", "French" },
        { "de", "German" }, { "ger", "German" }, { "deu", "German" },
        { "it", "Italian" }, { "ita", "Italian" },
        { "pt", "Portuguese" }, { "por", "Portuguese" },
        { "ja", "Japanese" }, { "jpn", "Japanese" },
        { "ko", "Korean" }, { "kor", "Korean" },
        { "zh", "Chinese" }, { "zho", "Chinese" }, { "chi", "Chinese" },
        { "ru", "Russian" }, { "rus", "Russian" },
        { "hi", "Hindi" }, { "hin", "Hindi" },
        { "ar", "Arabic" }, { "ara", "Arabic" },
        { "bn", "Bengali" }, { "ben", "Bengali" },
        { "nl", "Dutch" }, { "dut", "Dutch" }, { "nld", "Dutch" },
        { "pl", "Polish" }, { "pol", "Polish" },
        { "sv", "Swedish" }, { "swe", "Swedish" },
        { "no", "Norwegian" }, { "nor", "Norwegian" },
        { "da", "Danish" }, { "dan", "Danish" },
        { "fi", "Finnish" }, { "fin", "Finnish" },
        { "tr", "Turkish" }, { "tur", "Turkish" },
        { "el", "Greek" }, { "gre", "Greek" }, { "ell", "Greek" },
        { "he", "Hebrew" }, { "heb", "Hebrew" },
        { "th", "Thai" }, { "tha", "Thai" },
        { "vi", "Vietnamese" }, { "vie", "Vietnamese" },
        { "id", "Indonesian" }, { "ind", "Indonesian" },
        { "cs", "Czech" }, { "cze", "Czech" }, { "ces", "Czech" },
        { "hu", "Hungarian" }, { "hun", "Hungarian" },
        { "ro", "Romanian" }, { "rum", "Romanian" }, { "ron", "Romanian" },
        { "uk", "Ukrainian" }, { "ukr", "Ukrainian" },
        { "aze", "Azerbaijani" }, { "bul", "Bulgarian" }, { "est", "Estonian" },
        { "lav", "Latvian" }, { "lit", "Lithuanian" }, { "slo", "Slovak" },
        { "und", "Undetermined" }, { "mul", "Multiple Languages" }, { "zxx", "No Linguistic Content" }
    };

    public static List<ContainerTrackInfo> GetContainerTracks(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<ContainerTrackInfo>();

        lock (CacheLock)
        {
            if (ContainerCache.TryGetValue(path, out var cached) && (DateTime.UtcNow - cached.Loaded).TotalMinutes < 30)
            {
                return cached.Tracks;
            }
        }

        var tracks = new List<ContainerTrackInfo>();
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext is ".mkv" or ".webm" or ".mka")
        {
            tracks = ParseMkvTracks(path);
        }

        lock (CacheLock)
        {
            ContainerCache[path] = (DateTime.UtcNow, tracks);
        }

        return tracks;
    }

    public record SidecarTrack(string FilePath, string DisplayName, string Language, string Extension);

    public static List<SidecarTrack> GetSidecarAudioFiles(string? mediaPath)
    {
        var list = new List<SidecarTrack>();
        if (string.IsNullOrEmpty(mediaPath) || !File.Exists(mediaPath)) return list;

        try
        {
            var dir = Path.GetDirectoryName(mediaPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;

            var baseName = Path.GetFileNameWithoutExtension(mediaPath);
            var coreTitle = baseName.Split('(')[0].Trim();

            var audioExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ac3", ".eac3", ".dts", ".flac", ".aac", ".m4a", ".mp3", ".wav", ".opus", ".ogg" };
            foreach (var file in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (!audioExtensions.Contains(ext)) continue;
                if (file.Equals(mediaPath, StringComparison.OrdinalIgnoreCase)) continue;

                var fName = Path.GetFileNameWithoutExtension(file);
                if (fName.StartsWith(coreTitle, StringComparison.OrdinalIgnoreCase) || fName.Contains(coreTitle, StringComparison.OrdinalIgnoreCase))
                {
                    string desc = fName;
                    if (desc.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        desc = desc.Substring(baseName.Length).Trim(' ', '-', '—', '.', '_');
                    }
                    else if (desc.StartsWith(coreTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        desc = desc.Substring(coreTitle.Length).Trim(' ', '-', '—', '.', '_');
                    }

                    if (string.IsNullOrEmpty(desc)) desc = Path.GetFileName(file);

                    string lang = "English";
                    if (desc.Contains("French", StringComparison.OrdinalIgnoreCase)) lang = "French";
                    else if (desc.Contains("German", StringComparison.OrdinalIgnoreCase)) lang = "German";
                    else if (desc.Contains("Spanish", StringComparison.OrdinalIgnoreCase)) lang = "Spanish";
                    else if (desc.Contains("Italian", StringComparison.OrdinalIgnoreCase)) lang = "Italian";
                    else if (desc.Contains("Japanese", StringComparison.OrdinalIgnoreCase)) lang = "Japanese";

                    string format = ext.TrimStart('.').ToUpperInvariant();
                    string displayName = $"{lang} • {desc} [{format} External]";

                    list.Add(new SidecarTrack(file, displayName, lang, ext));
                }
            }
        }
        catch { }

        return list;
    }

    public static List<SidecarTrack> GetSidecarSubtitleFiles(string? mediaPath)
    {
        var list = new List<SidecarTrack>();
        if (string.IsNullOrEmpty(mediaPath) || !File.Exists(mediaPath)) return list;

        try
        {
            var dir = Path.GetDirectoryName(mediaPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;

            var baseName = Path.GetFileNameWithoutExtension(mediaPath);
            var coreTitle = baseName.Split('(')[0].Trim();

            var subExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".srt", ".vtt", ".ass", ".ssa", ".sup", ".sub" };
            foreach (var file in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (!subExtensions.Contains(ext)) continue;
                if (file.Equals(mediaPath, StringComparison.OrdinalIgnoreCase)) continue;

                var fName = Path.GetFileNameWithoutExtension(file);
                if (fName.StartsWith(coreTitle, StringComparison.OrdinalIgnoreCase) || fName.Contains(coreTitle, StringComparison.OrdinalIgnoreCase))
                {
                    string desc = fName;
                    if (desc.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                    {
                        desc = desc.Substring(baseName.Length).Trim(' ', '-', '—', '.', '_');
                    }
                    else if (desc.StartsWith(coreTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        desc = desc.Substring(coreTitle.Length).Trim(' ', '-', '—', '.', '_');
                    }

                    if (string.IsNullOrEmpty(desc)) desc = Path.GetFileName(file);

                    string lang = "English";
                    if (desc.Contains("French", StringComparison.OrdinalIgnoreCase)) lang = "French";
                    else if (desc.Contains("German", StringComparison.OrdinalIgnoreCase)) lang = "German";
                    else if (desc.Contains("Spanish", StringComparison.OrdinalIgnoreCase)) lang = "Spanish";
                    else if (desc.Contains("Italian", StringComparison.OrdinalIgnoreCase)) lang = "Italian";
                    else if (desc.Contains("Japanese", StringComparison.OrdinalIgnoreCase)) lang = "Japanese";

                    string format = ext.TrimStart('.').ToUpperInvariant();
                    string displayName = $"{lang} • {desc} [{format} External]";

                    list.Add(new SidecarTrack(file, displayName, lang, ext));
                }
            }
        }
        catch { }

        return list;
    }

    /// <summary>
    /// Formats an audio track into a rich informative label:
    /// e.g. "English • Dolby TrueHD Atmos 7.1 (48 kHz) [Unsupported]"
    /// </summary>
    public static string FormatAudioTrack(AudioTrack track, int trackIndex, string? sourcePath = null)
    {
        if (track == null) return $"Track {trackIndex + 1}";

        sourcePath ??= AppServices.PlaybackViewModel.CurrentTrack?.SourcePath;
        var containerTracks = GetContainerTracks(sourcePath);
        var containerAudioTracks = containerTracks.FindAll(t => t.TrackType == 2);

        ContainerTrackInfo? cTrack = null;
        if (trackIndex >= 0 && trackIndex < containerAudioTracks.Count)
        {
            cTrack = containerAudioTracks[trackIndex];
        }

        string rawLang = !string.IsNullOrEmpty(track.Language) ? track.Language : (cTrack?.Language ?? "en");
        string language = ResolveLanguage(rawLang);
        string label = track.Label?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(label) && cTrack != null && !string.IsNullOrEmpty(cTrack.Name))
        {
            label = cTrack.Name;
        }

        // Extract encoding details
        string codec = string.Empty;
        string channelLayout = string.Empty;
        string sampleRateStr = string.Empty;
        string bitrateStr = string.Empty;
        bool isUnsupportedCodec = false;

        if (cTrack != null)
        {
            if (cTrack.CodecId.Contains("TRUEHD"))
            {
                codec = "Dolby TrueHD";
                if (label.Contains("ATMOS", StringComparison.OrdinalIgnoreCase) || cTrack.Name.Contains("ATMOS", StringComparison.OrdinalIgnoreCase))
                {
                    codec = "Dolby TrueHD Atmos";
                }
                // Windows Media Foundation does not natively decode TrueHD streams
                isUnsupportedCodec = true;
            }
            else if (cTrack.CodecId.Contains("EAC3"))
            {
                codec = (label.Contains("ATMOS", StringComparison.OrdinalIgnoreCase) || cTrack.Name.Contains("ATMOS", StringComparison.OrdinalIgnoreCase))
                    ? "Dolby Atmos (E-AC3)"
                    : "Dolby Digital Plus (E-AC3)";
            }
            else if (cTrack.CodecId.Contains("AC3"))
            {
                codec = "Dolby Digital (AC3)";
            }
            else if (cTrack.CodecId.Contains("DTS"))
            {
                codec = cTrack.CodecId.Contains("HD") ? "DTS-HD MA" : "DTS Surround";
            }
            else if (cTrack.CodecId.Contains("AAC"))
            {
                codec = "AAC";
            }
            else if (cTrack.CodecId.Contains("FLAC"))
            {
                codec = "FLAC Lossless";
            }

            if (cTrack.Channels > 0)
            {
                channelLayout = ResolveChannels((uint)cTrack.Channels);
            }
            if (cTrack.SampleRate > 0)
            {
                sampleRateStr = $"{(cTrack.SampleRate / 1000.0):0.#} kHz";
            }
        }

        try
        {
            var props = track.GetEncodingProperties();
            if (props != null)
            {
                if (string.IsNullOrEmpty(codec)) codec = ResolveAudioCodec(props.Subtype, label);
                if (string.IsNullOrEmpty(channelLayout) && props.ChannelCount > 0) channelLayout = ResolveChannels(props.ChannelCount);

                if (string.IsNullOrEmpty(sampleRateStr) && props.SampleRate > 0)
                {
                    sampleRateStr = props.SampleRate >= 1000
                        ? $"{(props.SampleRate / 1000.0):0.#} kHz"
                        : $"{props.SampleRate} Hz";
                }

                if (props.Bitrate > 0)
                {
                    bitrateStr = $"{props.Bitrate / 1000} kbps";
                }
            }
        }
        catch { }

        // Check for commentary or description
        bool isCommentary = (cTrack?.IsCommentary ?? false) || label.Contains("Commentary", StringComparison.OrdinalIgnoreCase);
        bool isDescriptive = (cTrack?.IsVisualImpaired ?? false) || label.Contains("Descriptive", StringComparison.OrdinalIgnoreCase) || label.Contains("Audio Description", StringComparison.OrdinalIgnoreCase);

        // If label is specific (e.g. "Commentary by Director Jon Favreau")
        if (!string.IsNullOrEmpty(label) &&
            !label.Equals(rawLang, StringComparison.OrdinalIgnoreCase) &&
            !label.Equals($"Track {trackIndex + 1}", StringComparison.OrdinalIgnoreCase) &&
            !label.Equals("Dolby Atmos 7.1 Mix", StringComparison.OrdinalIgnoreCase))
        {
            string desc = label;
            if (desc.StartsWith(language, StringComparison.OrdinalIgnoreCase))
            {
                desc = desc.Substring(language.Length).Trim(' ', '-', '—', ':');
            }

            var partsWithDesc = new List<string> { language };
            if (!string.IsNullOrEmpty(desc)) partsWithDesc.Add(desc);
            if (!string.IsNullOrEmpty(channelLayout)) partsWithDesc.Add(channelLayout);
            if (isUnsupportedCodec) partsWithDesc.Add("[Unsupported]");

            return string.Join(" • ", partsWithDesc);
        }

        // Build standard composite string
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(language)) parts.Add(language);

        var technicalParts = new List<string>();
        if (!string.IsNullOrEmpty(codec)) technicalParts.Add(codec);
        if (!string.IsNullOrEmpty(channelLayout)) technicalParts.Add(channelLayout);

        string techSummary = string.Join(" ", technicalParts);
        if (!string.IsNullOrEmpty(techSummary)) parts.Add(techSummary);

        if (isCommentary && !string.IsNullOrEmpty(label)) parts.Add(label);
        else if (isDescriptive) parts.Add("Descriptive Audio");

        if (isUnsupportedCodec)
        {
            parts.Add("[Unsupported by OS]");
        }

        var metaDetails = new List<string>();
        if (!string.IsNullOrEmpty(sampleRateStr)) metaDetails.Add(sampleRateStr);
        else if (!string.IsNullOrEmpty(bitrateStr)) metaDetails.Add(bitrateStr);

        string detailsPart = metaDetails.Count > 0 ? $"({string.Join(", ", metaDetails)})" : string.Empty;

        if (parts.Count == 0) return $"Track {trackIndex + 1}";

        string mainTitle = string.Join(" • ", parts);
        if (!string.IsNullOrEmpty(detailsPart))
        {
            mainTitle = $"{mainTitle} {detailsPart}";
        }

        return mainTitle;
    }

    /// <summary>
    /// Formats a subtitle track into a clean human-readable label:
    /// e.g. "English [Commentary by Director Jon Favreau]" or "French [Canadian]"
    /// </summary>
    public static string FormatSubtitleTrack(TimedMetadataTrack track, int trackIndex, string? sourcePath = null)
    {
        if (track == null) return $"Subtitle {trackIndex + 1}";

        sourcePath ??= AppServices.PlaybackViewModel.CurrentTrack?.SourcePath;
        var containerTracks = GetContainerTracks(sourcePath);
        var containerSubTracks = containerTracks.FindAll(t => t.TrackType == 17);

        ContainerTrackInfo? cTrack = null;
        if (trackIndex >= 0 && trackIndex < containerSubTracks.Count)
        {
            cTrack = containerSubTracks[trackIndex];
        }

        string rawLang = !string.IsNullOrEmpty(track.Language) ? track.Language : (cTrack?.Language ?? "en");
        string language = ResolveLanguage(rawLang);
        string label = track.Label?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(label) && cTrack != null && !string.IsNullOrEmpty(cTrack.Name))
        {
            label = cTrack.Name;
        }

        if (!string.IsNullOrEmpty(label) &&
            !label.Equals(rawLang, StringComparison.OrdinalIgnoreCase) &&
            !label.Equals($"Track {trackIndex + 1}", StringComparison.OrdinalIgnoreCase))
        {
            if (label.StartsWith(language, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
            return $"{language} [{label}]";
        }

        if (cTrack != null)
        {
            if (cTrack.IsForced) return $"{language} [Forced]";
            if (cTrack.IsHearingImpaired) return $"{language} [SDH]";
        }

        if (track.TimedMetadataKind == TimedMetadataKind.Caption)
        {
            return $"{language} [CC]";
        }

        return language;
    }

    private static List<ContainerTrackInfo> ParseMkvTracks(string path)
    {
        var list = new List<ContainerTrackInfo>();
        try
        {
            using var fs = File.OpenRead(path);
            byte[] buf = new byte[Math.Min(fs.Length, 8 * 1024 * 1024)];
            int read = fs.Read(buf, 0, buf.Length);

            int pos = 0;
            while (pos < read - 8)
            {
                if (buf[pos] == 0x16 && buf[pos + 1] == 0x54 && buf[pos + 2] == 0xAE && buf[pos + 3] == 0x6B)
                {
                    int testPos = pos + 4;
                    long tracksLen = ReadVInt(buf, ref testPos);
                    if (tracksLen > 0 && testPos < read && buf[testPos] == 0xAE)
                    {
                        int tracksEnd = (int)Math.Min(read, testPos + tracksLen);
                        ParseTracksInternal(buf, testPos, tracksEnd, list);
                        if (list.Count > 0) break;
                    }
                }
                pos++;
            }
        }
        catch { }
        return list;
    }

    private static void ParseTracksInternal(byte[] buf, int start, int end, List<ContainerTrackInfo> list)
    {
        int pos = start;
        while (pos < end)
        {
            int elemId = ReadId(buf, ref pos);
            if (elemId == 0) break;
            long len = ReadVInt(buf, ref pos);
            if (len < 0) break;
            int elemEnd = (int)Math.Min(end, pos + len);

            if (elemId == 0xAE) // TrackEntry
            {
                var track = new ContainerTrackInfo();
                ParseTrackEntryInternal(buf, pos, elemEnd, track);
                list.Add(track);
            }
            pos = elemEnd;
        }
    }

    private static void ParseTrackEntryInternal(byte[] buf, int start, int end, ContainerTrackInfo t)
    {
        int pos = start;
        while (pos < end)
        {
            int elemId = ReadId(buf, ref pos);
            if (elemId == 0) break;
            long len = ReadVInt(buf, ref pos);
            if (len < 0) break;
            int elemEnd = (int)Math.Min(end, pos + len);

            switch (elemId)
            {
                case 0xD7: // TrackNumber
                    t.Number = (int)ReadUint(buf, pos, (int)len);
                    break;
                case 0x83: // TrackType
                    t.TrackType = (int)ReadUint(buf, pos, (int)len);
                    break;
                case 0x86: // CodecID
                    t.CodecId = Encoding.ASCII.GetString(buf, pos, (int)len).TrimEnd('\0');
                    break;
                case 0x536E: // Name
                    t.Name = Encoding.UTF8.GetString(buf, pos, (int)len).TrimEnd('\0');
                    break;
                case 0x22B59C: // Language
                    t.Language = Encoding.ASCII.GetString(buf, pos, (int)len).TrimEnd('\0');
                    break;
                case 0x22B59D: // LanguageIETF
                    if (string.IsNullOrEmpty(t.Language))
                        t.Language = Encoding.ASCII.GetString(buf, pos, (int)len).TrimEnd('\0');
                    break;
                case 0x55EE: // FlagCommentary
                    t.IsCommentary = ReadUint(buf, pos, (int)len) == 1;
                    break;
                case 0x55E9: // FlagHearingImpaired
                    t.IsHearingImpaired = ReadUint(buf, pos, (int)len) == 1;
                    break;
                case 0x55EA: // FlagVisualImpaired
                    t.IsVisualImpaired = ReadUint(buf, pos, (int)len) == 1;
                    break;
                case 0x55AA: // FlagForced
                    t.IsForced = ReadUint(buf, pos, (int)len) == 1;
                    break;
                case 0x88: // FlagDefault
                    t.IsDefault = ReadUint(buf, pos, (int)len) == 1;
                    break;
                case 0xE1: // Audio
                    ParseAudioInternal(buf, pos, elemEnd, t);
                    break;
            }
            pos = elemEnd;
        }
    }

    private static void ParseAudioInternal(byte[] buf, int start, int end, ContainerTrackInfo t)
    {
        int pos = start;
        while (pos < end)
        {
            int elemId = ReadId(buf, ref pos);
            if (elemId == 0) break;
            long len = ReadVInt(buf, ref pos);
            if (len < 0) break;
            int elemEnd = (int)Math.Min(end, pos + len);

            switch (elemId)
            {
                case 0x9F: // Channels
                    t.Channels = (int)ReadUint(buf, pos, (int)len);
                    break;
                case 0xB5: // SamplingFrequency
                    if (len == 4)
                    {
                        var fb = new byte[4];
                        Array.Copy(buf, pos, fb, 0, 4);
                        if (BitConverter.IsLittleEndian) Array.Reverse(fb);
                        t.SampleRate = BitConverter.ToSingle(fb, 0);
                    }
                    else if (len == 8)
                    {
                        var db = new byte[8];
                        Array.Copy(buf, pos, db, 0, 8);
                        if (BitConverter.IsLittleEndian) Array.Reverse(db);
                        t.SampleRate = BitConverter.ToDouble(db, 0);
                    }
                    break;
                case 0x6264: // BitDepth
                    t.BitDepth = (int)ReadUint(buf, pos, (int)len);
                    break;
            }
            pos = elemEnd;
        }
    }

    private static int ReadId(byte[] buf, ref int pos)
    {
        if (pos >= buf.Length) return 0;
        byte b = buf[pos];
        int numBytes;
        if ((b & 0x80) != 0) numBytes = 1;
        else if ((b & 0x40) != 0) numBytes = 2;
        else if ((b & 0x20) != 0) numBytes = 3;
        else if ((b & 0x10) != 0) numBytes = 4;
        else return 0;

        if (pos + numBytes > buf.Length) return 0;
        int id = 0;
        for (int i = 0; i < numBytes; i++)
        {
            id = (id << 8) | buf[pos++];
        }
        return id;
    }

    private static long ReadVInt(byte[] buf, ref int pos)
    {
        if (pos >= buf.Length) return -1;
        byte b = buf[pos];
        int numBytes;
        int mask;
        if ((b & 0x80) != 0) { numBytes = 1; mask = 0x7F; }
        else if ((b & 0x40) != 0) { numBytes = 2; mask = 0x3F; }
        else if ((b & 0x20) != 0) { numBytes = 3; mask = 0x1F; }
        else if ((b & 0x10) != 0) { numBytes = 4; mask = 0x0F; }
        else if ((b & 0x08) != 0) { numBytes = 5; mask = 0x07; }
        else if ((b & 0x04) != 0) { numBytes = 6; mask = 0x03; }
        else if ((b & 0x02) != 0) { numBytes = 7; mask = 0x01; }
        else if ((b & 0x01) != 0) { numBytes = 8; mask = 0x00; }
        else return -1;

        if (pos + numBytes > buf.Length) return -1;
        long val = b & mask;
        pos++;
        for (int i = 1; i < numBytes; i++)
        {
            val = (val << 8) | buf[pos++];
        }
        return val;
    }

    private static ulong ReadUint(byte[] buf, int pos, int len)
    {
        ulong val = 0;
        for (int i = 0; i < len && (pos + i) < buf.Length; i++)
        {
            val = (val << 8) | buf[pos + i];
        }
        return val;
    }

    private static string ResolveLanguage(string? rawLang)
    {
        if (string.IsNullOrWhiteSpace(rawLang))
        {
            return "Audio";
        }

        string clean = rawLang.Trim();

        if (IsoLanguageMap.TryGetValue(clean, out var name))
        {
            return name;
        }

        try
        {
            var culture = new CultureInfo(clean);
            if (!string.IsNullOrEmpty(culture.EnglishName) && !culture.EnglishName.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                var parenIndex = culture.EnglishName.IndexOf('(');
                if (parenIndex > 0)
                {
                    return culture.EnglishName.Substring(0, parenIndex).Trim();
                }
                return culture.EnglishName;
            }
        }
        catch { }

        return clean.Length <= 3 ? clean.ToUpperInvariant() : clean;
    }

    private static string ResolveAudioCodec(string? subtype, string? label)
    {
        string sub = subtype?.ToUpperInvariant() ?? string.Empty;
        string lbl = label?.ToUpperInvariant() ?? string.Empty;

        if (sub.Contains("TRUEHD") || sub.Contains("DOLBY TRUEHD") || sub.Contains("MLP") || sub.Contains("2004") || lbl.Contains("TRUEHD"))
        {
            return lbl.Contains("ATMOS") || sub.Contains("ATMOS") ? "Dolby TrueHD Atmos" : "Dolby TrueHD";
        }
        if (sub.Contains("ATMOS") || lbl.Contains("ATMOS"))
        {
            return "Dolby Atmos";
        }
        if (sub.Contains("EAC3") || sub.Contains("EC-3") || sub.Contains("EC3") || sub.Contains("DIGITAL PLUS") || sub.Contains("DDP") || sub.Contains("2003") || lbl.Contains("EAC3") || lbl.Contains("DDP"))
        {
            return "Dolby Digital Plus (E-AC3)";
        }
        if (sub.Contains("AC3") || sub.Contains("DOLBY DIGITAL") || sub.Contains("A52") || sub.Contains("2000") || lbl.Contains("AC3"))
        {
            return "Dolby Digital (AC3)";
        }
        if (sub.Contains("DTSHD") || sub.Contains("DTS-HD") || sub.Contains("DTSMA") || sub.Contains("DTS-MA") || lbl.Contains("DTS-HD") || lbl.Contains("DTS-MA"))
        {
            return "DTS-HD MA";
        }
        if (sub.Contains("DTS") || sub.Contains("2001") || lbl.Contains("DTS"))
        {
            return "DTS Surround";
        }
        if (sub.Contains("FLAC") || lbl.Contains("FLAC"))
        {
            return "FLAC Lossless";
        }
        if (sub.Contains("ALAC") || lbl.Contains("ALAC"))
        {
            return "Apple Lossless (ALAC)";
        }
        if (sub.Contains("OPUS") || lbl.Contains("OPUS"))
        {
            return "Opus";
        }
        if (sub.Contains("VORBIS") || lbl.Contains("VORBIS") || lbl.Contains("OGG"))
        {
            return "Vorbis";
        }
        if (sub.Contains("AAC") || sub.Contains("AACL") || sub.Contains("AACH") || sub.Contains("MP4A") || sub.Contains("2002") || lbl.Contains("AAC"))
        {
            return "AAC";
        }
        if (sub.Contains("PCM") || sub.Contains("LPCM") || sub.Contains("WAVE") || sub.Contains("0001"))
        {
            return "LPCM Lossless";
        }
        if (sub.Contains("MP3") || sub.Contains("MPEG"))
        {
            return "MP3";
        }

        if (!string.IsNullOrEmpty(subtype) && !subtype.StartsWith("{"))
        {
            return subtype;
        }

        return string.Empty;
    }

    private static string ResolveChannels(uint channelCount)
    {
        return channelCount switch
        {
            8 => "7.1 Surround",
            6 => "5.1 Surround",
            4 => "4.0 Quad",
            3 => "2.1 Stereo",
            2 => "Stereo 2.0",
            1 => "Mono 1.0",
            > 8 => $"{channelCount - 1}.1 Surround",
            _ => string.Empty
        };
    }
}
