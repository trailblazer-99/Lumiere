using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Tests.Helpers
{
    [TestClass]
    public class LocalMediaMatcherTests
    {
        [TestMethod]
        public void CleanTitleForComparison_RemovesPunctuationAndQualityTokens()
        {
            string raw = "Dune.Part.Two.2024.2160p.UHD.BluRay.x265";
            string cleaned = LocalMediaMatcher.CleanTitleForComparison(raw);

            Assert.AreEqual("dune part two", cleaned);
        }

        [TestMethod]
        public void CleanTitleForComparison_HandlesAmpersandAndApostrophes()
        {
            string raw = "Lock, Stock & Two Smoking Barrels (1998)";
            string cleaned = LocalMediaMatcher.CleanTitleForComparison(raw);

            Assert.AreEqual("lock stock and two smoking barrels", cleaned);
        }

        [TestMethod]
        public void CleanTitleForComparison_StripsSeasonEpisodeTokens()
        {
            string raw = "Severance.S01E01.Good.News.About.Hell.1080p.webrip";
            string cleaned = LocalMediaMatcher.CleanTitleForComparison(raw);

            Assert.AreEqual("severance good news about hell", cleaned);
        }

        [TestMethod]
        public void IsTitleMatch_MatchesEquivalentTitles()
        {
            Assert.IsTrue(LocalMediaMatcher.IsTitleMatch("Inception", "Inception (2010) [1080p]"));
            Assert.IsTrue(LocalMediaMatcher.IsTitleMatch("Ted Lasso", "Ted Lasso S03E01"));
            Assert.IsTrue(LocalMediaMatcher.IsTitleMatch("Interstellar", "interstellar"));
        }

        [TestMethod]
        public void IsTitleMatch_RejectsDifferentTitles()
        {
            Assert.IsFalse(LocalMediaMatcher.IsTitleMatch("The Batman", "Spider-Man"));
            Assert.IsFalse(LocalMediaMatcher.IsTitleMatch("Avatar", "The Matrix"));
            Assert.IsFalse(LocalMediaMatcher.IsTitleMatch(null, "Inception"));
            Assert.IsFalse(LocalMediaMatcher.IsTitleMatch("", ""));
        }

        [TestMethod]
        public async Task FindMatchingMediaAsync_FindsInMemoryMatch()
        {
            var knownItems = new List<MediaItem>
            {
                new() { Id = "1", Title = "Severance S01", SourcePath = @"C:\Videos\Severance.S01E01.1080p.mkv" },
                new() { Id = "2", Title = "The Matrix", SourcePath = @"C:\Videos\The.Matrix.1999.mkv" }
            };

            var match = await LocalMediaMatcher.FindMatchingMediaAsync("Severance", knownItems, new List<string>());

            Assert.IsNotNull(match);
            Assert.AreEqual("1", match.Id);
        }
    }
}
