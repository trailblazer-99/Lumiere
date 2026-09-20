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
