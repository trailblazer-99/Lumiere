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
    public SettingsViewModel ViewModel { get; } = AppServices.SettingsViewModel;

    public SettingsPage()
    {
        InitializeComponent();
        this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Disabled;
    }

    private async void OnAddFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add("*");

            var window = App.MainWindowInstance;
            IntPtr hwnd = window != null ? WindowHelper.GetWindowHandle(window) : IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                ViewModel.AddFolder(folder.Path);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
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
            if (_allSearchItems.Count == 0)
            {
                InitializeSettingsSearchCatalog();
            }

            if (AppServices.Settings.Current.ReduceMotion)
            {
                PageContent.Opacity = 1.0;
                return;
            }

            var visual = ElementCompositionPreview.GetElementVisual(PageContent);
            var compositor = visual.Compositor;

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(0f, 0f);
            fadeAnimation.InsertKeyFrame(1f, 1f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(300);
            visual.StartAnimation("Opacity", fadeAnimation);

            var slideAnimation = compositor.CreateVector3KeyFrameAnimation();
            slideAnimation.InsertKeyFrame(0f, new System.Numerics.Vector3(0, 16, 0));
            slideAnimation.InsertKeyFrame(1f, new System.Numerics.Vector3(0, 0, 0));
            slideAnimation.Duration = TimeSpan.FromMilliseconds(350);
            visual.StartAnimation("Offset", slideAnimation);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to animate SettingsPage entrance: {ex.Message}");
            PageContent.Opacity = 1.0;
        }
    }

    private readonly List<SettingSearchItem> _allSearchItems = new();

    private void InitializeSettingsSearchCatalog()
    {
        _allSearchItems.Clear();

        // 1. Playback
        _allSearchItems.Add(new SettingSearchItem { Title = "Autoplay on launch", Description = "Automatically start playback when the app starts", Section = "Playback", Keywords = "autoplay, auto play, start, launch, boot, startup, begin", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Resume playback position", Description = "Remember where you left off and resume from that location", Section = "Playback", Keywords = "resume, remember, history, progress, position, time, continue, timestamp", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Skip forward interval", Description = "How many seconds to skip when seeking forward", Section = "Playback", Keywords = "skip, forward, jump, seconds, interval, seek, fast forward, 5s, 10s, 15s, 30s, 45s, 60s", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Skip backward interval", Description = "How many seconds to skip when seeking backward", Section = "Playback", Keywords = "skip, backward, back, jump, seconds, interval, seek, rewind, 5s, 10s, 15s, 30s, 45s, 60s", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Default playback speed", Description = "Set the default speed for media playback (0.25x - 2.0x)", Section = "Playback", Keywords = "speed, rate, fast, slow, 1x, 2x, 1.5x, 0.5x, pitch, tempo, playback speed, normal", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Auto-advance to next track", Description = "Automatically play the next track when current one finishes", Section = "Playback", Keywords = "auto advance, next track, autoplay next, playlist, queue, continue, next song", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Remember last played track", Description = "Load the last played track on app startup", Section = "Playback", Keywords = "remember track, last played, startup, resume song, previous track, history", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Crossfade between tracks", Description = "Smoothly blend audio when transitioning between tracks", Section = "Playback", Keywords = "crossfade, blend, transition, smooth, mix, overlap, fade, duration, seconds", TargetElement = PlaybackSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Gapless playback", Description = "Eliminate silence gaps between consecutive tracks in an album", Section = "Playback", Keywords = "gapless, seamless, no gap, album, continuous, silence, silence removal", TargetElement = PlaybackSection });

        // 2. Audio & Output
        _allSearchItems.Add(new SettingSearchItem { Title = "Equalizer preset", Description = "Adjust 10-band frequency levels and audio EQ presets", Section = "Audio & Output", Keywords = "equalizer, eq, graphic eq, 10 band, bands, frequency, preset, rock, pop, jazz, classical, acoustic, flat", TargetElement = AudioSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Volume normalization", Description = "Balance volume differences across songs and albums", Section = "Audio & Output", Keywords = "replaygain, normalization, normalize, loudness, volume leveling, peak, ebu r128, track, album", TargetElement = AudioSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Default startup volume", Description = "Set the starting volume level for new sessions", Section = "Audio & Output", Keywords = "volume, loudness, sound level, initial volume, default startup volume, percentage, master", TargetElement = AudioSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Mono audio", Description = "Combine left and right audio channels into a single mono channel", Section = "Audio & Output", Keywords = "mono, mono audio, stereo, channels, accessibility, downmix", TargetElement = AudioSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Bass boost level", Description = "Enhance low-end frequencies and subwoofer punch", Section = "Audio & Output", Keywords = "bass, bass boost, low frequency, subwoofer, punch, deep bass, low end, level, slider", TargetElement = AudioSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Audio balance", Description = "Adjust stereo channel balance between Left and Right", Section = "Audio & Output", Keywords = "balance, left, right, stereo, channels, pan, l, r, stereo balance, audio balance", TargetElement = AudioSection });

        // 3. Video
        _allSearchItems.Add(new SettingSearchItem { Title = "Default aspect ratio", Description = "Set default video framing (16:9, 4:3, 21:9, Zoom, Fill)", Section = "Video", Keywords = "aspect ratio, 16:9, 4:3, 21:9, stretch, fill, fit, zoom, widescreen, ultrawide, letterbox, pillarbox", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Default subtitle language", Description = "Preferred language for automatic subtitle selection", Section = "Video", Keywords = "subtitle, language, english, spanish, french, german, captions, srt", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Subtitle font size", Description = "Adjust the default size of on-screen subtitle text", Section = "Video", Keywords = "subtitle size, font size, small, medium, large, extra large, text size, font scale", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Subtitle background opacity", Description = "Control translucent background box opacity behind subtitles", Section = "Video", Keywords = "subtitle background, opacity, translucent, black box, shadow, outline, backdrop, readability", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Hardware acceleration", Description = "Use GPU video decoding (DXVA2, D3D11VA, NVDEC)", Section = "Video", Keywords = "hardware acceleration, gpu, decode, dxva, dxva2, d3d11, nvdec, vce, intel qsv, amd, nvidia, video card", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Auto-rotate video", Description = "Automatically rotate portrait and landscape videos based on orientation metadata", Section = "Video", Keywords = "rotate, auto rotate, orientation, portrait, landscape", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "HDR mode", Description = "Configure Auto, Forced, or SDR mode for HDR10 & streaming displays", Section = "Video", Keywords = "hdr, high dynamic range, hdr10, auto, force, forced hdr, sdr, 10bit, 12bit, bt2020, dci-p3, colorspace, wide color", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Auto brightness boost in HDR", Description = "Automatically boost display brightness to 100% during HDR playback", Section = "Video", Keywords = "brightness, auto, hdr, boost, luminance, monitor, screen, 100%, peak brightness, nits, laptop", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Tone mapping", Description = "Choose ACES, Reinhard, or Hable tone-mapping operator", Section = "Video", Keywords = "tonemap, tone mapping, aces, reinhard, hable, luminance, roll-off, clipping, contrast, sdr conversion", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Real-time HDR decoding", Description = "Direct GPU decoding and MPO shared-surface rendering", Section = "Video", Keywords = "decode, realtime, latency, gpu, hardware, mpo, dual gpu, nvdec, d3d11va, direct shared surface, 10-bit", TargetElement = VideoSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show HDR badge on player", Description = "Display an HDR10/HLG badge on the video player when active", Section = "Video", Keywords = "badge, overlay, indicator, status, hdr10, hlg, dolby vision, tag, label, stamp", TargetElement = VideoSection });

        // 4. Appearance & Visuals
        _allSearchItems.Add(new SettingSearchItem { Title = "App theme", Description = "Switch between Dark, Light, or System default theme", Section = "Appearance & Visuals", Keywords = "theme, dark, light, system, mode, color, appearance, style, dark mode, light mode", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Backdrop type", Description = "Change window material (Mica, Mica Alt, Acrylic, Solid)", Section = "Appearance & Visuals", Keywords = "backdrop, mica, mica alt, acrylic, solid, blur, transparency, glass, material, aero, window background", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Accent color", Description = "Choose primary highlight color (Orange, Purple, Blue, Teal, Red, Pink)", Section = "Appearance & Visuals", Keywords = "accent, color, tint, orange, purple, blue, teal, red, pink, custom color, system default", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show media card glow", Description = "Enable ambient artwork-colored glow effects on media cards", Section = "Appearance & Visuals", Keywords = "glow, card glow, ambient, dynamic glow, border glow, illumination, card border", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show timeline preview on hover", Description = "Display timestamp preview when hovering over timeline slider", Section = "Appearance & Visuals", Keywords = "timeline preview, hover, timestamp, preview thumbnail, seek preview", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Compact density mode", Description = "Reduce padding and spacing for a denser interface layout", Section = "Appearance & Visuals", Keywords = "compact, density, layout, padding, spacing, dense", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show album art in transport bar", Description = "Display album artwork thumbnail in bottom playback bar", Section = "Appearance & Visuals", Keywords = "album art, transport bar, thumbnail, artwork, bottom bar, cover", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Animated transitions", Description = "Enable smooth page and card transition animations", Section = "Appearance & Visuals", Keywords = "animations, transitions, animated, smooth, motion", TargetElement = AppearanceSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show transport bar", Description = "Toggle playback controls bar visibility across the app", Section = "Appearance & Visuals", Keywords = "transport bar, show transport bar, hide transport bar, player bar, bottom bar, playback bar, mini bar, toggle transport bar, transport", TargetElement = AppearanceSection });

        // 5. Controls & Interface
        _allSearchItems.Add(new SettingSearchItem { Title = "Show shuffle button", Description = "Display the shuffle button in the transport controls", Section = "Controls & Interface", Keywords = "shuffle, random, transport controls, button", TargetElement = ControlsSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show repeat button", Description = "Display the repeat button in the transport controls", Section = "Controls & Interface", Keywords = "repeat, loop, transport controls, button", TargetElement = ControlsSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show subtitles button", Description = "Display the subtitles toggle in the transport controls", Section = "Controls & Interface", Keywords = "subtitles, captions, cc, transport controls, button", TargetElement = ControlsSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show fullscreen button", Description = "Display the fullscreen toggle in the transport controls", Section = "Controls & Interface", Keywords = "fullscreen, maximize, expand, transport controls, button", TargetElement = ControlsSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show Picture in Picture button", Description = "Display the mini overlay player toggle button", Section = "Controls & Interface", Keywords = "pip, picture in picture, compact overlay, mini player, pin", TargetElement = ControlsSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show queue in more menu", Description = "Show playback queue option in the transport overflow menu", Section = "Controls & Interface", Keywords = "queue, play queue, more menu, overflow, up next", TargetElement = ControlsSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show speed controls in more menu", Description = "Show playback speed selection in the transport overflow menu", Section = "Controls & Interface", Keywords = "speed, playback rate, 1x, 2x, more menu, overflow", TargetElement = ControlsSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show open files button on home page", Description = "Display quick file picker button on the Home screen", Section = "Controls & Interface", Keywords = "open files, browse, home page button, picker button, show button, home browse", TargetElement = ControlsSection });

        // 6. Media Library & Files
        _allSearchItems.Add(new SettingSearchItem { Title = "Library folders", Description = "Add or remove local folders to include in your media library", Section = "Media Library & Files", Keywords = "library, folders, scan, folder, add, music, videos, directory, path, watch folder, media folders, remove folder", TargetElement = LibrarySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Automatic library scan on launch", Description = "Automatically check for new media files on startup", Section = "Media Library & Files", Keywords = "scan, refresh, update, library, startup scan, background scan, auto scan", TargetElement = LibrarySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Sort library by", Description = "Default sorting for media lists (Title, Artist, Date, Duration)", Section = "Media Library & Files", Keywords = "sort, order, title, artist, album, date added, year, duration, file name, ascending, descending", TargetElement = LibrarySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Show hidden files", Description = "Include hidden folders and files when scanning libraries", Section = "Media Library & Files", Keywords = "hidden, dot files, scan, library, hidden files", TargetElement = LibrarySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Auto-import new files", Description = "Monitor watched library folders in real-time for newly added files", Section = "Media Library & Files", Keywords = "auto import, watcher, monitor, real-time, scan, import", TargetElement = LibrarySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Supported formats", Description = "View and configure file extensions recognized by Lumière", Section = "Media Library & Files", Keywords = "formats, extensions, mp4, mkv, mp3, flac, wav, aac, m4a, avi, webm, hevc, av1", TargetElement = LibrarySection });

        // 7. AI Features
        _allSearchItems.Add(new SettingSearchItem { Title = "Enable lyrics translation", Description = "Automatically translate synced lyrics using AI", Section = "AI Features", Keywords = "ai lyrics, translation, translate, song lyrics, multilingual, synced lyrics, realtime lyrics", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Translation target language", Description = "Select language for AI lyrics and subtitle translation", Section = "AI Features", Keywords = "target language, language, english, spanish, french, german, japanese, chinese, hindi, korean, italian, russian", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Use Local AI (Ollama)", Description = "Process AI tasks locally on-device for privacy and offline use", Section = "AI Features", Keywords = "ollama, local ai, privacy, offline, on-device, llm, phi, llama, mistral, deepseek", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Ollama Model Name", Description = "Specify the installed Ollama model for AI features", Section = "AI Features", Keywords = "model name, llama3, mistral, gemma, phi3, deepseek, qwen, local model, prompt", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Enable semantic search", Description = "Natural language search in your music and video libraries", Section = "AI Features", Keywords = "semantic search, natural language, ai search, query, smart search, vector, embeddings", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "AI Equalizer Matcher", Description = "Automatically match EQ presets to track genre and acoustics", Section = "AI Features", Keywords = "ai equalizer, eq matcher, smart eq, auto eq, sound profile, genre detection, acoustic tuning", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Voice Clarity Enhancer", Description = "Hardware-accelerated dialogue boost for clearer vocals", Section = "AI Features", Keywords = "voice clarity, dialogue boost, spoken voice, dialogue clarity, speech enhancement, vocal booster, vocals", TargetElement = AiSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Dynamic Volume Leveler (Night Mode)", Description = "Level loud explosions and boost quiet dialogue for night viewing", Section = "AI Features", Keywords = "night mode, volume leveler, dynamic range, quiet dialogue, loud explosions, late night, compression, drc", TargetElement = AiSection });

        // 8. Privacy & History
        _allSearchItems.Add(new SettingSearchItem { Title = "Send anonymous telemetry", Description = "Share usage statistics to help improve Lumière", Section = "Privacy & History", Keywords = "telemetry, crash, data, usage, privacy, diagnostics, reports, analytics, anonymous", TargetElement = PrivacySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "API Data Attribution", Description = "Metadata and streaming availability provided by TMDB & Watchmode", Section = "Privacy & History", Keywords = "tmdb, watchmode, api, attribution, license, credits, terms", TargetElement = PrivacySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Remember recently played", Description = "Keep history of recently played media items", Section = "Privacy & History", Keywords = "recently played, history, recent items, playback history, track list, recent videos, recent songs", TargetElement = PrivacySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Remember playback position per track", Description = "Store and resume last position for each individual track", Section = "Privacy & History", Keywords = "position, per track, remember time, bookmark, track history, resume time", TargetElement = PrivacySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Clear data", Description = "Clear playback history, search history, or recent files", Section = "Privacy & History", Keywords = "clear, delete, reset history, wipe cache, clear recent, remove history, purge data", TargetElement = PrivacySection });

        // 9. Accessibility
        _allSearchItems.Add(new SettingSearchItem { Title = "High contrast mode", Description = "Increase contrast between UI elements for better legibility", Section = "Accessibility", Keywords = "high contrast, contrast, accessibility, black, white, yellow, visibility, legible, vision", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Text scale", Description = "Increase text size proportionally throughout the application", Section = "Accessibility", Keywords = "text scale, font size, zoom, text size, large text, magnification, 100%, 150%, 200%, scale", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Color blind mode", Description = "Color filters for Protanopia, Deuteranopia, and Tritanopia", Section = "Accessibility", Keywords = "color blind, protanopia, deuteranopia, tritanopia, daltonism, color vision deficiency, palette", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Screen reader optimization", Description = "Optimize UI automation peer labels for Narrator & NVDA", Section = "Accessibility", Keywords = "screen reader, narrator, jaws, nvda, accessibility, aria, automation, speech, voice", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Focus indicator thickness", Description = "Adjust thickness of focus rectangle borders around controls", Section = "Accessibility", Keywords = "focus indicator, focus border, keyboard focus, outline, thickness, 1px, 2px, 3px, 4px, 5px, border", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Captions always on", Description = "Automatically enable captions for all media playback", Section = "Accessibility", Keywords = "captions, closed captions, cc, always on, subtitles, forced captions, deaf, hard of hearing", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Visual notifications for sound", Description = "Flash visual indicators when audio cues play", Section = "Accessibility", Keywords = "visual notifications, sound cues, flash, screen flash, audio indicator, deaf, alert flash", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Keyboard navigation highlight", Description = "Show prominent high-visibility focus borders during keyboard navigation", Section = "Accessibility", Keywords = "keyboard navigation, highlight, orange focus, tab navigation, arrows, hotkey focus", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Larger click targets", Description = "Increase touch/click target sizing on buttons and controls to 44px+", Section = "Accessibility", Keywords = "larger click targets, touch, touch friendly, 44px, large buttons, big icons, motor accessibility", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Reduce motion", Description = "Disable animations and smooth visual transitions throughout app", Section = "Accessibility", Keywords = "reduce motion, disable animations, smooth transitions, vestibular, motion sickness, instant", TargetElement = AccessibilitySection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Auto-read controls", Description = "Announce UI control labels when focused by keyboard or mouse", Section = "Accessibility", Keywords = "auto read, announce, speak, voice over, control labels, focused item, narrator helper", TargetElement = AccessibilitySection });

        // 10. Keyboard Shortcuts
        _allSearchItems.Add(new SettingSearchItem { Title = "Available keyboard shortcuts", Description = "View keyboard controls for playback, seeking, volume, and fullscreen", Section = "Keyboard Shortcuts", Keywords = "keyboard, shortcuts, hotkeys, space, arrows, media keys, f, m, j, l, hotkey table, available keyboard shortcuts", TargetElement = ShortcutsSection });

        // 11. Reset & About
        _allSearchItems.Add(new SettingSearchItem { Title = "Reset settings", Description = "Restore all Lumière player preferences to factory defaults", Section = "Reset & About", Keywords = "reset, restore, clear, default, factory reset, wipe preferences, reset settings", TargetElement = AboutSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "Check for updates", Description = "Check online for new Lumière Media Player versions", Section = "Reset & About", Keywords = "update, version, release, new version, check for updates, upgrade, github, download", TargetElement = AboutSection });
        _allSearchItems.Add(new SettingSearchItem { Title = "About Lumière Media Player", Description = "Version info, license, and system hardware specifications", Section = "Reset & About", Keywords = "about, version, update, license, author, developer, credits, build number", TargetElement = AboutSection });
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

                var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                // Score-based search ranking
                var scoredResults = _allSearchItems
                    .Select(item =>
                    {
                        int score = 0;

                        // Exact phrase matches (highest priority)
                        if (ContainsIgnoreCase(item.Title, query)) score += 100;
                        if (ContainsIgnoreCase(item.Keywords, query)) score += 60;
                        if (ContainsIgnoreCase(item.Description, query)) score += 40;
                        if (ContainsIgnoreCase(item.Section, query)) score += 30;

                        // Per-term matches
                        int matchedTerms = 0;
                        foreach (var term in terms)
                        {
                            bool termMatched = false;
                            if (ContainsIgnoreCase(item.Title, term)) { score += 25; termMatched = true; }
                            if (ContainsIgnoreCase(item.Keywords, term)) { score += 15; termMatched = true; }
                            if (ContainsIgnoreCase(item.Description, term)) { score += 10; termMatched = true; }
                            if (ContainsIgnoreCase(item.Section, term)) { score += 5; termMatched = true; }
                            if (termMatched) matchedTerms++;
                        }

                        // Bonus for matching all terms
                        if (matchedTerms == terms.Length) score += 50;

                        return new { Item = item, Score = score, MatchedTerms = matchedTerms };
                    })
                    .Where(x => x.Score > 0 && x.MatchedTerms > 0)
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.Item)
                    .ToList();

                var localResults = new System.Collections.ObjectModel.ObservableCollection<SettingSearchItem>(scoredResults);

                if (currentRevision != _searchRevision) return;
                sender.ItemsSource = localResults;

                // Second pass: background AI search for better semantic matching
                var aiResults = await LumiereMediaPlayer.Services.AiAssistantService.SemanticSearchSettingsAsync(query, _allSearchItems);
                if (currentRevision == _searchRevision && aiResults != null && aiResults.Count > 0)
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        if (currentRevision == _searchRevision)
                        {
                            foreach (var item in aiResults)
                            {
                                if (!localResults.Contains(item))
                                {
                                    localResults.Add(item);
                                }
                            }
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
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
            if (PageScrollViewer == null) return;

            // Find the exact setting card for this item, or fall back to section
            FrameworkElement? target = FindCardByTitle(item.Title) ?? item.TargetElement as FrameworkElement;
            if (target == null) return;

            // Calculate position of target element relative to PageScrollViewer
            var transform = target.TransformToVisual(PageScrollViewer);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            
            // Center the specific card nicely in viewport (offset by ~90px from top)
            double currentOffset = PageScrollViewer.VerticalOffset;
            double targetOffset = Math.Max(0, currentOffset + point.Y - 90);
            
            PageScrollViewer.ChangeView(null, targetOffset, null, false);

            // Highlight ONLY this specific setting card with accent border & background glow
            if (target is Border borderCard)
            {
                var originalBorder = borderCard.BorderBrush;
                var originalThickness = borderCard.BorderThickness;
                var originalBackground = borderCard.Background;

                var accentBrush = Application.Current.Resources["AccentFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush 
                                  ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 140, 0));

                var glowBackground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 140, 0));

                borderCard.BorderBrush = accentBrush;
                borderCard.BorderThickness = new Thickness(2);
                borderCard.Background = glowBackground;

                // Keep highlight on ONLY this exact card for 2 seconds then smoothly restore
                await Task.Delay(2000);

                borderCard.BorderBrush = originalBorder;
                borderCard.BorderThickness = originalThickness;
                borderCard.Background = originalBackground;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
        }
    }

    private FrameworkElement? FindCardByTitle(string title)
    {
        if (PageContent == null || string.IsNullOrWhiteSpace(title)) return null;
        return FindCardByTitleRecursive(PageContent, title.Trim());
    }

    private FrameworkElement? FindCardByTitleRecursive(DependencyObject parent, string title)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock tb)
            {
                var text = tb.Text?.Trim();
                if (!string.IsNullOrEmpty(text) && 
                    (string.Equals(text, title, StringComparison.OrdinalIgnoreCase) ||
                     text.StartsWith(title, StringComparison.OrdinalIgnoreCase) ||
                     title.StartsWith(text, StringComparison.OrdinalIgnoreCase)))
                {
                    // Traverse upwards to find the enclosing SettingsCard Border
                    var current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(tb);
                    while (current != null && current != PageContent)
                    {
                        if (current is Border b && b != PageContent)
                        {
                            return b;
                        }
                        current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
                    }
                    return tb;
                }
            }

            var found = FindCardByTitleRecursive(child, title);
            if (found != null) return found;
        }
        return null;
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

