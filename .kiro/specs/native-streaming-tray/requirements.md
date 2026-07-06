# Requirements Document

## Introduction

The Native Streaming Tray is a high-performance, horizontally-scrollable tray component embedded in the FluentMediaPlayer WinUI 3 (.NET 8) desktop application. It aggregates live and on-demand streaming links across YouTube and Twitch, resolves them into raw adaptive media stream URLs, and plays them back using a native LibVLC pipeline — eliminating WebView2 entirely. The feature encompasses a reusable XAML tray control, a hardware-accelerated native player view, an MVVM view model with source-generated properties, a lock-free location engine that accounts for VPN switching, and memory-safe disposal of all unmanaged LibVLC resources.

---

## Glossary

- **NativeStreamingTray**: The reusable XAML `UserControl` (`NativeStreamingTray.xaml`) that renders a horizontally scrollable list of streaming channel/video tiles.
- **NativeDirectStreamPlayer**: The XAML `UserControl` (`NativeDirectStreamPlayer.xaml`) that owns the LibVLCSharp `VideoView` and manages the media lifecycle.
- **OptimizedStreamingViewModel**: The CommunityToolkit.Mvvm `ObservableObject` subclass that drives both tray and player views via source-generated observable properties and relay commands.
- **AntiGravityLocationEngine**: The static utility class that determines the active public IP's ISO 3166-1 alpha-2 country code by querying `https://ipapi.co/json/`, with a 15-second lock-free cache.
- **YoutubeClient**: The `YoutubeExplode.YoutubeClient` instance used to resolve YouTube video IDs into raw adaptive stream URLs.
- **TwitchGraphQL_Endpoint**: The Twitch GraphQL API endpoint `https://api.twitch.tv/gql` used to fetch `PlaybackAccessToken` and signature.
- **Usher_HLS_Endpoint**: The Twitch HLS playlist URL template `https://usher.ttvnw.net/api/channel/hls/{channel_name}.m3u8?token={token}&sig={sig}&allow_source=true`.
- **LibVLC**: The unmanaged Core LibVLC instance initialised via `LibVLCSharp.WinUI`, configured with hardware-accelerated command-line arguments.
- **MediaPlayer**: The `LibVLCSharp.Shared.MediaPlayer` instance that owns the active media pipeline.
- **Media**: The `LibVLCSharp.Shared.Media` instance constructed from a resolved stream URL.
- **CancellationTokenSource**: A `System.Threading.CancellationTokenSource` used to cancel in-flight stream resolution tasks.
- **StreamEntry**: A model record representing a single tray item — containing platform identifier, display title, thumbnail URL, and resolution target (YouTube video ID or Twitch channel name).
- **PlaybackAccessToken**: The JSON `value` field returned by the Twitch GraphQL API's `PlaybackAccessToken` query.
- **TwitchSignature**: The JSON `signature` field returned alongside the `PlaybackAccessToken`.
- **JsonSerializerContext**: A compile-time `System.Text.Json.Serialization.JsonSerializerContext` subclass that provides source-generated serialization for all model types, ensuring Native AOT compatibility.
- **DXVA2**: DirectX Video Acceleration 2, the hardware decode path activated in LibVLC via `--hwdec=dxva2`.
- **ItemsStackPanel**: The WinUI `ItemsStackPanel` set to horizontal orientation, used as the items panel for UI-virtualised tray rendering.

---

## Requirements

### Requirement 1: XAML Tray Component Layout

**User Story:** As a user, I want a horizontally scrollable tray of streaming tiles so that I can browse and launch streams without leaving the main application window.

#### Acceptance Criteria

1. THE `NativeStreamingTray` SHALL render streaming tiles inside an `ItemsControl` whose `ItemsPanel` is a horizontal `ItemsStackPanel`, enabling UI virtualisation.
2. THE `NativeStreamingTray` SHALL display each tile as a transparent button whose content places the platform logo image before the channel/video title label in the visual order.
3. WHEN a tile button receives a pointer-over event, THE `NativeStreamingTray` SHALL change the cursor to `Microsoft.UI.Input.InputSystemCursor` with `InputSystemCursorShape.Hand` automatically.
4. THE `NativeStreamingTray` SHALL decode each platform logo image at exactly 52×52 logical pixels by setting `DecodePixelWidth="52"` and `DecodePixelHeight="52"` on the `Image` element binding, preventing oversized GPU surface allocations.
5. THE `NativeStreamingTray` SHALL expose a `StreamEntries` dependency property of type `IReadOnlyList<StreamEntry>` that the `OptimizedStreamingViewModel` binds to.
6. WHEN `StreamEntries` is null or empty, THE `NativeStreamingTray` SHALL display a placeholder text element reading "No streams available" and SHALL hide the `ItemsControl`.
7. WHEN a tile button is clicked, THE `NativeStreamingTray` SHALL invoke the `SelectStreamCommand` on the bound `OptimizedStreamingViewModel`, passing the corresponding `StreamEntry` as the command parameter.
8. WHEN the `NativeStreamingTray` is unloaded from the visual tree, THE `NativeStreamingTray` SHALL invoke `OptimizedStreamingViewModel.CancelResolutionCommand` to release active playback and unmanaged resources.

