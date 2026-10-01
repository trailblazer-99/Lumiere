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

        if (durationSeconds > 0)
        {
            _isProgrammaticUpdate = true;
            try
            {
                ScrubSlider.Maximum = durationSeconds;
                ScrubSlider.Value = positionSeconds;
                TimeTextBlock.Text = $"{FormatTime(positionSeconds)} / {FormatTime(durationSeconds)}";
            }
            finally
            {
                _isProgrammaticUpdate = false;
            }
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
        VolumeChanged?.Invoke(this, e.NewValue);
    }

    private static string FormatTime(double sec)
    {
        var ts = TimeSpan.FromSeconds(sec);
        return ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
    }
}
