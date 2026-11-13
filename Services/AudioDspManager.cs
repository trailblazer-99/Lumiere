using System;
using System.Threading.Tasks;
using LumiereMediaPlayer.Models;
using Windows.Media.Playback;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Handles audio categorization, reverb profiles, voice clarity, and equalizer categorization.
/// </summary>
public class AudioDspManager : IAudioDspManager
{
    public void ApplyEffects(MediaPlayer mediaPlayer, AppSettings settings)
    {
        if (mediaPlayer == null || settings == null) return;

        try
        {
            if (settings.VoiceClarityEnabled)
            {
                mediaPlayer.AudioCategory = MediaPlayerAudioCategory.Speech;
            }
            else if (settings.NightModeEnabled)
            {
                mediaPlayer.AudioCategory = MediaPlayerAudioCategory.SoundEffects;
            }
            else
            {
                mediaPlayer.AudioCategory = settings.SelectedReverbPreset switch
                {
                    "Concert Hall" => MediaPlayerAudioCategory.Movie,
                    "Cave" => MediaPlayerAudioCategory.Movie,
                    "Auditorium" => MediaPlayerAudioCategory.Media,
                    _ => MediaPlayerAudioCategory.Media
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioDspManager] ApplyEffects error: {ex.Message}");
        }
    }

    public async Task<EqualizerPreset> DetermineEqualizerPresetAsync(MediaItem track, AppSettings settings)
    {
        if (track == null || settings == null || !settings.AiEqualizerMatcherEnabled)
        {
            return EqualizerPreset.Flat;
        }

        string genre = track.Genre ?? string.Empty;
        string title = track.Title ?? string.Empty;

        // 1. Fast local genre matching
        if (genre.Contains("Rock", StringComparison.OrdinalIgnoreCase) || genre.Contains("Metal", StringComparison.OrdinalIgnoreCase))
        {
            return EqualizerPreset.Rock;
        }
        if (genre.Contains("Pop", StringComparison.OrdinalIgnoreCase) || genre.Contains("Dance", StringComparison.OrdinalIgnoreCase))
        {
            return EqualizerPreset.Pop;
        }
        if (genre.Contains("Electronic", StringComparison.OrdinalIgnoreCase) || genre.Contains("Techno", StringComparison.OrdinalIgnoreCase) || genre.Contains("Club", StringComparison.OrdinalIgnoreCase))
        {
            return EqualizerPreset.Electronic;
        }
        if (genre.Contains("Classical", StringComparison.OrdinalIgnoreCase) || genre.Contains("Orchestral", StringComparison.OrdinalIgnoreCase))
        {
            return EqualizerPreset.Classical;
        }
        if (genre.Contains("Jazz", StringComparison.OrdinalIgnoreCase) || genre.Contains("Blues", StringComparison.OrdinalIgnoreCase))
        {
            return EqualizerPreset.Jazz;
        }
        if (genre.Contains("Speech", StringComparison.OrdinalIgnoreCase) || genre.Contains("Podcast", StringComparison.OrdinalIgnoreCase) || genre.Contains("Vocal", StringComparison.OrdinalIgnoreCase))
        {
            return EqualizerPreset.Vocal;
        }

        // 2. AI matching fallback
        var config = ConfigService.Config;
        bool hasAiProvider = settings.UseLocalAi || !string.IsNullOrWhiteSpace(settings.GeminiApiKey) || (config.UseProxy && !string.IsNullOrEmpty(config.ProxyBaseUrl));
        if (hasAiProvider)
        {
            try
            {
                var apiResult = await AiAssistantService.CategorizeEqualizerAsync(title, genre);
                if (apiResult != EqualizerPreset.Flat)
                {
                    return apiResult;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioDspManager] AI Equalizer matching error: {ex.Message}");
            }
        }

        return EqualizerPreset.Flat;
    }
}
