using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LumiereMediaPlayer.Helpers;
using LumiereMediaPlayer.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Storage.Pickers;

namespace LumiereMediaPlayer.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly Window _hostWindow;

    public SettingsViewModel ViewModel { get; } = AppServices.SettingsViewModel;

    public SettingsPage()
    {
        _hostWindow = App.MainWindowInstance ?? throw new System.InvalidOperationException("MainWindow is not initialized.");
        InitializeComponent();
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            visual.Opacity = 0f;
        }
        catch { }
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.GetWindowHandle(_hostWindow));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.AddFolder(folder.Path);
        }
    }

    private void OnRemoveFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string path)
        {
            ViewModel.RemoveFolderCommand.Execute(path);
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AppServices.Settings.Current.ReduceMotion)
            {
                try
                {
                    var v = ElementCompositionPreview.GetElementVisual(PageContent);
                    v.Opacity = 1f;
                }
                catch { }
                PageContent.Opacity = 1.0;
                return;
            }

            ElementCompositionPreview.SetIsTranslationEnabled(PageContent, true);
            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            var compositor = visual.Compositor;

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(0f, 0f);
            fadeAnimation.InsertKeyFrame(1f, 1f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(400);
            visual.StartAnimation("Opacity", fadeAnimation);

            var slideAnimation = compositor.CreateVector3KeyFrameAnimation();
            slideAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 24, 0));
            slideAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0));
            slideAnimation.Duration = TimeSpan.FromMilliseconds(450);
            visual.StartAnimation("Translation", slideAnimation);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to animate SettingsPage entrance: {ex.Message}");
            PageContent.Opacity = 1.0;
        }

        try
        {
            AiLyricsTranslationToggle.IsOn = ViewModel.AiLyricsTranslationEnabled;
            AiTranslationLanguageComboBox.SelectedIndex = ViewModel.SelectedAiTranslationLanguageIndex;
            AiSemanticSearchToggle.IsOn = ViewModel.AiSemanticSearchEnabled;
            AiEqualizerMatcherToggle.IsOn = ViewModel.AiEqualizerMatcherEnabled;
            VoiceClarityToggle.IsOn = ViewModel.VoiceClarityEnabled;
            NightModeToggle.IsOn = ViewModel.NightModeEnabled;
        }
        catch { }

        InitializeSettingsSearchCatalog();
    }

    private void OnAiLyricsTranslationToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.AiLyricsTranslationEnabled = toggle.IsOn;
        }
    }

    private void OnAiTranslationLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            ViewModel.SelectedAiTranslationLanguageIndex = comboBox.SelectedIndex;
        }
    }

    private void OnAiSemanticSearchToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.AiSemanticSearchEnabled = toggle.IsOn;
        }
    }

    private void OnAiEqualizerMatcherToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.AiEqualizerMatcherEnabled = toggle.IsOn;
        }
    }

    private void OnVoiceClarityToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.VoiceClarityEnabled = toggle.IsOn;
        }
    }

    private void OnNightModeToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.NightModeEnabled = toggle.IsOn;
        }
    }

    private void OnShowOpenFilesOnHomeToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            ViewModel.ShowOpenFilesOnHome = toggleSwitch.IsOn;
        }
    }

    private readonly List<SettingSearchItem> _allSearchItems = new();

    private void InitializeSettingsSearchCatalog()
    {
        _allSearchItems.Clear();

        // 1. Playback
        _allSearchItems.Add(new SettingSearchItem { Title = "Autoplay on launch", Description = "Automatically start playback when the app starts", Section = "Playback", Keywords = "autoplay, start, launch, automatic", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Resume playback position", Description = "Remember where you left off and resume from that location", Section = "Playback", Keywords = "resume, remember, history, progress, position", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Default skip duration", Description = "Configure jump intervals for skip forward and back", Section = "Playback", Keywords = "skip, forward, backward, jump, seconds", TargetElement = PlaybackSection });

        // 2. Audio & Output
        _allSearchItems.Add(new SettingSearchItem { Title = "Default audio device", Description = "Select output device for media playback", Section = "Audio & Output", Keywords = "audio, speaker, headphones, output, device", TargetElement = AudioSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Exclusive WASAPI mode", Description = "Bypass Windows mixer for bit-perfect audio output", Section = "Audio & Output", Keywords = "wasapi, exclusive, bit-perfect, audio, dac, hi-res", TargetElement = AudioSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Audio pass-through", Description = "Pass Dolby Digital / DTS directly to receiver", Section = "Audio & Output", Keywords = "passthrough, bitstream, dolby, dts, receiver", TargetElement = AudioSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Spatial audio & Atmos", Description = "Enable virtualized surround and Dolby Atmos rendering", Section = "Audio & Output", Keywords = "spatial, atmos, surround, virtual, 3d", TargetElement = AudioSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Equalizer presets", Description = "Adjust frequency bands and EQ presets", Section = "Audio & Output", Keywords = "equalizer, eq, bass, treble, boost, preset", TargetElement = AudioSection });

        // 3. Video
        _allSearchItems.Add(new SettingSearchItem { Title = "HDR mode", Description = "Configure Auto, Forced, or SDR mode for HDR10 & streaming-only displays", Section = "Video", Keywords = "hdr, high dynamic range, auto, force, color, 10bit, bt2020, dci, dual gpu, streaming", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Auto brightness boost in HDR", Description = "Automatically boost monitor or laptop brightness to 100% when HDR playback starts, and restore when finished", Section = "Video", Keywords = "brightness, auto, hdr, boost, luminance, monitor, screen, 100%", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Tone-mapping mode", Description = "Choose ACES, Reinhard, or Hable tone-mapping operator", Section = "Video", Keywords = "tonemap, tone mapping, aces, reinhard, hable, luminance, bright", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Real-time HDR decoding", Description = "Direct GPU decoding and MPO shared-surface rendering", Section = "Video", Keywords = "decode, realtime, latency, gpu, hardware, mpo, dual gpu", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show HDR badge on player", Description = "Display HDR10 badge indicator during playback", Section = "Video", Keywords = "badge, overlay, indicator, status, hdr10, hlg, dolby vision", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Hardware acceleration", Description = "Use GPU video decoding (DXVA2, D3D11VA, NVDEC)", Section = "Video", Keywords = "gpu, decode, acceleration, dxva, nvdec, vce, intel", TargetElement = VideoSection });

        // 4. Appearance & Visuals
        _allSearchItems.Add(new SettingSearchItem { Title = "App theme", Description = "Switch between Dark, Light, or System default theme", Section = "Appearance & Visuals", Keywords = "theme, dark, light, system, mode, color", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Mica / Acrylic material", Description = "Enable Windows 11 Mica backdrop and Acrylic translucency", Section = "Appearance & Visuals", Keywords = "mica, acrylic, glass, blur, transparency, backdrop", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Ambient artwork glow", Description = "Swirling ambient radial-gradient glow behind player", Section = "Appearance & Visuals", Keywords = "ambient, glow, artwork, poster, swirl, dynamic, background", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Reduce motion", Description = "Disable UI transitions and ambient animations for accessibility", Section = "Appearance & Visuals", Keywords = "motion, animation, reduce, accessible, disable animation", TargetElement = AppearanceSection });

        // 5. Controls & Interface
        _allSearchItems.Add(new SettingSearchItem { Title = "OSD auto-hide timeout", Description = "Time before playback controls overlay hides automatically", Section = "Controls & Interface", Keywords = "osd, controls, bar, timeout, hide, overlay", TargetElement = ControlsSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show playback progress in taskbar", Description = "Display media progress bar on taskbar icon", Section = "Controls & Interface", Keywords = "taskbar, progress, windows, icon", TargetElement = ControlsSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Compact overlay mode", Description = "Picture-in-picture floating player always on top", Section = "Controls & Interface", Keywords = "compact, pip, picture in picture, always on top", TargetElement = ControlsSection });

        // 6. Media Library & Files
        _allSearchItems.Add(new SettingSearchItem { Title = "Library watch folders", Description = "Add or remove local folders to include in library", Section = "Media Library & Files", Keywords = "library, scan, folder, add, music, videos, directory", TargetElement = LibrarySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Auto-scan library on launch", Description = "Automatically check for new media files on startup", Section = "Media Library & Files", Keywords = "scan, refresh, update, library", TargetElement = LibrarySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Metadata provider", Description = "Fetch artwork and titles from TMDB or iTunes", Section = "Media Library & Files", Keywords = "tmdb, imdb, scraper, cover, poster, metadata", TargetElement = LibrarySection });

        // 7. AI Features
        _allSearchItems.Add(new SettingSearchItem { Title = "AI frame interpolation (MEMC)", Description = "Smooth motion by generating intermediate frames", Section = "AI Features", Keywords = "ai, memc, fps, smooth, motion, frame, 60fps", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "AI super-resolution", Description = "Real-time AI upscaling for low-resolution video", Section = "AI Features", Keywords = "ai, upscaling, resolution, enhance, 4k, superres", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "AI subtitle translation", Description = "Translate subtitles in real-time using local AI models", Section = "AI Features", Keywords = "ai, subtitle, translate, language, automatic", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "AI lyrics translation", Description = "Translate song lyrics automatically during playback", Section = "AI Features", Keywords = "ai, lyrics, song, music, translate", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "AI voice clarity / Night mode", Description = "Boost vocal clarity and balance dynamic range for late night", Section = "AI Features", Keywords = "ai, voice, dialogue, clarity, night, dynamic range", TargetElement = AiSection });

        // 8. Privacy & History
        _allSearchItems.Add(new SettingSearchItem { Title = "Keep playback history", Description = "Remember watched videos and listening history", Section = "Privacy & History", Keywords = "privacy, history, recent, track, watch, clear", TargetElement = PrivacySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Send anonymous telemetry", Description = "Share usage statistics to help improve Lumière", Section = "Privacy & History", Keywords = "telemetry, crash, data, usage", TargetElement = PrivacySection });

        // 9. Accessibility
        _allSearchItems.Add(new SettingSearchItem { Title = "Closed captions formatting", Description = "Customize subtitle font, size, color, and background opacity", Section = "Accessibility", Keywords = "subtitle, cc, caption, font, size, color, background", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "High contrast subtitles", Description = "Enhance subtitle legibility with high contrast borders", Section = "Accessibility", Keywords = "contrast, accessible, visibility", TargetElement = AccessibilitySection });

        // 10. Keyboard Shortcuts
        _allSearchItems.Add(new SettingSearchItem { Title = "Hotkeys & shortcuts", Description = "View and customize keyboard controls for playback and navigation", Section = "Keyboard Shortcuts", Keywords = "keyboard, shortcuts, hotkeys, space, arrows, media keys", TargetElement = ShortcutsSection });

        // 11. Reset & About
        _allSearchItems.Add(new SettingSearchItem { Title = "Reset settings to default", Description = "Restore all Lumière player preferences to factory defaults", Section = "Reset & About", Keywords = "reset, restore, clear, default", TargetElement = AboutSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "About Lumière Media Player", Description = "Version info, license, and update status", Section = "Reset & About", Keywords = "about, version, update, license, author", TargetElement = AboutSection });
    }

    private int _searchRevision = 0;

    private async void OnSettingsSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        try
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(query))
                {
                    sender.ItemsSource = null;
                    return;
                }

                int currentRevision = System.Threading.Interlocked.Increment(ref _searchRevision);

                // First pass: instant local search
                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var localResults = new System.Collections.ObjectModel.ObservableCollection<SettingSearchItem>(
                    _allSearchItems.Where(item => terms.All(term => 
                                   ContainsIgnoreCase(item.Title, term) ||
                                   ContainsIgnoreCase(item.Keywords, term) ||
                                   ContainsIgnoreCase(item.Section, term) ||
                                   ContainsIgnoreCase(item.Description, term)))
                );

                if (currentRevision != _searchRevision) return;
                sender.ItemsSource = localResults;

                // Second pass: background AI search for better semantic matching
                var aiResults = await LumiereMediaPlayer.Services.AiAssistantService.SemanticSearchSettingsAsync(query, _allSearchItems);
                if (currentRevision == _searchRevision && aiResults != null && aiResults.Count > 0)
                {
                    // Dynamically append AI results to the existing observable collection
                    foreach (var item in aiResults)
                    {
                        if (!localResults.Contains(item))
                        {
                            localResults.Add(item);
                        }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsPage] OnSettingsSearchTextChanged Error: {ex.Message}");
        }
    }

    private static bool ContainsIgnoreCase(string source, string query)
    {
        return source.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSettingsSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SettingSearchItem item)
        {
            sender.Text = item.Title;
            NavigateToSearchItem(item);
        }
    }

    private void OnSettingsSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        SettingSearchItem? itemToNavigate = null;

        if (args.ChosenSuggestion is SettingSearchItem item)
        {
            itemToNavigate = item;
        }
        else if (!string.IsNullOrWhiteSpace(args.QueryText))
        {
            var query = args.QueryText.Trim();
            itemToNavigate = _allSearchItems.FirstOrDefault(i =>
                string.Equals(i.Title, query, StringComparison.OrdinalIgnoreCase) ||
                ContainsIgnoreCase(i.Title, query) ||
                ContainsIgnoreCase(i.Keywords, query));
        }

        if (itemToNavigate != null)
        {
            sender.Text = itemToNavigate.Title;
            NavigateToSearchItem(itemToNavigate);
        }
    }

    private async void NavigateToSearchItem(SettingSearchItem item)
    {
        try
        {
            if (item.TargetElement is UIElement target)
            {
                target.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0.2f
                });

                var origOpacity = target.Opacity;
                target.Opacity = 0.35;
                await Task.Delay(180);
                target.Opacity = 1.0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error navigating to setting search item: {ex.Message}");
        }
    }
}

public sealed class SettingSearchItem
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Section { get; init; } = "";
    public string Keywords { get; init; } = "";
    public UIElement? TargetElement { get; init; }
    public string Subtitle => $"{Section} • {Description}";

    public override string ToString() => Title;
}