---

### Requirement 2: Native Direct Stream Player View

**User Story:** As a user, I want video content to play back natively without a browser engine so that memory and CPU overhead is minimised.

#### Acceptance Criteria

1. THE `NativeDirectStreamPlayer` SHALL host a `LibVLCSharp.WinUI.VideoView` control as its primary rendering surface, with no dependency on `WebView2` or any Chromium-based component.
2. WHEN a non-null, non-empty stream URL is assigned to the `NativeDirectStreamPlayer`, THE `NativeDirectStreamPlayer` SHALL construct a `Media` object from that URL, set `IsPlaybackError` to `false`, and call `MediaPlayer.Play(Media)`.
3. IF a null or empty stream URL is assigned to the `NativeDirectStreamPlayer`, THEN THE `NativeDirectStreamPlayer` SHALL set `IsPlaybackError` to `true` and SHALL NOT attempt to construct a `Media` object or call `MediaPlayer.Play`.
4. WHEN the `NativeDirectStreamPlayer` receives an `Unloaded` event OR the `OptimizedStreamingViewModel.IsStreamActive` property changes to `false` while this view is the active player, THE `NativeDirectStreamPlayer` SHALL call `MediaPlayer.Stop()`, then `MediaPlayer.Dispose()`, then `Media.Dispose()`, in that order, on non-null instances only, before setting both fields to `null`.
5. THE `NativeDirectStreamPlayer` SHALL initialise the `LibVLC` instance with the command-line arguments `"--file-caching=1200"`, `"--network-caching=1200"`, and `"--hwdec=dxva2"` to enable DXVA2 hardware-accelerated decode and reduce buffer starvation.
6. IF `MediaPlayer.Play()` returns `false`, THEN THE `NativeDirectStreamPlayer` SHALL set `IsPlaybackError` to `true` without throwing an unhandled exception.

---

### Requirement 3: MVVM State Controller

**User Story:** As a developer, I want a single ViewModel driving both the tray and player views so that state is consistent and the UI remains responsive.

#### Acceptance Criteria

1. THE `OptimizedStreamingViewModel` SHALL derive from `CommunityToolkit.Mvvm.ComponentModel.ObservableObject` and SHALL use `[ObservableProperty]` source-generator attributes for all bindable state fields.
2. THE `OptimizedStreamingViewModel` SHALL expose three `[RelayCommand]`-decorated async methods: `LoadStreamEntriesAsync()` with no parameters, `SelectStreamAsync(StreamEntry entry)`, and `CancelResolutionAsync()` with no parameters.
3. WHEN `SelectStreamAsync` is invoked while `IsResolving` is `true`, THE `OptimizedStreamingViewModel` SHALL call `_cts.Cancel()` and `_cts.Dispose()`, create a replacement `CancellationTokenSource`, and set `IsResolving` to `false` before starting the new resolution task.
4. WHEN a `CancellationToken` is cancelled during an async resolution loop, THE `OptimizedStreamingViewModel` SHALL catch `OperationCanceledException`, SHALL NOT set `IsPlaybackError` to `true`, and SHALL NOT call `Debug.WriteLine` for the cancellation.
5. THE `OptimizedStreamingViewModel` SHALL be registered as a singleton property named `StreamingViewModel` on `AppServices` so that the tray and player views share the same instance.
6. THE `OptimizedStreamingViewModel` SHALL expose an `[ObservableProperty]` `bool IsResolving` field that is set to `true` at the start of any resolution task and is set to `false` in the `finally` block of that task, regardless of outcome.

---

### Requirement 4: VPN-Aware Location Engine

**User Story:** As a user who routes traffic through a VPN, I want the application to detect my active network's country so that region-restricted content is correctly resolved.

#### Acceptance Criteria

