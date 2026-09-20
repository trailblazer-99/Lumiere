using System;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Manages media audio categorization, equalizer matching, voice clarity, and sound spatialization.
/// </summary>
public interface IAudioDspManager
{
    void ApplyEffects(MediaPlayer mediaPlayer, AppSettings settings);
    Task<EqualizerPreset> DetermineEqualizerPresetAsync(MediaItem track, AppSettings settings);
}
