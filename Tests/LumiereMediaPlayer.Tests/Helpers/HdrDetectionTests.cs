using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Tests.Helpers
{
    [TestClass]
    public class HdrDetectionTests
    {
        private string _tempDir = "";

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "Lumiere_HdrDetectionTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }

        [TestMethod]
        [DataRow("David.mp4")]
        [DataRow("DVDrip.x264.mp4")]
        [DataRow("Devil.Wears.Prada.mp4")]
        [DataRow("Adventure.Time.S01E01.mp4")]
        [DataRow("Endeavour.S01E01.mkv")]
        [DataRow("Anime_10bit_SDR.mkv")]
        [DataRow("Standard_Movie_1080p.mp4")]
        public async Task ScanMetadata_DoesNotDetectHdr_ForFalsePositiveFilenames(string fileName)
        {
            string filePath = Path.Combine(_tempDir, fileName);
            await File.WriteAllBytesAsync(filePath, new byte[128]);

            var item = new MediaItem
            {
                Id = Guid.NewGuid().ToString(),
                SourcePath = filePath,
                Title = Path.GetFileNameWithoutExtension(fileName)
            };

            await MediaMetadataScanner.ScanMetadataAsync(item);

            Assert.IsTrue(string.IsNullOrEmpty(item.HdrFormat) || item.HdrFormat == "SDR",
                $"File '{fileName}' was falsely identified as HDR: '{item.HdrFormat}'");
        }

        [TestMethod]
        [DataRow("Movie.2024.HDR.2160p.mkv", "HDR10")]
        [DataRow("Movie.2024.HDR10+.2160p.mkv", "HDR10+")]
        [DataRow("Movie.2024.HLG.2160p.mkv", "HLG")]
        [DataRow("Movie.2024.DV.2160p.mkv", "Dolby Vision")]
        [DataRow("Movie.2024.DV.HDR.2160p.mkv", "Dolby Vision / HDR10")]
        [DataRow("Movie.2024.DoVi.2160p.mkv", "Dolby Vision")]
        [DataRow("Movie.2024.Dolby.Vision.2160p.mkv", "Dolby Vision")]
        public async Task ScanMetadata_DetectsHdr_ForValidHdrFilenames(string fileName, string expectedFormat)
        {
            string filePath = Path.Combine(_tempDir, fileName);
            await File.WriteAllBytesAsync(filePath, new byte[128]);

            var item = new MediaItem
            {
                Id = Guid.NewGuid().ToString(),
                SourcePath = filePath,
                Title = Path.GetFileNameWithoutExtension(fileName)
            };

            await MediaMetadataScanner.ScanMetadataAsync(item);
            Assert.AreEqual(expectedFormat, item.HdrFormat);
        }

        [TestMethod]
        public void GetContainerTracks_Mp4ColrHdr10_IdentifiesHdr10()
        {
            string filePath = Path.Combine(_tempDir, "sample_hdr10.mp4");
            byte[] bytes = new byte[128];
            int offset = 16;
            bytes[offset] = (byte)'c';
            bytes[offset + 1] = (byte)'o';
            bytes[offset + 2] = (byte)'l';
            bytes[offset + 3] = (byte)'r';
            bytes[offset + 4] = (byte)'n';
            bytes[offset + 5] = (byte)'c';
            bytes[offset + 6] = (byte)'l';
            bytes[offset + 7] = (byte)'x';
            bytes[offset + 8] = 0; bytes[offset + 9] = 9;
            bytes[offset + 10] = 0; bytes[offset + 11] = 16;
            bytes[offset + 12] = 0; bytes[offset + 13] = 9;
            File.WriteAllBytes(filePath, bytes);

            var tracks = MediaTrackFormatHelper.GetContainerTracks(filePath);
            var vTrack = tracks.Find(t => t.TrackType == 1);
            Assert.IsNotNull(vTrack);
            Assert.AreEqual("HDR10", vTrack.HdrFormat);
        }

        [TestMethod]
        public void GetContainerTracks_Mp4DolbyVisionProfile78_IdentifiesDolbyVisionAndHdr10()
        {
            string filePath = Path.Combine(_tempDir, "sample_dv_hdr10.mp4");
            byte[] bytes = new byte[128];
            int offset = 16;
            bytes[offset] = (byte)'c';
            bytes[offset + 1] = (byte)'o';
            bytes[offset + 2] = (byte)'l';
            bytes[offset + 3] = (byte)'r';
            bytes[offset + 4] = (byte)'n';
            bytes[offset + 5] = (byte)'c';
            bytes[offset + 6] = (byte)'l';
            bytes[offset + 7] = (byte)'x';
            bytes[offset + 8] = 0; bytes[offset + 9] = 9;
            bytes[offset + 10] = 0; bytes[offset + 11] = 16;
            bytes[offset + 12] = 0; bytes[offset + 13] = 9;

            bytes[48] = (byte)'d';
            bytes[49] = (byte)'v';
            bytes[50] = (byte)'c';
            bytes[51] = (byte)'C';
            File.WriteAllBytes(filePath, bytes);

            var tracks = MediaTrackFormatHelper.GetContainerTracks(filePath);
            var vTrack = tracks.Find(t => t.TrackType == 1);
            Assert.IsNotNull(vTrack);
            Assert.AreEqual("Dolby Vision / HDR10", vTrack.HdrFormat);
        }

        [TestMethod]
        public void GetContainerTracks_Mp4DolbyVisionOnly_IdentifiesDolbyVision()
        {
            string filePath = Path.Combine(_tempDir, "sample_dv_only.mp4");
            byte[] bytes = new byte[128];
            bytes[32] = (byte)'d';
            bytes[33] = (byte)'v';
            bytes[34] = (byte)'h';
            bytes[35] = (byte)'1';
            File.WriteAllBytes(filePath, bytes);

            var tracks = MediaTrackFormatHelper.GetContainerTracks(filePath);
            var vTrack = tracks.Find(t => t.TrackType == 1);
            Assert.IsNotNull(vTrack);
            Assert.AreEqual("Dolby Vision", vTrack.HdrFormat);
        }

        [TestMethod]
        public void ParseHevcCodecPrivate_WithBt2020AndPq_IdentifiesHdr10()
        {
            string filePath = Path.Combine(_tempDir, "synthetic_hevc_hdr10.mkv");
            byte[] spsBytes = Convert.FromHexString("420101022000000090000000000099a001e020021c4d96566924caf016a1220120800001f480002ee004");
            
            var hvcC = new List<byte> { 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 0, 0, 0, 1, 33, 0, 1, 0, (byte)spsBytes.Length };
            hvcC.AddRange(spsBytes);

            var trackEntry = new List<byte>
            {
                0x83, 0x81, 0x01,
                0x86, 0x90
            };
            trackEntry.AddRange(System.Text.Encoding.ASCII.GetBytes("V_MPEGH/ISO/HEVC"));
            trackEntry.Add(0x63); trackEntry.Add(0xA2);
            trackEntry.Add((byte)(0x80 | hvcC.Count));
            trackEntry.AddRange(hvcC);

            var mkv = new List<byte>
            {
                0x16, 0x54, 0xAE, 0x6B,
                0x40, (byte)(trackEntry.Count + 2),
                0xAE,
                (byte)(0x80 | trackEntry.Count)
            };
            mkv.AddRange(trackEntry);

            File.WriteAllBytes(filePath, mkv.ToArray());

            var tracks = MediaTrackFormatHelper.GetContainerTracks(filePath);
            var vTrack = tracks.Find(t => t.TrackType == 1);
            Assert.IsNotNull(vTrack);
            Assert.AreEqual("HDR10", vTrack.HdrFormat);
            Assert.AreEqual(16, vTrack.TransferCharacteristics);
            Assert.AreEqual(9, vTrack.ColorPrimaries);
            Assert.AreEqual(10, vTrack.BitDepth);
        }
    }
}
