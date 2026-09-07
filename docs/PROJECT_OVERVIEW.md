# 🎬 Project Overview: Lumière Media Player

> **AI Context Prompt / System Note:**
> *This document provides a comprehensive technical, architectural, and operational specification of **Lumière Media Player**. It is structured for rapid ingestion by LLMs (such as Google Gemini) to understand the codebase, design patterns, services, data flow, and feature set.*

---

## 1. Executive Summary & Vision

**Lumière Media Player** is a modern, enterprise-grade, high-performance native Windows desktop audio and video player built with **WinUI 3**, **C#**, and the **Windows App SDK** targeting **.NET 10.0**.

Designed in strict accordance with the **Microsoft Fluent Design System** (Mica/Acrylic materials, rounded corners, subtle lighting, spring motion animations, and compact density), Lumière combines:
- A **hardware-accelerated local media playback engine** with Direct3D 11/12 rendering, advanced HDR tone mapping (HDR10, HLG, Dolby Vision), and a 10-band DSP graphic equalizer.
- A **Zero-Trust serverless cloud discovery architecture** powered by an isolated Azure Function backend (**LumiereProxy**) that proxies third-party metadata APIs without exposing credentials in client binaries.
- An **integrated multi-provider AI intelligence engine** using **Google Gemini** as the primary cognitive driver (with local Ollama and offline heuristic fallbacks) for semantic library search, multilingual lyrics translation, smart EQ categorization, and movie curation.

---

## 2. Technology Stack & Key Dependencies

| Domain | Technology / Component | Details & Usage |
| :--- | :--- | :--- |
| **Framework & Runtime** | **.NET 10.0** / C# 13 | High-performance modern managed runtime (Windows x64 and ARM64). |
| **User Interface** | **WinUI 3** / **Windows App SDK** | Fluent Design System 2, Mica Alt backdrops, Acrylic brushes, XAML styling. |
| **Video Engine** | **Direct3D 11/12 & Media Foundation** | GPU hardware decoding, custom swapchains, and subtitle rendering. |
| **HDR & Display** | **Win32 DDC/CI (`dxva2.dll`)** | DisplayInformation HDR detection, DDC/CI peak monitor brightness overrides. |
| **Audio Processing** | **TagLibSharp & Core Media API** | 10-band graphic EQ, crossfade transitions, gapless playback, stereo pan & mono downmix. |
| **Primary AI Engine** | **Google Gemini API** (v1 / v1beta) | `gemini-2.5-flash`, `gemini-2.0-flash`, and `gemini-1.5-flash` endpoints. |
| **Local AI Fallback** | **Ollama** (`http://localhost:11434`) | Offline local LLM support (`llama3.2`) with automatic failover. |
| **Web Integration** | **Microsoft Edge WebView2** | Embedded modern Chromium runtime for YouTube and Twitch portals. |
| **Cloud Proxy Backend**| **Azure Functions** (.NET 10 Isolated) | $0/month perpetual free-tier serverless proxy for TMDB, Watchmode, and MusicAPI. |
| **Security & Storage** | **Windows Hello & PasswordVault** | DPAPI-encrypted credential storage and biometric authentication. |
| **CI / CD DevOps** | **GitHub Actions & Azure DevOps** | Automated MSIX bundling, code signing, and `.appinstaller` distribution. |

---

## 3. Architecture & Data Flow Diagram

```
+---------------------------------------------------------------------------------------+
|                              LUMIÈRE WINUI 3 DESKTOP CLIENT                           |
|                                                                                       |
|  +---------------------------+  +---------------------------+  +-------------------+  |
|  |     Local Media Engine    |  |     Video / HDR Engine    |  |    AI Assistant   |  |
|  |  - TagLibSharp Metadata   |  |  - Direct3D 11/12 Engine  |  |  - Gemini 2.5/2.0 |  |
|  |  - 10-Band Graphic EQ     |  |  - Win32 DDC/CI Brightness|  |  - Ollama Fallback|  |
|  |  - Gapless & Crossfade    |  |  - Tone Mapping & Badges  |  |  - Semantic Search|  |
|  +---------------------------+  +---------------------------+  +-------------------+  |
|                                                                                       |
|  +---------------------------------------------------------------------------------+  |
|  |                Fluent Design UI Layer (Mica / Acrylic / Spring Motion)          |  |
|  +---------------------------------------------------------------------------------+  |
+---------------------------------------------------------------------------------------+
                                  │                                   │
              Direct Key (Header) │                                   │ Passes App-Token
                                  ▼                                   ▼
        +-------------------------------+           +-----------------------------------+
        |       Google Gemini API       |           |     LumiereProxy (Azure Function) |
        | (gemini-2.5-flash / 2.0-flash)|           | - Serverless .NET 10 Isolated App |
        +-------------------------------+           | - Holds TMDB, Watchmode, MOTN keys|
                                                    +-----------------------------------+
                                                                      │
                                                     +----------------─┴─────────────────+
                                                     ▼                                   ▼
                                                  [TMDB]                            [Watchmode]
```

