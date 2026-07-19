using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NVorbis;
using Concentus.Oggfile;
using Concentus.Structs;

namespace LumiereMediaPlayer.Services;

public static class AudioPipelineHelper
{
    private static readonly Dictionary<string, string> _transcodedCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _cacheLock = new();

    static AudioPipelineHelper()
    {
        try
        {
            var tempFolder = Windows.Storage.ApplicationData.Current.TemporaryFolder.Path;
            if (Directory.Exists(tempFolder))
            {
                foreach (var file in Directory.GetFiles(tempFolder, "transcoded_*"))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public static async Task<string?> GetPlayableFileAsync(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return null;

        // If it's a web URL, return as-is
        if (Uri.TryCreate(sourcePath, UriKind.Absolute, out var uri) && 
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return sourcePath;
        }

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext != ".ogg" && ext != ".opus")
        {
            return sourcePath;
        }

        try
        {
            if (!File.Exists(sourcePath))
            {
                return sourcePath;
            }

            // Generate a unique cache key based on file path and last write time
            var lastWrite = File.GetLastWriteTimeUtc(sourcePath).Ticks;
            var key = $"{sourcePath}_{lastWrite}";

            lock (_cacheLock)
            {
                if (_transcodedCache.Count > 10) { _transcodedCache.Clear(); }
                if (_transcodedCache.TryGetValue(key, out var cachedPath) && File.Exists(cachedPath))
                {
                    return cachedPath;
                }
            }

            // Create a unique temporary file path in the application's temporary folder
            var tempFolder = Windows.Storage.ApplicationData.Current.TemporaryFolder.Path;
            var safeName = GetSafeFilename(sourcePath);
            var tempPath = Path.Combine(tempFolder, $"transcoded_{Guid.NewGuid():N}_{safeName}.wav");

            bool success = false;
            await Task.Run(() =>
            {
                if (ext == ".opus")
                {
                    success = DecodeOggOpus(sourcePath, tempPath);
                }
                else // .ogg
                {
                    // Try Opus first, then Vorbis as fallback
                    success = DecodeOggOpus(sourcePath, tempPath);
                    if (!success)
                    {
                        success = DecodeOggVorbis(sourcePath, tempPath);
                    }
                }
            });

            if (success && File.Exists(tempPath))
            {
                lock (_cacheLock)
                {
                    _transcodedCache[key] = tempPath;
                }
                return tempPath;
            }
            else
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to transcode: {ex.Message}");
        }

        return sourcePath; // Fallback to original path if transcoding fails
    }

    private static string GetSafeFilename(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private static bool DecodeOggVorbis(string inputPath, string outputPath)
    {
        try
        {
            using var vorbis = new VorbisReader(inputPath);
            int sampleRate = vorbis.SampleRate;
            int channels = vorbis.Channels;

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(fs);

            // Write dummy WAV header first (44 bytes)
            byte[] dummyHeader = new byte[44];
            writer.Write(dummyHeader);

            float[] readBuffer = new float[channels * sampleRate]; // 1 second buffer
            int samplesRead;
            int totalSamples = 0;

            while ((samplesRead = vorbis.ReadSamples(readBuffer, 0, readBuffer.Length)) > 0)
            {
                for (int i = 0; i < samplesRead; i++)
                {
                    float sample = readBuffer[i];
                    if (sample > 1.0f) sample = 1.0f;
                    else if (sample < -1.0f) sample = -1.0f;

                    short shortSample = (short)(sample * short.MaxValue);
                    writer.Write(shortSample);
                }
                totalSamples += samplesRead;
            }

            // Go back and write the real WAV header
            fs.Position = 0;
            WriteWavHeader(fs, (ushort)channels, 16, sampleRate, totalSamples);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ogg Vorbis decode error: {ex.Message}");
            return false;
        }
    }

    private static bool DecodeOggOpus(string inputPath, string outputPath)
    {
        try
        {
            using var fileIn = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Standard Opus sample rate is 48000 Hz. Decode to stereo.
            int sampleRate = 48000;
            int channels = 2;
#pragma warning disable CS0618
            var decoder = new OpusDecoder(sampleRate, channels);
#pragma warning restore CS0618

            // Initialize Ogg stream reader
            var oggIn = new OpusOggReadStream(decoder, fileIn);

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(fs);

            // Write dummy WAV header first (44 bytes)
            byte[] dummyHeader = new byte[44];
            writer.Write(dummyHeader);

            int totalSamples = 0;
            while (oggIn.HasNextPacket)
            {
                short[] packet = oggIn.DecodeNextPacket();
                if (packet != null && packet.Length > 0)
                {
                    for (int i = 0; i < packet.Length; i++)
                    {
                        writer.Write(packet[i]);
                    }
                    totalSamples += packet.Length;
                }
            }

            if (totalSamples == 0)
            {
                return false;
            }

            // Go back and write the real WAV header
            fs.Position = 0;
            WriteWavHeader(fs, (ushort)channels, 16, sampleRate, totalSamples);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ogg Opus decode error: {ex.Message}");
            return false;
        }
    }

    private static void WriteWavHeader(Stream stream, ushort channels, ushort bitsPerSample, int sampleRate, int totalSamples)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        bool useExtensible = channels > 2;
        int pcmChunkSize = useExtensible ? 40 : 16;
        int dataLength = totalSamples * (bitsPerSample / 8);
        int fileLength = 4 + (8 + pcmChunkSize) + (8 + dataLength);

        // RIFF chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(fileLength);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // "fmt " sub-chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(pcmChunkSize);
        
        ushort formatTag = useExtensible ? (ushort)0xFFFE : (ushort)1;
        writer.Write(formatTag);
        writer.Write(channels);
        writer.Write(sampleRate);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        writer.Write(byteRate);
        ushort blockAlign = (ushort)(channels * (bitsPerSample / 8));
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        if (useExtensible)
        {
            writer.Write((ushort)22); // cbSize
            writer.Write(bitsPerSample); // ValidBitsPerSample
            writer.Write((uint)0); // ChannelMask (0 = default mapping)
            // SubFormat GUID: KSDATAFORMAT_SUBTYPE_PCM
            writer.Write((uint)0x00000001);
            writer.Write((ushort)0x0000);
            writer.Write((ushort)0x0010);
            writer.Write(new byte[] { 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71 });
        }

        // "data" sub-chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
    }
}
