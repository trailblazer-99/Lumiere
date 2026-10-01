# Lumiere Media Player — Architecture Guide

A guide for contributors on how the codebase is organized and how the major components interact.

---

## Project Structure

```
LumiereMediaPlayer/
├── App.xaml(.cs)              # Application entry point, DI container setup
├── AppServices.cs             # Static service locator (bridge over DI — being phased out)
├── ServiceCollectionExtensions.cs # DI container registration
├── MainWindow.xaml(.cs)       # Main application window, hosts NavigationView + Frame
├── Models/                    # Data models (MediaItem, AppSettings, Playlist, etc.)
├── ViewModels/                # MVVM ViewModels (ObservableObject-based)
├── Services/                  # Business logic services
│   ├── Interfaces/            # Service interfaces for DI
│   ├── Display/               # HDR / advanced color management
│   └── Streaming/             # Streaming platform integrations (TMDB, Watchmode, etc.)
├── Helpers/                   # UI helpers, static utilities
├── Controls/                  # Custom WinUI 3 controls (TransportBar, MediaCard, etc.)
├── Pages/                     # Navigation pages (Home, Video, NowPlaying, Settings, etc.)
├── Styles/                    # Centralized XAML styles and design tokens
├── Assets/                    # App icons, splash screens
├── LumiereProxy/              # Azure Functions serverless proxy (API key protection)
├── Tests/                     # Unit test project
├── docs/                      # User-facing documentation (installation, privacy, etc.)
└── scripts/                   # Build/deployment scripts
```

---

## Dependency Injection

Services are registered in [`ServiceCollectionExtensions.cs`](../ServiceCollectionExtensions.cs) and the DI container is built in `App.xaml.cs`.

### Accessing Services

**New code** should use constructor injection:
```csharp
public class MyViewModel(ISettingsService settings, IPlaybackSession playback)
{
    // Use settings and playback directly
}
```

**Legacy code** still uses the static `AppServices` locator, which is a bridge over DI:
```csharp
// This works but is being phased out
var settings = AppServices.Settings;
```

### Service Lifetimes

| Registration | Lifetime | Examples |
|---|---|---|
| `Singleton` | One instance for app lifetime | `SettingsService`, `PlaybackSession`, `HdrPipelineService` |
| `Transient` | New instance per resolve | Page ViewModels (`HomeViewModel`, `VideoViewModel`, etc.) |

---

## MVVM Architecture

### ViewModels
- Located in `ViewModels/`
- Inherit from `CommunityToolkit.Mvvm.ObservableObject`
- Use `[ObservableProperty]` and `[RelayCommand]` source generators
- `PlaybackViewModel` is the central ViewModel — most other VMs depend on it

### Data Flow
```
User Interaction → Page/Control Code-Behind → ViewModel Command/Property
    → Service Layer (PlaybackSession, SettingsService, etc.)
        → Model Update → ObservableProperty notification → UI Update
```

### Key ViewModels
| ViewModel | Responsibility |
|---|---|
| `PlaybackViewModel` | Current track, play state, volume, position, queue |
| `HomeViewModel` | Recently played, quick actions |
| `VideoViewModel` | Video library, HDR state, metadata overlays |
| `SettingsViewModel` | All app settings, two-way bound to UI |
| `NowPlayingViewModel` | Album art, lyrics, visualizer data |

---

## Services Architecture

### Core Services
| Service | Interface | Responsibility |
|---|---|---|
| `PlaybackSession` | `IPlaybackSession` | Media playback engine, queue management, audio effects, crossfade |
| `SettingsService` | `ISettingsService` | Read/write app settings from Windows LocalSettings |
| `HistoryService` | `IHistoryService` | Playback history persistence |
| `HdrPipelineService` | `IHdrPipelineService` | HDR detection, tone mapping, brightness management |
| `AdvancedColorDisplayManager` | `IDisplayManager` | Display capability detection (HDR, WCG, luminance) |

### Streaming Services (in `Services/Streaming/`)
| Service | Responsibility |
|---|---|
| `TmdbService` | Movie/TV metadata from TMDB API |
| `WatchmodeService` | Streaming availability data |
| `MusicApiService` | Music streaming search and playback |
| `StreamingLibraryService` | User's saved streaming items |

### Proxy Architecture
API keys are **never** shipped in the client. Instead:
1. Client sends requests to the Azure Functions proxy (`LumiereProxy/`)
2. Proxy authenticates via `X-Lumiere-App-Token` header
3. Proxy adds the real API key and forwards to the upstream service
4. Response flows back through the proxy to the client

---

## Video, HDR & Color Grading Pipeline

Lumière implements a high-fidelity video processing and HDR tone-mapping architecture directly integrated with Windows Media Foundation (MF), Direct3D 11/12, and the Desktop Window Manager (DWM).