---

## 4. Core Subsystems & Technical Details

### 🎵 1. Local Media & Audio Engine
- **Metadata Extraction (`MediaMetadataScanner`)**: Recursively indexes directories and parses ID3v2, FLAC Vorbis, MP4, and Opus metadata, sample rates, bitrates, channels, and album artwork using `TagLibSharp`.
- **Playback Session (`PlaybackSession`)**: Manages queue progression, shuffle, loop modes, frame-accurate seeking, and skip intervals (5s, 10s, 30s).
- **10-Band Graphic Equalizer**: Preamp and 10 frequency bands (31Hz, 62Hz, 125Hz, 250Hz, 500Hz, 1kHz, 2kHz, 4kHz, 8kHz, 16kHz) with presets (*Flat, Bass Boost, Rock, Pop, Classical, Vocal Boost*).
- **DSP Capabilities**:
  - Crossfade duration control (0 to 10 seconds).
  - Gapless playback for continuous live recordings and albums.
  - Stereo balance (L/R pan) and Mono mixdown toggle for single-ear accessibility.
  - Volume normalization and dynamic peak limiting.

### 🎬 2. Hardware-Accelerated Video & HDR Pipeline (`HdrPipelineService`)
- **Display Capability Detection**: Interrogates `AdvancedColorDisplayManager` and `DisplayInformation` for HDR10, HLG, and Dolby Vision support.
- **Dynamic DDC/CI Monitor Brightness Override**: Uses native Win32 `Dxva2.dll` calls to automatically ramp monitor brightness to 100% when HDR playback starts and restores user settings on exit.
- **Tone Mapping Operators**: Implements Media Foundation tone mapping (`ACES`, `Reinhard`, `BT.2408`, `Clip`) to guarantee vivid color representation and SDR fallback on standard displays.
- **Dynamic Stream Badges**: Real-time badge indicators for **4K UHD**, **HD**, **SD**, **HDR10**, **Dolby Vision**, **Dolby Atmos**, **Dolby 5.1**, and **Surround Sound**.
- **Windowing Modes**: Fullscreen, standard windowed, and Picture-in-Picture (PiP / Compact Overlay) with custom subtitle font scaling and background opacity.

### 🧠 3. Multi-Provider AI Intelligence Engine (`AiAssistantService`)
- **Three-Tier Fallback Hierarchy**:
  1. **Google Gemini (Primary)**: Dynamically checks available models (`gemini-2.5-flash`, `gemini-2.0-flash`, `gemini-1.5-flash`), passing credentials securely via `x-goog-api-key` headers or cloud proxy.
  2. **Local Ollama (Automatic Fallback)**: Automatically directs requests to `http://localhost:11434` (`llama3.2`) if the cloud connection is unavailable.
  3. **Local Offline Heuristics**: Levenshtein distance typo matching (distance <= 2), decade extractors (e.g. "90s", "80s"), and mood classifiers.
- **AI-Powered Capabilities**:
  - **Natural Language Semantic Library Search**: Translates queries like *"upbeat 80s rock for working out"* into ranked indices across the user's local music tracks.
  - **Settings Search**: Natural language routing for application preferences and audio configurations.
  - **Multilingual Lyrics Translation**: Translates synced lyrics into 13+ languages (Spanish, French, German, Japanese, Hindi, etc.) while maintaining timing, emotion, and format, with in-memory caching.
  - **Smart Equalizer Categorization**: Automatically detects genre and title nuances to set the EQ preset.
  - **Curated Film & TV Discovery**: AI-driven suggestions based on contextual descriptions and themes.

### 🌐 4. Streaming & Provider Discovery Engine
- **Native Streaming App Router (`StreamingRouter`)**: Protocol deep-linking router that launches native Windows Store apps for:
  - *Netflix, Spotify, Prime Video, Disney+, Apple TV, Max (HBO), Paramount+, Hulu, Crunchyroll, Peacock, Tubi, Pluto TV, JioCinema, Hotstar, Tidal, Deezer, Plex.*
  - Automatic fallback to browser URLs for rentals, purchases, and uninstalled storefronts.
- **WebView2 Portals**: Embedded Chromium browsing for YouTube and Twitch.
- **Persistent Local Watchlists**: Offline JSON storage categorized into *Watchlist, Plan to Watch, Watching, Completed,* and *Favorites*.

### 🔒 5. Zero-Trust Security & Cloud Proxy (`LumiereProxy`)
- **Serverless Architecture**: Isolated worker .NET 10 Azure Function app.
- **Zero Client Secret Exposure**: Third-party API keys (TMDB, Watchmode, MusicAPI, Gemini) are stored exclusively in Azure App Settings.
- **Authentication**: Client requests pass an HMAC / app token header (`X-Lumiere-App-Token`).
- **Windows Credential Locker**: Client-side user API keys are stored via Windows `PasswordVault` (DPAPI hardware-backed encryption).
- **Windows Hello**: Biometric authorization (Face/Fingerprint/PIN) for sensitive user actions.

---

