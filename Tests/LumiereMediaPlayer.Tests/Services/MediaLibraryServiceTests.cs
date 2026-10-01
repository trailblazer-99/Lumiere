using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using LumiereMediaPlayer.Models;
using LumiereMediaPlayer.Services;

namespace LumiereMediaPlayer.Tests.Services;

[TestClass]
public class MediaLibraryServiceTests
{
    [TestInitialize]
    public void Setup()
    {
        MediaLibraryService.ClearLibrary();
    }

    [TestCleanup]
    public void Cleanup()
    {
        MediaLibraryService.ClearLibrary();
    }

    [TestMethod]
    public async Task AddTrackAsync_DeduplicatesSamePathWithDifferentCasingAndSlashes()
    {
        var item1 = new MediaItem
        {
            Id = "guid-1",
            Title = "Knight Ep 1",
            FilePath = "C:/Videos/Knight/Episode1.mkv",
            Kind = MediaKind.Video
        };

        var item2 = new MediaItem
        {
            Id = "guid-2",
            Title = "Knight Ep 1 Duplicate",
            FilePath = @"c:\videos\knight\episode1.mkv",
            Kind = MediaKind.Video
        };

        var added1 = await MediaLibraryService.AddTrackAsync(item1);
        var added2 = await MediaLibraryService.AddTrackAsync(item2);

        Assert.IsNotNull(added1);
        Assert.IsNull(added2);
        Assert.AreEqual(1, MediaLibraryService.VideoTracks.Count);
    }

    [TestMethod]
    public async Task ClearVideoTracksAsync_ClearsOnlyVideoTracksPreservingAudio()
    {
        var audio = new MediaItem
        {
            Id = "audio-1",
            Title = "Song 1",
            FilePath = "C:/Music/Song1.mp3",
            Kind = MediaKind.Audio
        };

        var video1 = new MediaItem
        {
            Id = "video-1",
            Title = "Movie 1",
            FilePath = "C:/Videos/Movie1.mp4",
            Kind = MediaKind.Video
        };

        var video2 = new MediaItem
        {
            Id = "video-2",
            Title = "Movie 2",
            FilePath = "C:/Videos/Movie2.mp4",
            Kind = MediaKind.Video
        };

        await MediaLibraryService.AddTrackAsync(audio);
        await MediaLibraryService.AddTrackAsync(video1);
        await MediaLibraryService.AddTrackAsync(video2);

        Assert.AreEqual(2, MediaLibraryService.VideoTracks.Count);
        Assert.AreEqual(1, MediaLibraryService.AudioTracks.Count);

        await MediaLibraryService.ClearVideoTracksAsync();

        Assert.AreEqual(0, MediaLibraryService.VideoTracks.Count);
        Assert.AreEqual(1, MediaLibraryService.AudioTracks.Count);
        Assert.AreEqual("Song 1", MediaLibraryService.AudioTracks.First().Title);
    }

    [TestMethod]
    public async Task SetFavorite_SyncsWithCanonicalTrackInLibrary()
    {
        var canonical = new MediaItem
        {
            Id = "video-fav-1",
            Title = "Movie 1",
            FilePath = "C:/Videos/Movie1.mp4",
            Kind = MediaKind.Video,
            IsFavorite = false
        };
        await MediaLibraryService.AddTrackAsync(canonical);

        // A detached duplicate instance (such as one loaded from playback history)
        var detached = new MediaItem
        {
            Id = "detached-id",
            Title = "Movie 1",
            FilePath = "C:/Videos/Movie1.mp4",
            Kind = MediaKind.Video,
            IsFavorite = false
        };

        MediaLibraryService.SetFavorite(detached, true);

        Assert.IsTrue(detached.IsFavorite);
        Assert.IsTrue(canonical.IsFavorite);
        Assert.AreEqual(1, MediaLibraryService.FavoriteVideos.Count);
        Assert.AreEqual("Movie 1", MediaLibraryService.FavoriteVideos.First().Title);
        Assert.AreEqual(1, MediaLibraryService.FavoritesPlaylist.Tracks.Count);
    }

    [TestMethod]
    public async Task ToggleFavorite_TogglesStateBidirectionally()
    {
        var item = new MediaItem
        {
            Id = "toggle-1",
            Title = "Toggled Video",
            FilePath = "C:/Videos/Toggled.mp4",
            Kind = MediaKind.Video,
            IsFavorite = false
        };
        await MediaLibraryService.AddTrackAsync(item);

        MediaLibraryService.ToggleFavorite(item);
        Assert.IsTrue(item.IsFavorite);
        Assert.AreEqual(1, MediaLibraryService.FavoriteVideos.Count);

        MediaLibraryService.ToggleFavorite(item);
        Assert.IsFalse(item.IsFavorite);
        Assert.AreEqual(0, MediaLibraryService.FavoriteVideos.Count);
    }
}