### Hardware Acceleration & Direct MPO Presentation
- **Direct Multi-Plane Overlay (MPO)**: The player operates in native presentation mode with `IsVideoFrameServerEnabled = false` and `RealTimePlayback = true` during HDR playback. Frames are composited directly on dedicated GPU hardware overlay planes, bypassing CPU system-memory readback and DWM composition queue latency.
- **Hybrid Multi-GPU Orchestration**: An asynchronous non-blocking WMI scan inspects `Win32_VideoController` to detect hybrid graphics configurations (e.g. AMD Radeon iGPU + NVIDIA RTX/GTX dGPU). For multi-GPU environments, cross-adapter DXGI shared-surface presentation is prioritized to preserve 10-bit P010 HDR color precision across PCIe buses.
- **Persistent Swapchain Architecture**: A single `GlobalVideoPlayer` element floats dynamically across the visual tree (docking into `VideoPage`'s `VideoPlayerHost` during library browsing and expanding into fullscreen mode), preventing tearing down or recreating native DirectX swapchains during window transitions.

### Windows Media Foundation Attributes (`mfobjects.h` Compliance)
Lumière strictly adheres to official Windows Media Foundation SDK specifications for color grading and video encoding properties:
- **Transfer Functions (`MF_MT_TRANSFER_FUNCTION`)**:
  - `MFVideoTransFunc_2084` (`15`): SMPTE ST 2084 Perceptual Quantizer (PQ) for HDR10 and Dolby Vision.
  - `MFVideoTransFunc_HLG` (`16`): ARIB STD-B67 Hybrid Log-Gamma for broadcast HDR streams.
  - `MFVideoTransFunc_709` (`5`): Standard ITU-R BT.709 transfer curve for SDR video.
  - `MFVideoTransFunc_2020` (`13`): BT.2020 SDR gamma curve for wide-color gamut SDR.
- **Color Primaries (`MF_MT_VIDEO_PRIMARIES`)**:
  - `MFVideoPrimaries_BT2020` (`9`): Wide-gamut Rec.2020 color primaries.
  - `MFVideoPrimaries_BT709` (`2`): Standard Rec.709 color primaries.
- **YUV-to-RGB Transfer Matrix (`MF_MT_YUV_MATRIX`)**:
  - `MFVideoTransferMatrix_BT2020_10` (`4`): ITU-R BT.2020 non-constant luminance transfer matrix.
  - `MFVideoTransferMatrix_BT709` (`1`): ITU-R BT.709 transfer matrix.
- **Nominal Range (`MF_MT_VIDEO_NOMINAL_RANGE`)**:
  - `MFNominalRange_16_235` / `MFNominalRange_Wide` (`2`): Broadcast studio limited range (16–235 for 8-bit SDR, 64–940 for 10-bit HDR), preserving absolute black levels and avoiding washed-out contrast.
  - `MFNominalRange_0_255` / `MFNominalRange_Normal` (`1`): Full PC/Data range (0–255 / 0–1023).
- **Tone Mapping Operator (`MF_VIDEO_TONEMAPPING_OPERATOR` `DE9AC8C9-9602-4A85-AA27-BCE095709DFF`)**:
  - Display-adaptive resolution via `AdvancedColorDisplayManager`:
    - `TrueHdrOledOrMiniLed` (>= 550 nits): Direct passthrough / Clip at screen peak (`3u`).
    - `EntryHdr` (350–550 nits): ITU-R BT.2408 highlight compression (`2u`).
    - `WideColorGamutSdr` & `StandardSdr`: BT.2408 SDR reference mapping (`2u`).
    - Manual overrides: ACES (`1u`), Reinhard (`0u`), BT.2408 (`2u`), Clip (`3u`).

### Asynchronous Track Demuxing & Race Prevention
When media sources open, video tracks may finish demuxing asynchronously after `MediaPlayer.MediaOpened` fires. `HdrPipelineService` subscribes to `MediaPlaybackItem.VideoTracksChanged` with automatic detection cache invalidation, ensuring that late-arriving video tracks immediately receive full HDR configuration and tone-mapping operator assignment without falsely falling back to SDR.

### Three-Tier Dynamic Brightness Escalation (`BrightnessOverrideHelper`)
During fullscreen HDR playback, Lumière automatically ramps display luminance to 100% and restores the user's prior level upon exit:
1. **Tier 0 (WinRT System API)**: `Windows.Graphics.Display.BrightnessOverride` for modern Windows laptops and tablets.
2. **Tier 1 (WMI Architecture)**: `WmiMonitorBrightness` via asynchronous background tasks for internal panels where WinRT override is unsupported.
3. **Tier 2 (Win32 DDC/CI)**: `dxva2.dll` (`GetMonitorBrightness`/`SetMonitorBrightness`) physical monitor I2C bus control for external desktop monitors.

---

## Styling System

All styles are centralized in [`Styles/MediaPlayerStyles.xaml`](../Styles/MediaPlayerStyles.xaml), loaded via `App.xaml`.

### Design Tokens
- **Spacing**: `SpacingSmall`, `SpacingMedium`, `SpacingLarge`
- **Corner Radius**: `CardCornerRadius`, `RoundCornerRadius`
- **Typography**: `DisplayTextStyle`, `SectionHeaderTextStyle`, `BodyTextStyle`

### Theme Support
- Full Dark/Light/System theme via `ThemeResource` semantic brushes
- Custom theme dictionaries for `Default`, `Light`, and `Dark`
- Video player background forced to pure black regardless of theme

### Animations
- Spring physics hover/press effects via `SpringAnimationHelper`
- Lottie animations for Play/Pause toggle
- WinUI staggered entrance transitions for lists

---

## Building & Running

### Prerequisites
- .NET 10 SDK
- Visual Studio 2022+ with WinUI 3 workload
- Windows 10 19041+ or Windows 11

### Development Build
```bash
dotnet build
```

### Configuration
Copy `appsettings.json.example` to `appsettings.json` and fill in your proxy URL and token. CI injects these automatically for releases.

---

## CI/CD

### GitHub Actions (`.github/workflows/build.yml`)
- **PR builds**: Format check → Restore → Build (verify compilation)
- **Release builds**: Version injection → Multi-arch MSBuild (x64 + ARM64) → MSIX Bundle → Code signing → GitHub Release → AppInstaller deployment

### Azure Pipelines (`azure-pipelines.yml`)
- WinUI 3 app build + Azure Functions proxy deployment