## 5. Repository Structure & Map

```
Lumiere/
├── LumiereMediaPlayer.csproj   # Main WinUI 3 Desktop Project (.NET 10.0)
├── App.xaml / App.xaml.cs       # Application entry point & lifecycle
├── AppServices.cs              # Dependency Container & Lazy ViewModel Initializer
│
├── Controls/                   # Reusable WinUI 3 controls
│   ├── TransportBar.xaml       # Bottom media playback bar & seeking controls
│   ├── MediaCard.xaml          # Animated Fluent media cards
│   ├── SelectionRibbon.xaml    # Multi-item batch management bar
│   └── QueuePanel.xaml         # Active playlist & queue drawer
│
├── Pages/                      # WinUI 3 Navigation Pages
│   ├── HomePage.xaml           # Aggregated hub (recent, quick-play, trending)
│   ├── VideoPage.xaml          # Fullscreen HDR video rendering view
│   ├── NowPlayingPage.xaml     # Rich audio player with animated artwork & lyrics
│   ├── MusicLibraryPage.xaml   # Local audio library browser
│   ├── StreamingMoviesPage.xaml# Online film discovery & provider catalog
│   ├── StreamingTvShowsPage.xaml# Series discovery catalog
│   ├── StreamingMusicPage.xaml # Online music search
│   ├── StreamingYouTubePage.xaml # Embedded YouTube WebView2 portal
│   ├── StreamingTwitchPage.xaml  # Embedded Twitch WebView2 portal
│   └── SettingsPage.xaml       # Comprehensive DSP, display, AI, & theme settings
│
├── ViewModels/                 # MVVM ViewModel layer (ObservableObject & RelayCommands)
│   ├── PlaybackViewModel.cs    # Bridge between playback engine and UI controls
│   ├── HomeViewModel.cs        # Dashboard data aggregation
│   ├── SettingsViewModel.cs    # Two-way binding for settings & hardware state
│   └── Streaming*ViewModel.cs  # ViewModels for movies, shows, and music
│
├── Services/                   # Core business logic & hardware orchestration
│   ├── PlaybackSession.cs      # Core audio/video player session & queue engine
│   ├── HdrPipelineService.cs   # HDR detection, tone mapping, DDC/CI brightness
│   ├── AiAssistantService.cs   # Gemini / Ollama / Heuristic AI provider
│   ├── SettingsService.cs      # Local JSON settings persistence
│   └── Streaming/              # TMDB, Watchmode, & MusicAPI connectors
│
├── Helpers/                    # Utility classes
│   ├── StreamingRouter.cs      # Protocol URI handler for native streaming apps
│   ├── MediaMetadataScanner.cs # TagLibSharp local media file parsing
│   ├── ThemeHelper.cs          # Mica / Acrylic / Accent color controllers
│   └── WindowsHelloHelper.cs   # Biometric authentication wrappers
│
├── LumiereProxy/               # Azure Function serverless proxy backend (.NET 10)
│   └── LumiereProxy.cs         # Route handler for TMDB, Watchmode, Gemini, & MusicAPI
│
└── .github/ & azure-pipelines.yml # CI/CD automation for MSIX builds & deployments
```

---

## 6. Build, Run, & Debugging Instructions

### Prerequisites
- Windows 10 Version 1809 (Build 17763) or Windows 11 (22H2+ recommended)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/)
- Visual Studio 2026 / 2022 with **.NET Desktop Development** and **WinUI 3 / Windows App SDK** workloads
- Microsoft Edge WebView2 Runtime

### Build from Command Line (PowerShell)
```powershell
# 1. Clone the repository
git clone https://github.com/trailblazer-99/Lumiere.git
cd Lumiere

# 2. Configure local settings (optional for local proxy testing)
Copy-Item appsettings.json.example appsettings.json

# 3. Restore NuGet dependencies
dotnet restore LumiereMediaPlayer.csproj

# 4. Build Debug x64 package
dotnet build LumiereMediaPlayer.csproj -c Debug -p:Platform=x64

# 5. Build Release x64 package
dotnet build LumiereMediaPlayer.csproj -c Release -p:Platform=x64
```

---

## 7. Useful Prompts for Gemini Collaboration

When working with this codebase in Gemini, you can use these context-primed prompts:

* **Audio Engineering**: *"Review `PlaybackSession.cs` and `AudioPipelineHelper.cs`. How can we implement a WASAPI Exclusive Mode audio output sink for bit-perfect audiophile playback?"*
* **AI Subtitles**: *"In `AiAssistantService.cs`, help me add an offline Whisper.net speech-to-text pipeline to auto-generate video subtitles on-device."*
* **HDR & Video Rendering**: *"Inspect `HdrPipelineService.cs`. How can we optimize real-time ACES tone-mapping performance during 4K 60FPS HDR10 video playback?"*
* **UI/UX Performance**: *"Review `MusicLibraryPage.xaml` and `MediaCard.xaml`. Suggest XAML virtualization optimizations to handle libraries with over 50,000 tracks smoothly."*
