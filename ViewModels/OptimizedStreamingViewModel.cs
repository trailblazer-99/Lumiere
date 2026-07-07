using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumiereMediaPlayer.Services.Streaming;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace LumiereMediaPlayer.ViewModels
{
    public partial class StreamSourceItem : ObservableObject
    {
        [ObservableProperty] public partial string Name { get; set; } = string.Empty;
        [ObservableProperty] public partial string LogoPath { get; set; } = string.Empty;
        [ObservableProperty] public partial string Platform { get; set; } = string.Empty;
        [ObservableProperty] public partial string TargetId { get; set; } = string.Empty;
        
        public System.Uri? LogoUri => !string.IsNullOrEmpty(LogoPath) ? new System.Uri(LogoPath) : null;
    }

    public partial class OptimizedStreamingViewModel : ObservableObject
    {
        private CancellationTokenSource? _extractionCts;
        private readonly YoutubeClient _youtubeClient = new();

        [ObservableProperty] public partial ObservableCollection<StreamSourceItem> AvailableStreams { get; set; } = new();
        [ObservableProperty] public partial bool IsLoading { get; set; }
        [ObservableProperty] public partial string? CurrentDirectStreamUrl { get; set; }
        [ObservableProperty] public partial bool IsPlayerVisible { get; set; }

        // Platform Context
        [ObservableProperty] public partial string CurrentPlatform { get; set; } = string.Empty;
        
        // Transport State
        [ObservableProperty] public partial bool IsPlaying { get; set; }
        [ObservableProperty] public partial bool IsMuted { get; set; }
        [ObservableProperty] public partial double Volume { get; set; } = 100;
        [ObservableProperty] public partial double PositionSeconds { get; set; }
        [ObservableProperty] public partial double DurationSeconds { get; set; }
        [ObservableProperty] public partial bool IsLive { get; set; }
        [ObservableProperty] public partial float PlaybackSpeed { get; set; } = 1.0f;
        [ObservableProperty] public partial bool IsTheaterMode { get; set; }

        [RelayCommand]
        private async Task PlayStreamAsync(StreamSourceItem? item)
        {
            if (item == null || string.IsNullOrEmpty(item.TargetId)) return;

            _extractionCts?.Cancel();
            _extractionCts = new CancellationTokenSource();
            var token = _extractionCts.Token;

            IsLoading = true;
            CurrentPlatform = item.Platform;
            string? directUrl = null;

            try
            {
                if (item.Platform == "Twitch")
                {
                    IsLive = true;
                    directUrl = await TwitchExtractionService.GetLiveStreamUrlAsync(item.TargetId, token);
                }
                else if (item.Platform == "YouTube")
                {
                    IsLive = false;
                    var manifest = await _youtubeClient.Videos.Streams.GetManifestAsync(item.TargetId, token);
                    
                    // CRITICAL FIX: Only grab streams where Audio and Video are pre-muxed.
                    // Falling back to GetVideoOnlyStreams() results in playback with zero sound.
                    var streamInfo = manifest.GetMuxedStreams().GetWithHighestVideoQuality();
                    
                    if (streamInfo != null)
                    {
                        directUrl = streamInfo.Url;
                    }
                    else
                    {
                        // Fallback to audio-only if muxed isn't available, but never video-only.
                        var audioStream = manifest.GetAudioOnlyStreams().GetWithHighestBitrate();
                        directUrl = audioStream?.Url;
                    }
                }

                if (!token.IsCancellationRequested && !string.IsNullOrEmpty(directUrl))
                {
                    CurrentDirectStreamUrl = directUrl;
                    IsPlayerVisible = true;
                }
            }
            catch (OperationCanceledException)
            {
                // Discarded gracefully
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to play stream: {ex.Message}");
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    IsLoading = false;
                }
            }
        }

        [RelayCommand]
        public void ClosePlayer()
        {
            _extractionCts?.Cancel();
            IsPlayerVisible = false;
            CurrentDirectStreamUrl = null;
            IsPlaying = false;
        }
    }
}