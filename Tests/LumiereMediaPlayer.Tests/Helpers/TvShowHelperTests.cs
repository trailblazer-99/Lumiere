using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;
using LumiereMediaPlayer.ViewModels;
using Moq;

namespace LumiereMediaPlayer.Tests.Helpers;

[TestClass]
public class TvShowHelperTests
{
    [TestMethod]
    public void ExtractTvEpisodeInfo_AKnightOfTheSevenKingdoms_StandardFormat()
    {
        var item = new MediaItem
        {
            Id = "ep1",
            Title = "A Knight of the Seven Kingdoms - S01E01 - The Hedge Knight",
            SourcePath = @"C:\Videos\A Knight of the Seven Kingdoms - S01E01 - The Hedge Knight.mkv",
            Kind = MediaKind.Video
        };

        var info = TvShowHelper.ExtractTvEpisodeInfo(item);

        Assert.IsNotNull(info);
        Assert.AreEqual("A Knight of the Seven Kingdoms", info.SeriesTitle);
        Assert.AreEqual(1, info.SeasonNumber);
        Assert.AreEqual(1, info.EpisodeNumber);
        Assert.AreEqual("The Hedge Knight", info.EpisodeTitle);
    }

    [TestMethod]
    public void ExtractTvEpisodeInfo_DottedWithTags()
    {
        var item = new MediaItem
        {
            Id = "ep2",
            Title = "A.Knight.of.the.Seven.Kingdoms.S01E02.1080p.WEBRip.x265",
            SourcePath = @"C:\Videos\A.Knight.of.the.Seven.Kingdoms.S01E02.1080p.WEBRip.x265.mkv",
            Kind = MediaKind.Video
        };

        var info = TvShowHelper.ExtractTvEpisodeInfo(item);

        Assert.IsNotNull(info);
        Assert.AreEqual("A Knight of the Seven Kingdoms", info.SeriesTitle);
        Assert.AreEqual(1, info.SeasonNumber);
        Assert.AreEqual(2, info.EpisodeNumber);
        Assert.AreEqual("Episode 2", info.EpisodeTitle);
    }

    [TestMethod]
    public void ExtractTvEpisodeInfo_NestedSeasonFolder()
    {
        var item = new MediaItem
        {
            Id = "ep3",
            Title = "Episode 03",
            SourcePath = @"D:\Media\TV\A Knight of the Seven Kingdoms\Season 1\Episode 03 - Trial of Seven.mp4",
            Kind = MediaKind.Video
        };

        var info = TvShowHelper.ExtractTvEpisodeInfo(item);

        Assert.IsNotNull(info);
        Assert.AreEqual("A Knight of the Seven Kingdoms", info.SeriesTitle);
        Assert.AreEqual(1, info.SeasonNumber);
        Assert.AreEqual(3, info.EpisodeNumber);
        Assert.AreEqual("Trial of Seven", info.EpisodeTitle);
    }

    [TestMethod]
    public void ExtractTvEpisodeInfo_RegularMovie_ReturnsNull()
    {
        var movie = new MediaItem
        {
            Id = "movie1",
            Title = "Inception (2010)",
            SourcePath = @"C:\Movies\Inception (2010) [1080p].mp4",
            Kind = MediaKind.Video
        };

        var info = TvShowHelper.ExtractTvEpisodeInfo(movie);

        Assert.IsNull(info);
    }

    [TestMethod]
    public void ExtractTvEpisodeInfo_NumericMovieTitles_ReturnsNull()
    {
        var movie1917 = new MediaItem
        {
            Id = "m1",
            Title = "1917",
            SourcePath = @"C:\Movies\1917 (2019) 1080p Bluray.mkv",
            Kind = MediaKind.Video
        };
        Assert.IsNull(TvShowHelper.ExtractTvEpisodeInfo(movie1917));

        var movie300 = new MediaItem
        {
            Id = "m2",
            Title = "300",
            SourcePath = @"C:\Movies\300.2006.1080p.mkv",
            Kind = MediaKind.Video
        };
        Assert.IsNull(TvShowHelper.ExtractTvEpisodeInfo(movie300));

        var movie2001 = new MediaItem
        {
            Id = "m3",
            Title = "2001 A Space Odyssey",
            SourcePath = @"C:\Movies\2001.A.Space.Odyssey.1968.mkv",
            Kind = MediaKind.Video
        };
        Assert.IsNull(TvShowHelper.ExtractTvEpisodeInfo(movie2001));
    }

