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
        public void CleanTitleForComparison_StripsMultiDigitEpisodesAndAlternativeFormats()
        {
            Assert.AreEqual("severance", LocalMediaMatcher.CleanTitleForComparison("Severance.S01E12.1080p.mkv"));
            Assert.AreEqual("ted lasso rainbow", LocalMediaMatcher.CleanTitleForComparison("Ted Lasso - 2x15 - Rainbow.mkv"));
            Assert.IsTrue(LocalMediaMatcher.IsTitleMatch("Ted Lasso", "Ted Lasso - 2x15 - Rainbow.mkv"));
            Assert.AreEqual("breaking bad", LocalMediaMatcher.CleanTitleForComparison("Breaking Bad Season 4 Episode 13"));
        }

        [TestMethod]
        public void IsTitleMatch_MatchesWithAndWithoutArticles()
        {
            Assert.IsTrue(LocalMediaMatcher.IsTitleMatch("The Dark Knight", "Dark Knight"));
            Assert.IsTrue(LocalMediaMatcher.IsTitleMatch("Dark Knight", "The Dark Knight (2008)"));
            Assert.IsTrue(LocalMediaMatcher.IsTitleMatch("A Beautiful Mind", "Beautiful Mind"));
        }

        [TestMethod]
        public void MatchesFileOrAncestors_MatchesDeeplyNestedFolderStructures()
        {
            string path1 = @"D:\Media\TV Shows\Severance\Season 1\S01E01.mkv";
            Assert.IsTrue(LocalMediaMatcher.MatchesFileOrAncestors("Severance", path1));

            string path2 = @"D:\Media\Movies\4K\Inception (2010)\Inception.2010.mkv";
            Assert.IsTrue(LocalMediaMatcher.MatchesFileOrAncestors("Inception", path2));
        }

        [TestMethod]
        public void VideoMetadataHelper_CleanTitleAndExtractYear_AccuratelyExtractsYearAndTitle()
        {
            var (title1, year1, isTv1) = VideoMetadataHelper.CleanTitleAndExtractYear("Dune.Part.Two.2024.1080p.mkv", null);
            Assert.AreEqual("Dune Part Two", title1);
            Assert.AreEqual(2024, year1);
            Assert.IsFalse(isTv1);

            var (title2, year2, isTv2) = VideoMetadataHelper.CleanTitleAndExtractYear("1917.mkv", null);
            Assert.AreEqual("1917", title2);
            Assert.IsFalse(isTv2);

            var (title3, year3, isTv3) = VideoMetadataHelper.CleanTitleAndExtractYear("Blade Runner 2049.mkv", null);
            Assert.AreEqual("Blade Runner 2049", title3);
            Assert.IsFalse(isTv3);

            var (title4, year4, isTv4) = VideoMetadataHelper.CleanTitleAndExtractYear("S01E05.mkv", @"D:\TV\Severance\Season 1\S01E05.mkv");
            Assert.AreEqual("Severance", title4);
            Assert.IsTrue(isTv4);

            var (title5, year5, isTv5) = VideoMetadataHelper.CleanTitleAndExtractYear("Inception (2010) [1080p] [BluRay] [YTS.MX].mp4", null);
            Assert.AreEqual("Inception", title5);
            Assert.AreEqual(2010, year5);
            Assert.IsFalse(isTv5);

            var (title6, year6, isTv6) = VideoMetadataHelper.CleanTitleAndExtractYear("Oppenheimer.2023.HDR.2160p.UHD.Remux.mkv", null);
            Assert.AreEqual("Oppenheimer", title6);
            Assert.AreEqual(2023, year6);
            Assert.IsFalse(isTv6);
        }

        [TestMethod]
        public void VideoMetadataHelper_PickBestTmdbMatch_SelectsClosestYearAndTitle()
        {
            var results = new List<LumiereMediaPlayer.Models.Streaming.TmdbMedia>
            {
                new() { Id = 101, Title = "Dune", ReleaseDate = "1984-12-14", VoteAverage = 6.4 },
                new() { Id = 102, Title = "Dune: Part Two", ReleaseDate = "2024-02-27", VoteAverage = 8.5 },
                new() { Id = 103, Title = "Dune", ReleaseDate = "2021-09-15", VoteAverage = 8.0 }
            };

            var best = VideoMetadataHelper.PickBestTmdbMatch(results, "Dune Part Two", 2024);
            Assert.IsNotNull(best);
            Assert.AreEqual(102, best.Id);
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