1. WHEN `AntiGravityLocationEngine.GetCountryCodeAsync()` is called, THE `AntiGravityLocationEngine` SHALL issue an HTTP GET request to `https://ipapi.co/json/` and extract the `country_code` field from the JSON response body.
2. THE `AntiGravityLocationEngine` SHALL parse the `country_code` value from the raw response using `ReadOnlySpan<char>` stack-allocated character scanning, without allocating intermediate `string` or `JsonDocument` heap objects on the hot parse path.
3. THE `AntiGravityLocationEngine` SHALL cache the resolved country code string for exactly 15 seconds from the time of successful retrieval, measured by comparing `Environment.TickCount64` timestamps using `Interlocked.Read`.
4. WHEN the cached country code is still valid (within 15 seconds of the last successful retrieval), THE `AntiGravityLocationEngine` SHALL return the cached value immediately without issuing a new HTTP request.
5. IF the HTTP request to `https://ipapi.co/json/` fails, returns a non-success status, or the parsed `country_code` field is absent, null, or empty, THEN THE `AntiGravityLocationEngine` SHALL return the string `"US"` as a safe fallback, SHALL NOT cache the fallback value, and SHALL NOT throw an exception to the caller.
6. THE `AntiGravityLocationEngine` SHALL use `Interlocked` primitives exclusively for reading and writing the cache-expiry timestamp, with no `lock`, `SemaphoreSlim`, or `async` mutex involved in the cache check path.
7. THE `country_code` value returned by `AntiGravityLocationEngine.GetCountryCodeAsync()` SHALL be a two-character ISO 3166-1 alpha-2 string (e.g., `"US"`, `"GB"`, `"DE"`); if the parsed value does not match this format, it SHALL be treated as absent and the `"US"` fallback SHALL apply.

---

### Requirement 5: YouTube Native Stream Extraction

**User Story:** As a user, I want YouTube videos to play back at the highest available quality without a browser rendering engine so that playback is smooth and lightweight.

#### Acceptance Criteria

1. WHEN `SelectStreamAsync(StreamEntry entry)` is called with a `StreamEntry` whose platform is `YouTube`, THE `OptimizedStreamingViewModel` SHALL call `YoutubeClient.Videos.Streams.GetManifestAsync(entry.ResolutionTarget, cancellationToken)` on a background thread via `Task.Run`.
2. THE `OptimizedStreamingViewModel` SHALL select the `MuxedStreamInfo` entry with the highest bitrate from the manifest; if no `MuxedStreamInfo` is present, it SHALL fall back to the highest-bitrate `VideoOnlyStreamInfo` URL and pass both URLs to the `NativeDirectStreamPlayer` for separate audio/video stream playback.
3. IF the manifest returned by `GetManifestAsync` contains no `MuxedStreamInfo` and no `VideoOnlyStreamInfo`, THEN THE `OptimizedStreamingViewModel` SHALL set `IsPlaybackError` to `true` and SHALL log `"YouTube: no playable stream found for {videoId}"` via `Debug.WriteLine`.
4. IF `YoutubeClient` throws a `VideoUnavailableException` or any `HttpRequestException` during extraction, THEN THE `OptimizedStreamingViewModel` SHALL set `IsPlaybackError` to `true` and SHALL log the exception message via `System.Diagnostics.Debug.WriteLine`.
5. WHEN the extraction `CancellationToken` is signalled before `GetManifestAsync` completes, THE `OptimizedStreamingViewModel` SHALL catch `OperationCanceledException`, set `IsResolving` to `false` in the `finally` block, and discard the partial result without setting `IsPlaybackError`.

---

### Requirement 6: Twitch Live HLS Manifest Resolution

**User Story:** As a user, I want live Twitch channels to play back natively at source quality so that the stream is uninterrupted and low-latency.

#### Acceptance Criteria

1. WHEN `SelectStreamAsync(StreamEntry entry)` is called with a `StreamEntry` whose platform is `Twitch`, THE `OptimizedStreamingViewModel` SHALL POST a `PlaybackAccessToken` GraphQL query body to `https://api.twitch.tv/gql` with the `Client-ID` header set to the value stored in `AppServices.TwitchClientId`.
2. THE `OptimizedStreamingViewModel` SHALL deserialise the GraphQL response using the compile-time `StreamingSerializerContext` to extract the `value` (token) and `signature` fields without using reflection-based JSON parsing at runtime.
3. IF the deserialised `value` or `signature` field is null or empty, THEN THE `OptimizedStreamingViewModel` SHALL set `IsPlaybackError` to `true`, log `"Twitch: null or empty token/signature for {channelName}"` via `Debug.WriteLine`, and SHALL NOT attempt to construct the HLS URL.
4. WHEN non-null, non-empty `value` and `signature` fields are obtained, THE `OptimizedStreamingViewModel` SHALL construct the HLS playlist URL using the template `https://usher.ttvnw.net/api/channel/hls/{channel_name}.m3u8?token={token}&sig={sig}&allow_source=true` and SHALL pass the resulting URL to the `NativeDirectStreamPlayer`.
5. IF the Twitch GraphQL POST returns a non-success HTTP status code, THEN THE `OptimizedStreamingViewModel` SHALL set `IsPlaybackError` to `true`, log the status code via `Debug.WriteLine`, and SHALL NOT attempt to construct the HLS URL.
6. THE `OptimizedStreamingViewModel` SHALL pass the active `CancellationToken` to the `HttpClient.SendAsync` call for the GraphQL POST so that switching channels mid-resolution immediately aborts the in-flight request.