    [TestMethod]
    public void ExtractTvEpisodeInfo_ScreenRecordingsAndClips_ReturnsNull()
    {
        var recording = new MediaItem
        {
            Id = "clip1",
            Title = "Screen Recording 2026-10-15 14-30",
            SourcePath = @"C:\Users\User\Videos\Screen Recording 2026-10-15 14-30-22.mp4",
            Kind = MediaKind.Video
        };
        Assert.IsNull(TvShowHelper.ExtractTvEpisodeInfo(recording));

        var phoneClip = new MediaItem
        {
            Id = "clip2",
            Title = "VID_20261015_120401",
            SourcePath = @"C:\Users\User\Videos\VID_20261015_120401.mp4",
            Kind = MediaKind.Video
        };
        Assert.IsNull(TvShowHelper.ExtractTvEpisodeInfo(phoneClip));

        var zoom = new MediaItem
        {
            Id = "clip3",
            Title = "Zoom Meeting Recording",
            SourcePath = @"C:\Users\User\Documents\Zoom\Zoom_0.mp4",
            Kind = MediaKind.Video
        };
        Assert.IsNull(TvShowHelper.ExtractTvEpisodeInfo(zoom));
    }

    [TestMethod]
    public void ConsolidateVideoLibrary_ConsolidatesTvEpisodesUnderOneTitleCard()
    {
        var videos = new List<MediaItem>
        {
            new MediaItem
            {
                Id = "ep-1",
                Title = "A Knight of the Seven Kingdoms - S01E01 - The Hedge Knight",
                SourcePath = @"C:\Videos\A Knight of the Seven Kingdoms - S01E01 - The Hedge Knight.mkv",
                Kind = MediaKind.Video,
                Duration = TimeSpan.FromMinutes(55),
                ReleaseYear = "2025"
            },
            new MediaItem
            {
                Id = "ep-3",
                Title = "A Knight of the Seven Kingdoms - S01E03 - Trial of the Seven",
                SourcePath = @"C:\Videos\A Knight of the Seven Kingdoms - S01E03 - Trial of the Seven.mkv",
                Kind = MediaKind.Video,
                Duration = TimeSpan.FromMinutes(58),
                ReleaseYear = "2025"
            },
            new MediaItem
            {
                Id = "ep-2",
                Title = "A Knight of the Seven Kingdoms - S01E02 - The Tourney at Ashford",
                SourcePath = @"C:\Videos\A Knight of the Seven Kingdoms - S01E02 - The Tourney at Ashford.mkv",
                Kind = MediaKind.Video,
                Duration = TimeSpan.FromMinutes(52),
                ReleaseYear = "2025"
            },
            new MediaItem
            {
                Id = "movie-1",
                Title = "The Dark Knight",
                SourcePath = @"C:\Movies\The Dark Knight (2008).mkv",
                Kind = MediaKind.Video,
                Duration = TimeSpan.FromMinutes(152),
                ReleaseYear = "2008"
            }
        };

        var consolidated = TvShowHelper.ConsolidateVideoLibrary(videos);

        // Should have 2 cards: 1 consolidated series card and 1 movie card
        Assert.AreEqual(2, consolidated.Count);

        var seriesCard = consolidated.FirstOrDefault(x => x.IsSeries);
        Assert.IsNotNull(seriesCard);
        Assert.AreEqual("A Knight of the Seven Kingdoms", seriesCard.Title);
        Assert.IsNotNull(seriesCard.Episodes);
        Assert.AreEqual(3, seriesCard.Episodes.Count);

        // Episodes should be strictly sorted in order: E1, E2, E3
        Assert.AreEqual(1, seriesCard.Episodes[0].EpisodeNumber);
        Assert.AreEqual(2, seriesCard.Episodes[1].EpisodeNumber);
        Assert.AreEqual(3, seriesCard.Episodes[2].EpisodeNumber);

        Assert.AreEqual("The Hedge Knight", seriesCard.Episodes[0].EpisodeTitle);
        Assert.AreEqual("The Tourney at Ashford", seriesCard.Episodes[1].EpisodeTitle);
        Assert.AreEqual("Trial of the Seven", seriesCard.Episodes[2].EpisodeTitle);

        Assert.AreEqual("3 Episodes", seriesCard.ReleaseYear);

        // First episode path should be used as series SourcePath for preview playback
        Assert.AreEqual(@"C:\Videos\A Knight of the Seven Kingdoms - S01E01 - The Hedge Knight.mkv", seriesCard.SourcePath);

        // Movie remains untouched
        var movieCard = consolidated.FirstOrDefault(x => !x.IsSeries);
        Assert.IsNotNull(movieCard);
        Assert.AreEqual("The Dark Knight", movieCard.Title);
    }

