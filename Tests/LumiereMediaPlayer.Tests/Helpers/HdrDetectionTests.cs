using System;
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
    }
}
