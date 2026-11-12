using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumiereMediaPlayer.Controls;

public sealed partial class GlassTransportBar : UserControl
{
    public event EventHandler? PlayPauseRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? MuteRequested;
    public event EventHandler? FullscreenRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler<double>? VolumeChanged;

    public GlassTransportBar()
    {
        InitializeComponent();
    }

    private bool _isProgrammaticUpdate;

    public void UpdatePlaybackState(bool isPlaying, double positionSeconds, double durationSeconds)
    {
        PlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";

        if (!double.IsNaN(durationSeconds) && !double.IsInfinity(durationSeconds) && durationSeconds > 0)
        {
            double clampedPos = double.IsNaN(positionSeconds) || double.IsInfinity(positionSeconds)
                ? 0.0
                : Math.Clamp(positionSeconds, 0.0, durationSeconds);

            _isProgrammaticUpdate = true;
            try
            {
                ScrubSlider.Maximum = durationSeconds;
                ScrubSlider.Value = clampedPos;
                TimeTextBlock.Text = $"{FormatTime(clampedPos)} / {FormatTime(durationSeconds)}";
            }
            finally
            {
                _isProgrammaticUpdate = false;
            }
        }
    }

    public void SetVolume(double volume)
    {
        if (double.IsNaN(volume) || double.IsInfinity(volume)) return;
        _isProgrammaticUpdate = true;
        try
        {
            VolumeSlider.Value = Math.Clamp(volume, VolumeSlider.Minimum, VolumeSlider.Maximum);
        }
        finally
        {
            _isProgrammaticUpdate = false;
        }
    }

    public void AdaptToVideoLuminance(float sceneLuminance)
    {
        GlassSurface.AdjustLuminance(sceneLuminance);
    }

    private void OnPlayPauseClicked(object sender, RoutedEventArgs e) => PlayPauseRequested?.Invoke(this, EventArgs.Empty);
    private void OnPreviousClicked(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke(this, EventArgs.Empty);
    private void OnNextClicked(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void OnMuteClicked(object sender, RoutedEventArgs e) => MuteRequested?.Invoke(this, EventArgs.Empty);
    private void OnFullscreenClicked(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke(this, EventArgs.Empty);

    private void OnScrubSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isProgrammaticUpdate) return;

        if (Math.Abs(e.NewValue - e.OldValue) > 0.5)
            SeekRequested?.Invoke(this, e.NewValue);
    }

    private void OnVolumeSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isProgrammaticUpdate) return;
        VolumeChanged?.Invoke(this, e.NewValue);
    }

    private static string FormatTime(double sec)
    {
        if (double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0)
        {
            return "00:00";
        }
        if (sec > 359999)
        {
            sec = 359999;
        }
        var ts = TimeSpan.FromSeconds(sec);
        return ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
    }
}