    [TestMethod]
    public void ConsolidateVideoLibrary_MultipleSeasons_SortedAndFormattedCorrectly()
    {
        var videos = new List<MediaItem>
        {
            new MediaItem
            {
                Id = "s2e1",
                Title = "A Knight of the Seven Kingdoms - S02E01 - The Mystery Knight",
                SourcePath = @"C:\Videos\A Knight of the Seven Kingdoms - S02E01 - The Mystery Knight.mkv",
                Kind = MediaKind.Video
            },
            new MediaItem
            {
                Id = "s1e1",
                Title = "A Knight of the Seven Kingdoms - S01E01 - The Hedge Knight",
                SourcePath = @"C:\Videos\A Knight of the Seven Kingdoms - S01E01 - The Hedge Knight.mkv",
                Kind = MediaKind.Video
            },
            new MediaItem
            {
                Id = "s1e2",
                Title = "A Knight of the Seven Kingdoms - S01E02 - The Sworn Sword",
                SourcePath = @"C:\Videos\A Knight of the Seven Kingdoms - S01E02 - The Sworn Sword.mkv",
                Kind = MediaKind.Video
            }
        };

        var consolidated = TvShowHelper.ConsolidateVideoLibrary(videos);

        Assert.AreEqual(1, consolidated.Count);
        var series = consolidated[0];
        Assert.IsTrue(series.IsSeries);
        Assert.AreEqual("2 Seasons • 3 Episodes", series.ReleaseYear);
        Assert.AreEqual(3, series.Episodes!.Count);

        // Ordered by Season then Episode
        Assert.AreEqual(1, series.Episodes[0].SeasonNumber);
        Assert.AreEqual(1, series.Episodes[0].EpisodeNumber);

        Assert.AreEqual(1, series.Episodes[1].SeasonNumber);
        Assert.AreEqual(2, series.Episodes[1].EpisodeNumber);

        Assert.AreEqual(2, series.Episodes[2].SeasonNumber);
        Assert.AreEqual(1, series.Episodes[2].EpisodeNumber);
    }

    [TestMethod]
    public void VideoViewModel_PlayVideo_SeriesCard_QueuesAllEpisodesInOrder()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        var ep1 = new MediaItem { Id = "ep1", Title = "Episode 1", SourcePath = "C:/Videos/E1.mp4", Kind = MediaKind.Video, SeasonNumber = 1, EpisodeNumber = 1 };
        var ep2 = new MediaItem { Id = "ep2", Title = "Episode 2", SourcePath = "C:/Videos/E2.mp4", Kind = MediaKind.Video, SeasonNumber = 1, EpisodeNumber = 2 };

        var seriesCard = new MediaItem
        {
            Id = "series-knight",
            Title = "A Knight of the Seven Kingdoms",
            Kind = MediaKind.Video,
            IsSeries = true,
            Episodes = new List<MediaItem> { ep1, ep2 },
            SourcePath = ep1.SourcePath
        };

        vm.PlayVideo(seriesCard);

        Assert.IsTrue(playback.IsVideoPlayerActive);
        Assert.AreEqual(seriesCard, vm.CurrentVideo);
        Assert.IsTrue(vm.IsPlaying);

