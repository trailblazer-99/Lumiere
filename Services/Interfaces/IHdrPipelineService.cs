using System;
using LumiereMediaPlayer.Models;
using Microsoft.UI.Xaml;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Detects display HDR capability, inspects video content metadata for HDR
/// format, configures the MediaPlayer for optimal HDR/SDR output, and
/// manages display brightness during HDR playback.
/// </summary>
public interface IHdrPipelineService
{
    event EventHandler<HdrStateChangedEventArgs>? HdrStateChanged;

    DisplayHdrCapability DisplayCapability { get; }
    HdrContentFormat ContentFormat { get; }
    bool IsDualGpuEnvironment { get; }
    string GpuEnvironmentDescription { get; }
    bool IsDisplayHdrCapable { get; }
    bool IsHdrActive { get; }
    string ContentFormatLabel { get; }
    string DisplayCapabilityLabel { get; }

    void Initialize(Window window);
    HdrContentFormat DetectContentFormat(MediaPlaybackItem? item);
    void ConfigurePipeline(MediaPlayer player, MediaPlaybackItem? item);
    void SetFullscreenState(bool isFullscreen);
    void ResetContentState();
}
