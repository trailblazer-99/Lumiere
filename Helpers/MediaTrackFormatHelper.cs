using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
    public double FrameRate = 0;
    public bool IsCommentary;
    public bool IsHearingImpaired;
    public bool IsVisualImpaired;
    public bool IsForced;
    public bool IsDefault;
    public string HdrFormat = "";
    public int VideoBitDepth = 0;
    public int TransferCharacteristics = 0;
    public int ColorPrimaries = 0;
    public int MatrixCoefficients = 0;
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
        else if (ext is ".mp4" or ".m4v" or ".mov")
        {
            tracks = ParseMp4Tracks(path);
        }

        lock (CacheLock)
        {
            if (ContainerCache.Count >= 200)
            {
                var now = DateTime.UtcNow;
                var expired = ContainerCache.Where(kvp => (now - kvp.Value.Loaded).TotalMinutes >= 30).Select(kvp => kvp.Key).ToList();
                foreach (var k in expired) ContainerCache.Remove(k);

                while (ContainerCache.Count >= 200)
                {
                    var oldestKey = ContainerCache.OrderBy(kvp => kvp.Value.Loaded).Select(kvp => kvp.Key).FirstOrDefault();
                    if (oldestKey != null) ContainerCache.Remove(oldestKey);
                    else break;
                }
            }
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
            int bufLen = (int)Math.Min(fs.Length, 8 * 1024 * 1024);
            byte[] buf = System.Buffers.ArrayPool<byte>.Shared.Rent(bufLen);
            try
            {
                int read = fs.Read(buf, 0, bufLen);

                int pos = 0;
                while (pos < read - 8)
                {
                    if (buf[pos] == 0x16 && buf[pos + 1] == 0x54 && buf[pos + 2] == 0xAE && buf[pos + 3] == 0x6B)
                    {
                        int testPos = pos + 4;
                        long tracksLen = ReadVInt(buf, ref testPos);
                        if (tracksLen > 0 && testPos < read)
                        {
                            int scanPos = testPos;
                            while (scanPos < read - 2 && (buf[scanPos] == 0xBF || buf[scanPos] == 0xEC))
                            {
                                int p = scanPos;
                                int skipId = ReadId(buf, ref p);
                                long skipLen = ReadVInt(buf, ref p);
                                if (skipLen < 0 || p + skipLen > read) break;
                                scanPos = (int)(p + skipLen);
                            }

                            if (scanPos < read && buf[scanPos] == 0xAE)
                            {
                                int tracksEnd = (int)Math.Min(read, testPos + tracksLen);
                                ParseTracksInternal(buf, scanPos, tracksEnd, list);
                                if (list.Count > 0) break;
                            }
                        }
                    }
                    pos++;
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buf);
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
                    if (t.CodecId.Equals("dvh1", StringComparison.OrdinalIgnoreCase) ||
                        t.CodecId.Equals("dvhe", StringComparison.OrdinalIgnoreCase) ||
                        t.CodecId.Equals("dovi", StringComparison.OrdinalIgnoreCase) ||
                        t.CodecId.Contains("DOLBY_VISION", StringComparison.OrdinalIgnoreCase) ||
                        t.CodecId.Contains("DOLBY VISION", StringComparison.OrdinalIgnoreCase) ||
                        t.CodecId.Contains("DOVI", StringComparison.OrdinalIgnoreCase))
                    {
                        t.HdrFormat = "Dolby Vision";
                    }
                    break;
                case 0x536E: // Name
                    t.Name = Encoding.UTF8.GetString(buf, pos, (int)len).TrimEnd('\0');
                    if (t.TrackType == 1 && (t.Name.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase) ||
                                             Regex.IsMatch(t.Name, @"(?i)(?:^|[\s._\-\[(+])(DV|DoVi|Dolby[\s._\-]?Vision)(?:$|[\s._\-\])+])")))
                    {
                        t.HdrFormat = "Dolby Vision";
                    }
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
                case 0x23E383: // DefaultDuration (nanoseconds per frame in MKV)
                    long defDur = (long)ReadUint(buf, pos, (int)len);
                    if (defDur > 0)
                    {
                        t.FrameRate = 1_000_000_000.0 / defDur;
                    }
                    break;
                case 0x63A2: // CodecPrivate
                    if (t.TrackType == 1 && len >= 4)
                    {
                        for (int i = pos; i <= elemEnd - 4; i++)
                        {
                            if ((buf[i] == 'd' && buf[i + 1] == 'v' && buf[i + 2] == 'c' && buf[i + 3] == 'C') ||
                                (buf[i] == 'd' && buf[i + 1] == 'v' && buf[i + 2] == 'v' && buf[i + 3] == 'C') ||
                                (buf[i] == 'd' && buf[i + 1] == 'v' && buf[i + 2] == 'h' && (buf[i + 3] == '1' || buf[i + 3] == 'e')) ||
                                (buf[i] == 'd' && buf[i + 1] == 'o' && buf[i + 2] == 'v' && buf[i + 3] == 'i'))
                            {
                                t.HdrFormat = (t.TransferCharacteristics == 16 || t.HdrFormat.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                                    ? "Dolby Vision / HDR10"
                                    : "Dolby Vision";
                                break;
                            }
                        }

                        ParseHevcCodecPrivate(buf, pos, (int)len, t);
                    }
                    break;
                case 0x41E4: // BlockAdditionMapping
                    if (t.TrackType == 1 && len >= 4)
                    {
                        for (int i = pos; i <= elemEnd - 4; i++)
                        {
                            if ((buf[i] == 'd' && buf[i + 1] == 'v' && buf[i + 2] == 'c' && buf[i + 3] == 'C') ||
                                (buf[i] == 'd' && buf[i + 1] == 'v' && buf[i + 2] == 'v' && buf[i + 3] == 'C') ||
                                (buf[i] == 'd' && buf[i + 1] == 'o' && buf[i + 2] == 'v' && (buf[i + 3] == 'p' || buf[i + 3] == 'i')))
                            {
                                t.HdrFormat = (t.TransferCharacteristics == 16 || t.HdrFormat.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                                    ? "Dolby Vision / HDR10"
                                    : "Dolby Vision";
                                break;
                            }
                        }
                    }
                    break;
                case 0xE0: // Video
                    ParseVideoInternal(buf, pos, elemEnd, t);
                    break;
                case 0xE1: // Audio
                    ParseAudioInternal(buf, pos, elemEnd, t);
                    break;
            }
            pos = elemEnd;
        }
    }

    private static void ParseVideoInternal(byte[] buf, int start, int end, ContainerTrackInfo t)
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
                case 0x23E383: // DefaultDuration inside Video element
                    long defDur = (long)ReadUint(buf, pos, (int)len);
                    if (defDur > 0)
                    {
                        t.FrameRate = 1_000_000_000.0 / defDur;
                    }
                    break;
                case 0x55B0: // Colour element
                    ParseColourInternal(buf, pos, elemEnd, t);
                    break;
            }
            pos = elemEnd;
        }
    }

    private static void ParseColourInternal(byte[] buf, int start, int end, ContainerTrackInfo t)
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
                case 0x55B1: // MatrixCoefficients
                    t.MatrixCoefficients = (int)ReadUint(buf, pos, (int)len);
                    break;
                case 0x55B2: // BitsPerChannel
                    int bpc = (int)ReadUint(buf, pos, (int)len);
                    if (bpc > 0)
                    {
                        t.BitDepth = bpc;
                        t.VideoBitDepth = bpc;
                    }
                    break;
                case 0x55B7: // Primaries (9 = BT.2020)
                    t.ColorPrimaries = (int)ReadUint(buf, pos, (int)len);
                    break;
                case 0x55BA: // TransferCharacteristics (16 = SMPTE ST 2084 / PQ, 18 = ARIB STD-B67 / HLG)
                    t.TransferCharacteristics = (int)ReadUint(buf, pos, (int)len);
                    break;
            }
            pos = elemEnd;
        }

        if (t.TransferCharacteristics == 16)
        {
            bool hasDolby = t.CodecId.Contains("DOLBY", StringComparison.OrdinalIgnoreCase) ||
                            t.Name.Contains("Dolby", StringComparison.OrdinalIgnoreCase) ||
                            t.HdrFormat.Contains("Dolby", StringComparison.OrdinalIgnoreCase) ||
                            Regex.IsMatch(t.Name, @"(?i)(?:^|[\s._\-\[(+])(DV|DoVi|Dolby[\s._\-]?Vision)(?:$|[\s._\-\])+])");

            t.HdrFormat = hasDolby ? "Dolby Vision / HDR10" : "HDR10";
        }
        else if (t.TransferCharacteristics == 18)
        {
            t.HdrFormat = "HLG";
        }
        else if (t.ColorPrimaries == 9 && (t.BitDepth >= 10 || t.VideoBitDepth >= 10))
        {
            if (string.IsNullOrEmpty(t.HdrFormat) || t.HdrFormat == "SDR")
            {
                t.HdrFormat = "HDR10";
            }
        }
    }

    private static List<ContainerTrackInfo> ParseMp4Tracks(string path)
    {
        var list = new List<ContainerTrackInfo>();
        try
        {
            using var fs = File.OpenRead(path);
            long fileLen = fs.Length;
            if (fileLen < 16) return list;

            int bufLen = (int)Math.Min(fileLen, 4 * 1024 * 1024);
            byte[] buf = System.Buffers.ArrayPool<byte>.Shared.Rent(bufLen);
            try
            {
                int read = fs.Read(buf, 0, bufLen);
                ScanMp4Buffer(buf, read, list);

                if (list.Count == 0 && fileLen > bufLen)
                {
                    long tailPos = Math.Max(0, fileLen - bufLen);
                    fs.Seek(tailPos, SeekOrigin.Begin);
                    read = fs.Read(buf, 0, bufLen);
                    ScanMp4Buffer(buf, read, list);
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buf);
            }
        }
        catch { }
        return list;
    }

    private static void ScanMp4Buffer(byte[] buf, int read, List<ContainerTrackInfo> list)
    {
        bool foundVideo = false;
        var videoTrack = new ContainerTrackInfo { TrackType = 1 };

        // Search for 'colr' box
        for (int i = 0; i <= read - 14; i++)
        {
            if (buf[i] == 'c' && buf[i + 1] == 'o' && buf[i + 2] == 'l' && buf[i + 3] == 'r')
            {
                if (buf[i + 4] == 'n' && buf[i + 5] == 'c' && buf[i + 6] == 'l' && (buf[i + 7] == 'x' || buf[i + 7] == 'c'))
                {
                    int prim = (buf[i + 8] << 8) | buf[i + 9];
                    int tf = (buf[i + 10] << 8) | buf[i + 11];
                    int mat = (buf[i + 12] << 8) | buf[i + 13];

                    videoTrack.ColorPrimaries = prim;
                    videoTrack.TransferCharacteristics = tf;
                    videoTrack.MatrixCoefficients = mat;

                    if (tf == 16)
                    {
                        videoTrack.HdrFormat = videoTrack.HdrFormat.Contains("Dolby", StringComparison.OrdinalIgnoreCase)
                            ? "Dolby Vision / HDR10"
                            : "HDR10";
                        videoTrack.BitDepth = 10;
                        videoTrack.VideoBitDepth = 10;
                    }
                    else if (tf == 18)
                    {
                        videoTrack.HdrFormat = "HLG";
                        videoTrack.BitDepth = 10;
                        videoTrack.VideoBitDepth = 10;
                    }
                    else if (prim == 9 && tf != 1 && tf != 13)
                    {
                        if (!videoTrack.HdrFormat.Contains("Dolby", StringComparison.OrdinalIgnoreCase))
                        {
                            videoTrack.HdrFormat = "HDR10";
                        }
                        videoTrack.BitDepth = 10;
                        videoTrack.VideoBitDepth = 10;
                    }
                    foundVideo = true;
                    break;
                }
            }
        }

        // Search for Dolby Vision sample entries and configuration boxes (4-byte FourCC codes: dvh1, dvhe, dva1, dvav, dav1, dvcC, dvvC)
        for (int i = 0; i <= read - 4; i++)
        {
            if ((buf[i] == 'd' && buf[i + 1] == 'v' && buf[i + 2] == 'h' && (buf[i + 3] == '1' || buf[i + 3] == 'e')) ||
                (buf[i] == 'd' && buf[i + 1] == 'v' && buf[i + 2] == 'a' && (buf[i + 3] == '1' || buf[i + 3] == 'v')) ||
                (buf[i] == 'd' && buf[i + 1] == 'a' && buf[i + 2] == 'v' && buf[i + 3] == '1') ||
                (buf[i] == 'd' && buf[i + 1] == 'v' && buf[i + 2] == 'c' && buf[i + 3] == 'C') ||
                (buf[i] == 'd' && buf[i + 1] == 'v' && buf[i + 2] == 'v' && buf[i + 3] == 'C'))
            {
                videoTrack.CodecId = "dvh1";
                videoTrack.HdrFormat = (videoTrack.TransferCharacteristics == 16 || videoTrack.HdrFormat.Contains("HDR", StringComparison.OrdinalIgnoreCase))
                    ? "Dolby Vision / HDR10"
                    : "Dolby Vision";
                videoTrack.BitDepth = 10;
                videoTrack.VideoBitDepth = 10;
                foundVideo = true;
                break;
            }
        }

        // Search for HEVC 'hvcC'
        for (int i = 0; i <= read - 8; i++)
        {
            if (buf[i] == 'h' && buf[i + 1] == 'v' && buf[i + 2] == 'c' && buf[i + 3] == 'C')
            {
                videoTrack.CodecId = "hev1";
                foundVideo = true;
                ParseHevcCodecPrivate(buf, i + 4, read - (i + 4), videoTrack);
                break;
            }
        }

        if (foundVideo)
        {
            list.Add(videoTrack);
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

    private static void ParseHevcCodecPrivate(byte[] buf, int pos, int len, ContainerTrackInfo t)
    {
        try
        {
            if (len < 23 || pos + 23 > buf.Length) return;
            if (buf[pos] != 1) return;

            int profileIdc = buf[pos + 1] & 0x1F;
            if (profileIdc == 2 && t.BitDepth < 10)
            {
                t.BitDepth = 10;
                t.VideoBitDepth = 10;
            }

            if (len > 18)
            {
                int lumaDepth = (buf[pos + 17] & 0x07) + 8;
                if (lumaDepth >= 10 && t.BitDepth < lumaDepth)
                {
                    t.BitDepth = lumaDepth;
                    t.VideoBitDepth = lumaDepth;
                }
            }

            int numArrays = buf[pos + 22];
            int curr = pos + 23;
            for (int a = 0; a < numArrays; a++)
            {
                if (curr + 3 > pos + len || curr + 3 > buf.Length) break;
                int nalType = buf[curr] & 0x3F;
                int numNalus = (buf[curr + 1] << 8) | buf[curr + 2];
                curr += 3;

                for (int n = 0; n < numNalus; n++)
                {
                    if (curr + 2 > pos + len || curr + 2 > buf.Length) break;
                    int nalLen = (buf[curr] << 8) | buf[curr + 1];
                    curr += 2;
                    if (curr + nalLen > pos + len || curr + nalLen > buf.Length) break;

                    if (nalType == 33 && nalLen > 4) // SPS
                    {
                        ParseHevcSps(buf, curr, nalLen, t);
                    }

                    curr += nalLen;
                }
            }
        }
        catch { }
    }

    private static void ParseHevcSps(byte[] buf, int offset, int length, ContainerTrackInfo t)
    {
        try
        {
            byte[] rbsp = RemoveEmulationPrevention(buf.AsSpan(offset, length));

            try
            {
                var br = new HevcBitReader(rbsp);
                br.ReadBits(16); // nal_unit_header

                br.ReadBits(4); // sps_video_param_set_id
                uint maxSubLayersMinus1 = br.ReadBits(3);
                br.ReadBit(); // sps_temporal_id_nesting_flag

                br.ReadBits(2); // profile_space
                br.ReadBit(); // tier_flag
                uint profileIdc = br.ReadBits(5);
                br.ReadBits(32); // profile_compatibility_flags
                br.ReadBits(4); // progressive/interlaced flags
                br.ReadBits(44); // constraint flags
                br.ReadBits(8); // level_idc

                var subLayerProfilePresent = new bool[maxSubLayersMinus1];
                var subLayerLevelPresent = new bool[maxSubLayersMinus1];
                for (int i = 0; i < maxSubLayersMinus1; i++)
                {
                    subLayerProfilePresent[i] = br.ReadBit() != 0;
                    subLayerLevelPresent[i] = br.ReadBit() != 0;
                }
                if (maxSubLayersMinus1 > 0)
                {
                    for (int i = (int)maxSubLayersMinus1; i < 8; i++) br.ReadBits(2);
                    for (int i = 0; i < maxSubLayersMinus1; i++)
                    {
                        if (subLayerProfilePresent[i]) br.ReadBits(2 + 1 + 5 + 32 + 4 + 44);
                        if (subLayerLevelPresent[i]) br.ReadBits(8);
                    }
                }

                br.ReadUe(); // sps_seq_parameter_set_id
                uint chromaFormatIdc = br.ReadUe();
                if (chromaFormatIdc == 3) br.ReadBit();

                br.ReadUe(); // pic_width
                br.ReadUe(); // pic_height
                if (br.ReadBit() != 0) // conf_win
                {
                    br.ReadUe(); br.ReadUe(); br.ReadUe(); br.ReadUe();
                }

                uint bitDepthLumaMinus8 = br.ReadUe();
                uint bitDepthChromaMinus8 = br.ReadUe();
                int depth = (int)bitDepthLumaMinus8 + 8;
                if (depth >= 10)
                {
                    t.BitDepth = depth;
                    t.VideoBitDepth = depth;
                }

                br.ReadUe(); // log2_max_pic_order_cnt_lsb_minus4
                bool subLayerOrdering = br.ReadBit() != 0;
                int startSub = subLayerOrdering ? 0 : (int)maxSubLayersMinus1;
                for (int i = startSub; i <= maxSubLayersMinus1; i++)
                {
                    br.ReadUe(); br.ReadUe(); br.ReadUe();
                }

                br.ReadUe(); br.ReadUe(); br.ReadUe(); br.ReadUe(); br.ReadUe(); br.ReadUe();

                if (br.ReadBit() != 0) // scaling_list
                {
                    if (br.ReadBit() != 0)
                    {
                        for (int sizeId = 0; sizeId < 4; sizeId++)
                        {
                            for (int matrixId = 0; matrixId < (sizeId != 3 ? 6 : 2); matrixId++)
                            {
                                if (br.ReadBit() == 0) br.ReadUe();
                                else
                                {
                                    int coefNum = Math.Min(64, 1 << (4 + (sizeId << 1)));
                                    if (sizeId > 1) br.ReadSe();
                                    for (int k = 0; k < coefNum; k++) br.ReadSe();
                                }
                            }
                        }
                    }
                }

                br.ReadBit(); // amp
                br.ReadBit(); // sao
                if (br.ReadBit() != 0) // pcm
                {
                    br.ReadBits(8); br.ReadUe(); br.ReadUe(); br.ReadBit();
                }

                uint numStRef = br.ReadUe();
                for (int i = 0; i < numStRef; i++)
                {
                    if (i != 0 && br.ReadBit() != 0)
                    {
                        br.ReadUe(); br.ReadBit(); br.ReadUe();
                    }
                    else
                    {
                        uint numNeg = br.ReadUe();
                        uint numPos = br.ReadUe();
                        for (int j = 0; j < numNeg; j++) { br.ReadUe(); br.ReadBit(); }
                        for (int j = 0; j < numPos; j++) { br.ReadUe(); br.ReadBit(); }
                    }
                }

                if (br.ReadBit() != 0) // lt_ref
                {
                    uint numLt = br.ReadUe();
                    for (int i = 0; i < numLt; i++) { br.ReadBits((int)(br.ReadUe() + 4)); br.ReadBit(); }
                }

                br.ReadBit(); // temporal_mvp
                br.ReadBit(); // strong_intra

                if (br.ReadBit() != 0) // vui_parameters_present
                {
                    if (br.ReadBit() != 0) // aspect_ratio
                    {
                        uint aspectIdc = br.ReadBits(8);
                        if (aspectIdc == 255) { br.ReadBits(16); br.ReadBits(16); }
                    }
                    if (br.ReadBit() != 0) br.ReadBit(); // overscan
                    if (br.ReadBit() != 0) // video_signal_type
                    {
                        br.ReadBits(3); // video_format
                        br.ReadBit(); // full_range
                        if (br.ReadBit() != 0) // colour_description
                        {
                            uint primaries = br.ReadBits(8);
                            uint transfer = br.ReadBits(8);
                            uint matrix = br.ReadBits(8);

                            ApplyHdrColorMetadata(t, (int)primaries, (int)transfer, (int)matrix);
                            return;
                        }
                    }
                }
            }
            catch { }

            // Resilient fallback pattern scan in RBSP
            for (int i = 0; i <= rbsp.Length - 3; i++)
            {
                if (rbsp[i] == 9 && rbsp[i + 1] == 16 && rbsp[i + 2] == 9)
                {
                    ApplyHdrColorMetadata(t, 9, 16, 9);
                    return;
                }
                if (rbsp[i] == 9 && rbsp[i + 1] == 18 && rbsp[i + 2] == 9)
                {
                    ApplyHdrColorMetadata(t, 9, 18, 9);
                    return;
                }
            }
        }
        catch { }
    }

    private static void ApplyHdrColorMetadata(ContainerTrackInfo t, int primaries, int transfer, int matrix)
    {
        t.ColorPrimaries = primaries;
        t.TransferCharacteristics = transfer;
        t.MatrixCoefficients = matrix;

        if (transfer == 16)
        {
            bool hasDolby = t.CodecId.Contains("DOLBY", StringComparison.OrdinalIgnoreCase) ||
                            t.Name.Contains("Dolby", StringComparison.OrdinalIgnoreCase) ||
                            t.HdrFormat.Contains("Dolby", StringComparison.OrdinalIgnoreCase) ||
                            Regex.IsMatch(t.Name, @"(?i)(?:^|[\s._\-\[(+])(DV|DoVi|Dolby[\s._\-]?Vision)(?:$|[\s._\-\])+])");

            t.HdrFormat = hasDolby ? "Dolby Vision / HDR10" : "HDR10";
            if (t.BitDepth < 10) t.BitDepth = 10;
            if (t.VideoBitDepth < 10) t.VideoBitDepth = 10;
        }
        else if (transfer == 18)
        {
            t.HdrFormat = "HLG";
            if (t.BitDepth < 10) t.BitDepth = 10;
            if (t.VideoBitDepth < 10) t.VideoBitDepth = 10;
        }
        else if (primaries == 9 && (t.BitDepth >= 10 || t.VideoBitDepth >= 10))
        {
            if (string.IsNullOrEmpty(t.HdrFormat) || t.HdrFormat == "SDR")
            {
                t.HdrFormat = "HDR10";
            }
        }
        else if (transfer == 1 && primaries == 1)
        {
            if (string.IsNullOrEmpty(t.HdrFormat))
            {
                t.HdrFormat = "SDR";
            }
        }
    }

    private static byte[] RemoveEmulationPrevention(ReadOnlySpan<byte> src)
    {
        byte[] dst = new byte[src.Length];
        int d = 0;
        for (int i = 0; i < src.Length; i++)
        {
            if (i + 2 < src.Length && src[i] == 0 && src[i + 1] == 0 && src[i + 2] == 3)
            {
                dst[d++] = 0;
                dst[d++] = 0;
                i += 2;
            }
            else
            {
                dst[d++] = src[i];
            }
        }
        if (d == src.Length) return dst;
        byte[] trimmed = new byte[d];
        Buffer.BlockCopy(dst, 0, trimmed, 0, d);
        return trimmed;
    }

    private ref struct HevcBitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bytePos;
        private int _bitPos;

        public HevcBitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bytePos = 0;
            _bitPos = 0;
        }

        public int ReadBit()
        {
            if (_bytePos >= _data.Length) return 0;
            int bit = (_data[_bytePos] >> (7 - _bitPos)) & 1;
            _bitPos++;
            if (_bitPos == 8)
            {
                _bitPos = 0;
                _bytePos++;
            }
            return bit;
        }

        public uint ReadBits(int n)
        {
            uint val = 0;
            for (int i = 0; i < n; i++)
            {
                val = (val << 1) | (uint)ReadBit();
            }
            return val;
        }

        public uint ReadUe()
        {
            int zeros = 0;
            while (ReadBit() == 0 && zeros < 32)
            {
                zeros++;
            }
            if (zeros == 0) return 0;
            uint val = ReadBits(zeros);
            return ((1u << zeros) - 1u) + val;
        }

        public int ReadSe()
        {
            uint code = ReadUe();
            return (code & 1) != 0 ? (int)((code + 1) / 2) : -(int)(code / 2);
        }
    }
}