        // Verify SetQueue was called with the series episodes list starting at index 0
        mockSession.Verify(s => s.SetQueue(It.Is<IEnumerable<MediaItem>>(q => q != null && q.SequenceEqual(seriesCard.Episodes)), 0), Moq.Times.Once);
    }

    [TestMethod]
    public void VideoViewModel_PlayVideo_SingleEpisodeOfSeries_QueuesAllEpisodesStartingFromChosenEpisode()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        var ep1 = new MediaItem { Id = "ep1", Title = "Episode 1", SourcePath = "C:/Videos/E1.mp4", Kind = MediaKind.Video, SeasonNumber = 1, EpisodeNumber = 1 };
        var ep2 = new MediaItem { Id = "ep2", Title = "Episode 2", SourcePath = "C:/Videos/E2.mp4", Kind = MediaKind.Video, SeasonNumber = 1, EpisodeNumber = 2 };
        var ep3 = new MediaItem { Id = "ep3", Title = "Episode 3", SourcePath = "C:/Videos/E3.mp4", Kind = MediaKind.Video, SeasonNumber = 1, EpisodeNumber = 3 };

        var seriesCard = new MediaItem
        {
            Id = "series-knight",
            Title = "A Knight of the Seven Kingdoms",
            Kind = MediaKind.Video,
            IsSeries = true,
            Episodes = new List<MediaItem> { ep1, ep2, ep3 },
            SourcePath = ep1.SourcePath
        };

        vm.FilteredVideos.Add(seriesCard);

        // Play Episode 2 specifically
        vm.PlayVideo(ep2);

        Assert.IsTrue(playback.IsVideoPlayerActive);
        Assert.AreEqual(ep2, vm.CurrentVideo);
        Assert.IsTrue(vm.IsPlaying);

        // Verify SetQueue was called with the full series episodes list starting at index 1 (Episode 2)
        mockSession.Verify(s => s.SetQueue(It.Is<IEnumerable<MediaItem>>(q => q != null && q.SequenceEqual(seriesCard.Episodes)), 1), Moq.Times.Once);
    }

    [TestMethod]
    public void VideoViewModel_PlayVideo_StandaloneMovie_QueuesOnlyMovie()
    {
        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(new List<MediaItem>());
        mockSession.Setup(s => s.CurrentIndex).Returns(-1);
        var playback = new PlaybackViewModel(mockSession.Object);

        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        var movie1 = new MediaItem { Id = "m1", Title = "Inception", SourcePath = "C:/Movies/Inception.mp4", Kind = MediaKind.Video };
        var movie2 = new MediaItem { Id = "m2", Title = "Interstellar", SourcePath = "C:/Movies/Interstellar.mp4", Kind = MediaKind.Video };
        vm.FilteredVideos.Add(movie1);
        vm.FilteredVideos.Add(movie2);

        vm.PlayVideo(movie1);

        Assert.IsTrue(playback.IsVideoPlayerActive);
        Assert.AreEqual(movie1, vm.CurrentVideo);

        // Verify SetQueue queued only movie1
        mockSession.Verify(s => s.SetQueue(It.Is<IEnumerable<MediaItem>>(q => q != null && q.Count() == 1 && q.First().Id == "m1"), 0), Moq.Times.Once);
    }

    [TestMethod]
    public void VideoViewModel_SyncFromPlayback_PlaylistSession_PopulatesUpNextVideos()
    {
        var item1 = new MediaItem { Id = "p1", Title = "Track 1", SourcePath = "C:/Videos/1.mp4", Kind = MediaKind.Video };
        var item2 = new MediaItem { Id = "p2", Title = "Track 2", SourcePath = "C:/Videos/2.mp4", Kind = MediaKind.Video };
        var playlistQueue = new List<MediaItem> { item1, item2 };

        var mockSession = new Mock<IPlaybackSession>();
        mockSession.Setup(s => s.Queue).Returns(playlistQueue);
        mockSession.Setup(s => s.CurrentIndex).Returns(0);
        mockSession.Setup(s => s.CurrentTrack).Returns(item1);

        var playback = new PlaybackViewModel(mockSession.Object);
        var mockHdr = new Mock<IHdrPipelineService>();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Current).Returns(new AppSettings());

        var vm = new VideoViewModel(playback, mockHdr.Object, mockSettings.Object);

        vm.SyncFromPlayback();

        // UpNextVideos must reflect the playlist queue, not library
        Assert.AreEqual(2, vm.UpNextVideos.Count);
        Assert.AreEqual("p1", vm.UpNextVideos[0].Id);
        Assert.AreEqual("p2", vm.UpNextVideos[1].Id);
    }
}