---

### Requirement 7: Source-Generated JSON Serialization

**User Story:** As a developer, I want all network model types to use compile-time JSON serialization so that the application is Native AOT-compatible and avoids reflection overhead at runtime.

#### Acceptance Criteria

1. A `partial` class named `StreamingSerializerContext` SHALL be declared under `Models/Streaming/`, decorated with `[JsonSourceGenerationOptions]` and `[JsonSerializable]` attributes covering: `TwitchGqlRequest`, `TwitchGqlResponse`, `TwitchPlaybackAccessToken`, `StreamEntry`, and `IReadOnlyList<StreamEntry>`.
2. THE `OptimizedStreamingViewModel` SHALL pass `StreamingSerializerContext.Default` to every `JsonSerializer.Serialize` and `JsonSerializer.Deserialize` call involved in Twitch resolution, with no reflection-based overloads.
3. THE `StreamingSerializerContext` SHALL be declared `partial` and SHALL reside in `Models/Streaming/StreamingSerializerContext.cs`.
4. THE `StreamEntry` record SHALL be annotated with `[JsonPropertyName]` attributes on all properties to guarantee stable wire-format compatibility with cached or persisted entries.

---

### Requirement 8: NuGet Dependency Provisioning

**User Story:** As a developer, I want the required NuGet packages declared in the project file so that any contributor can restore and build without manual package installation.

#### Acceptance Criteria

1. THE `FluentMediaPlayer.csproj` SHALL include a `<PackageReference>` for `LibVLCSharp.WinUI` at version `3.9.0` or higher.
2. THE `FluentMediaPlayer.csproj` SHALL include a `<PackageReference>` for `YoutubeExplode` at version `6.4.0` or higher.
3. WHEN the project is restored with `dotnet restore`, THE build pipeline SHALL resolve both `LibVLCSharp.WinUI` and `YoutubeExplode` without version conflicts against `CommunityToolkit.Mvvm 8.4.0` and `Microsoft.WindowsAppSDK 2.2.0`.
4. THE `FluentMediaPlayer.csproj` SHALL NOT include a `<PackageReference>` for `VideoLAN.LibVLC.Windows`, as `LibVLCSharp.WinUI` declares this transitive dependency.

---

### Requirement 9: Memory Safety and Unmanaged Resource Lifecycle

**User Story:** As a user running the application for extended sessions, I want the player to cleanly release GPU and system RAM whenever I switch tabs or close the stream tray so that memory does not grow unboundedly.

#### Acceptance Criteria

1. WHEN the `NativeDirectStreamPlayer` `Unloaded` event fires, THE `NativeDirectStreamPlayer` SHALL call `MediaPlayer.Stop()` before `MediaPlayer.Dispose()` on non-null instances to ensure the VLC decode thread exits cleanly before the pointer is freed.
2. WHEN the `OptimizedStreamingViewModel.IsStreamActive` property changes to `false` while the `NativeDirectStreamPlayer` is the active view, THE `NativeDirectStreamPlayer` SHALL execute `MediaPlayer.Stop()`, `MediaPlayer.Dispose()`, and `Media.Dispose()` on non-null instances, then set both fields to `null`.
3. THE `OptimizedStreamingViewModel` SHALL hold a single `CancellationTokenSource` field `_cts` and SHALL call `_cts.Cancel()` followed by `_cts.Dispose()` before assigning a replacement instance, preventing handle leaks across rapid user selections.
4. IF `MediaPlayer.Dispose()` throws any exception, THEN THE `NativeDirectStreamPlayer` SHALL catch it in a `try/catch`, log it via `Debug.WriteLine`, and continue execution to ensure `Media.Dispose()` is still called and fields are set to `null`.
5. THE `NativeDirectStreamPlayer` SHALL set `_mediaPlayer` and `_media` to `null` after their respective `Dispose()` calls to prevent double-free on any subsequent `Unloaded` re-entry.
6. WHEN playback is stopped and disposed, THE `NativeDirectStreamPlayer` SHALL set `IsPlaybackError` to `false` and notify the `OptimizedStreamingViewModel` so the tray UI reflects the idle state.
