# LumiereMediaPlayer

LumiereMediaPlayer is a premium, feature-rich media player for Windows, designed using modern WinUI 3 controls and Windows App SDK. It delivers a fast, fluid, and immersive audio and video experience.

## Features

- **Local Media Playback**: Scan and catalog local media library folders (audio & video) with automatic metadata scanning via TagLib.
- **Audio Pipelines**: Advanced audio controls including a multi-band equalizer, volume normalization, bass boost, gapless playback, and crossfade capabilities.
- **HDR Video Processing**: High dynamic range (HDR) handling and screen rendering management.
- **Integrated Streaming Pages**:
  - **YouTube**: In-app video search and playback.
  - **Twitch**: Live stream explorer and VOD player using a custom embedded WebView2 engine.
  - **Movies & TV Shows**: Popular listings, search, watch provider details (integrated with TMDB and Watchmode APIs).
  - **Music**: Dynamic streaming links integration using MusicAPI and iTunes catalogs.
- **Modern Fluent UI Design**: Mica and Acrylic backdrops, customizable accent colors, animated transitions, layout density controls, and dynamic hover glow effects.

---

## Getting Started

### Prerequisites

- **Windows 10 / 11** (19H1 or newer)
- **Visual Studio 2026** with the following workloads:
  - **.NET Desktop Development**
  - **Windows Application Development** (Windows App SDK / WinUI 3 templates)
- **.NET 8.0 SDK**

### Setup Instructions

1. **Clone the Repository**
   ```bash
   git clone https://github.com/your-username/LumiereMediaPlayer.git
   cd LumiereMediaPlayer
   ```

2. **Configure API Keys**
   The application uses separate APIs for TMDB, Watchmode, and YouTube. These are decoupled from the codebase for security.
   - Copy the configuration template:
     ```bash
     copy appsettings.json.example appsettings.json
     ```
   - Open `appsettings.json` and insert your personal API keys:
     ```json
     {
       "TmdbApiKey": "your-key-here",
       "WatchmodeApiKey": "your-key-here",
       "MotnApiKey": "your-key-here",
       "MusicApiKey": "your-key-here",
       "YouTubeApiKey": "your-key-here",
       "TwitchClientId": "your-client-id-here",
       "TwitchClientSecret": "your-secret-here"
     }
     ```
     *(Note: The app will fall back to default public keys if this file is not present or keys are empty).*

3. **Build and Run**
   - Open the solution file `LumiereMediaPlayer.slnx` or `LumiereMediaPlayer.csproj` in Visual Studio 2022.
   - Set the build configuration to `Debug` or `Release` and architecture to `x64` or `ARM64`.
   - Press **F5** to build and run the application.

---

## Project Maintenance

### Git Configuration
- Local config files (`appsettings.json`), compiled build outputs (`bin/`, `obj/`), and developer cache directories (`.vs/`) are automatically ignored via `.gitignore`. Do not commit raw secrets to the repository.

### CI/CD
- A GitHub Actions workflow (`.github/workflows/build.yml`) compiles the project on every push/pull-request to the `main` or `dev` branches to verify compilation integrity.
