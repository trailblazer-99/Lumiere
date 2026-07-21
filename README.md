# Lumière Media Player

[![Framework](https://img.shields.io/badge/Framework-.NET%2010.0-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![UI Library](https://img.shields.io/badge/UI-WinUI%203%20%7C%20Windows%20App%20SDK-0078D4.svg?style=flat-square&logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Design System](https://img.shields.io/badge/Design-Fluent%20Design-0078D4.svg?style=flat-square)](https://fluent2.microsoft.com/)
[![License](https://img.shields.io/badge/License-GPLv3-blue.svg?style=flat-square)](LICENSE)
[![CI/CD](https://img.shields.io/badge/DevOps-Azure%20%26%20GitHub%20Hybrid-0078D4.svg?style=flat-square&logo=azure-devops)](docs/AZURE_DEVOPS_MIGRATION.md)

**Lumière Media Player** is an enterprise-grade, high-performance native Windows desktop audio and video player built with **WinUI 3**, **C#**, and the **Windows App SDK**. Designed in strict accordance with the latest **Microsoft Fluent Design System** (Mica/Acrylic materials, rounded corners, theme shadows, compact density, and smooth micro-interactions), Lumière combines a local media library engine, queue/playlist management, HDR-aware video rendering (HDR10, HLG, Dolby Vision), and an API-backed streaming discovery catalog into a unified desktop experience.

Lumière operates on a **Zero-Trust Cloud Architecture**: all local playback and media file parsing stay entirely on the user's device, while sensitive external discovery APIs (TMDB, Watchmode, MusicAPI, Movie of the Night) are proxied through a serverless, zero-cost Azure Function backend (**LumiereProxy**). API credentials are never compiled or shipped inside distributed client binaries.

---

## 🌟 Key Features & Architecture

### 🎵 1. Local Media & Audio Engine
- **Folder Scanning & Metadata Parsing**: Recursively scans user-designated folders and extracts metadata, album art, sample rates, and bitrates using `TagLibSharp`.
- **Playback Controls**: Full play, pause, frame-accurate seeking, skip intervals (5s, 10s, 30s), custom playback speed (0.25x to 4.0x), shuffle, repeat (None, One, All), and seamless queue progression.
- **Advanced DSP & Audio Processing**:
  - 10-Band Graphic Equalizer with curated presets (Flat, Bass Boost, Rock, Pop, Classical, Vocal Boost).
  - Crossfade transition duration control (0 to 10 seconds).
  - Gapless playback engine for continuous audio tracks.
  - Audio Balance slider (Stereo L/R pan).
  - Mono Audio mixdown toggle for accessibility and single-earpiece listening.
  - Dynamic Volume Normalization and Peak Limiting.

### 🎬 2. Video & Display Engine (HDR & Dolby Vision)
- **Hardware-Accelerated Video Pipeline**: Native Windows Media engine leveraging Direct3D 11/12 GPU acceleration.
- **Display-Aware HDR Handling**:
  - Automatic hardware detection of Windows HDR-capable displays.
  - Full HDR10, Hybrid Log-Gamma (HLG), and Dolby Vision content output routing.
  - HDR Peak-Brightness tuning, Real-Time Tone Mapping, and SDR Fallback for legacy displays.
- **Dynamic Quality & Audio Badges**: Automatic visual badge rendering for **4K UHD**, **HD**, **SD**, **HDR**, **Dolby Vision**, **Dolby Atmos**, **Dolby Audio 5.1**, and **Surround Sound** based on stream metadata.
- **Custom Subtitle Engine**: Subtitle track selection, custom font scaling, background opacity control, and caption position adjustments.
- **Window Modes**: Fullscreen, Normal Windowed, and Picture-in-Picture (PiP / Compact Overlay) mode for multitasking.
- **Aspect Ratio Selector**: Auto, 16:9, 4:3, 21:9, and Stretch/Fill modes.

### 🌐 3. Streaming & Provider Discovery Engine
- **Native Deep-Link Router**: Seamlessly launches native Windows storefront apps for major streaming providers including **Netflix**, **Spotify**, **Disney+**, **Prime Video**, **Hulu**, **Max (HBO)**, **Paramount+**, **Peacock**, **Tubi**, **Pluto TV**, and **Apple TV**. Rent & Buy links and channel add-ons preserve clean HTTPS URLs with automatic browser fallbacks.
- **Movie & TV Discovery**: Explore trending, top-rated, and upcoming titles powered by Watchmode, TMDB, and Movie of the Night metadata.
- **Region-Aware Provider Catalog**: Displays subscription, rent, buy, and free streaming availability tailored by region (US, GB, CA, AU, etc.).
- **WebView2 Integration**: Embedded YouTube and Twitch browsing portals powered by Microsoft Edge WebView2 Runtime.
- **Persistent Local Watchlists**: Categorize titles into **Watchlist**, **Plan to Watch**, **Watching**, **Completed**, and **Favorites**, backed by local JSON persistence.
- **Music Search**: Integrated song search powered by the optional `LumiereProxy` API with iTunes fallback.

### ✨ 4. Fluent Design & Accessibility Suite
- **Modern Fluent Design System**: Built with Mica and Acrylic materials, subtle lighting, hover states, smooth micro-interactions, and visual transitions.
- **Interactive Settings Page**: Fine-tune crossfade duration, subtitle opacity, focus thickness, default volume, bass boost level, and audio balance via real-time interactive sliders.
- **Data & History Management**: Interactive one-click commands for clearing playback history, search history, recent files, and performing full factory resets.
- **Accessibility Suite**: High Contrast modes, large text scaling, reduced motion toggles, screen-reader optimizations, high-visibility keyboard focus indicators, and enlarged touch/click targets.

---

## 🏗️ Cloud Backend & Zero-Cost Architecture

```
 +----------------------------------+             +-----------------------------------+
 |   LUMIÈRE WINUI 3 DESKTOP APP    |             |      LUMIEREPROXY (AZURE)         |
 |  - Local Media Playback          |             |  - Serverless .NET 10 Function    |
 |  - Fluent UI & Video Engine      |  HTTPS/REST |  - Holds Secret API Keys          |
 |  - Passes 'APP_TOKEN' Header     | ----------->|  - TMDB / Watchmode / MusicAPI    |
 |  - Zero Embedded API Secrets     |             |  - 100% Free Tier ($0/month)      |
 +----------------------------------+             +-----------------------------------+
```

Lumière Media Player utilizes an optional serverless backend project (**`LumiereProxy`**) written as a .NET 10 isolated Azure Function. 

### Why Use a Proxy?
- **Zero API Key Leakage**: API credentials for third-party metadata services are stored as Environment Variables inside Azure Portal App Settings and never exposed to client binaries.
- **Zero Balance ($0/month) Cloud Cost**: Operates 100% within Azure's Perpetual Free Tier limits.

For complete Azure proxy deployment instructions, see [AZURE_PROXY_SETUP.md](docs/AZURE_PROXY_SETUP.md) and [AZURE_DEVOPS_MIGRATION.md](docs/AZURE_DEVOPS_MIGRATION.md).

---

## 💻 Technical Requirements & Prerequisites

| Component | Minimum Requirement | Recommended |
| --- | --- | --- |
| **Operating System** | Windows 10 Version 1809 (Build 17763) | Windows 11 Version 22H2 or newer |
| **SDK & Runtime** | .NET 10.0 SDK | .NET 10.0.302 SDK |
| **Tooling** | Visual Studio 2026 / Visual Studio 2022 | VS 2026 with WinUI 3 & .NET Desktop Workload |
| **Web Runtime** | Microsoft Edge WebView2 Runtime | Latest Evergreen WebView2 |
| **HDR Playback** | Standard SDR Display | HDR10 / Dolby Vision Display + WDDM 2.7+ GPU Driver |

---

## 🚀 Quick Start & Installation

For detailed step-by-step sideloading and certificate installation instructions, see [INSTALL.md](docs/INSTALL.md).

### Method 1: Web / App Installer (One-Click)
1. Download or click the `.appinstaller` file from the latest release page ([https://trailblazer-99.github.io/Lumiere/LumiereMediaPlayer.appinstaller](https://trailblazer-99.github.io/Lumiere/LumiereMediaPlayer.appinstaller)).
2. The native Windows App Installer dialog will launch.
3. Click **Install** or **Update**.

### Method 2: Sideloading `.msixbundle` / `.msix` Release Packages
1. **Enable Developer Mode**: Go to Windows **Settings** > **Privacy & security** > **For developers** > turn **Developer Mode** **ON**.
2. **Install Public Certificate**:
   - Right-click `LumiereMediaPlayer.cer` (located in `Signing/LumiereMediaPlayer.cer`) > select **Install Certificate**.
   - Select **Local Machine** > **Place all certificates in the following store** > choose **Trusted Root Certification Authorities**.
3. **Install Package**: Double-click `LumiereMediaPlayer.msixbundle` to install, or right-click `Add-AppDevPackage.ps1` and select **Run with PowerShell**.

---

## 🛠️ Building & Running from Source

### 1. Clone the Repository
```powershell
git clone https://github.com/trailblazer-99/Lumiere.git
cd Lumiere
```

### 2. Configure Local Application Settings
Create your local settings file from the example template:
```powershell
Copy-Item appsettings.json.example appsettings.json
```

Update `appsettings.json` to enable cloud proxy discovery:
```json
{
  "UseProxy": true,
  "ProxyBaseUrl": "https://LumiereProxy.azurewebsites.net/api",
  "ProxyAppToken": "your-app-token"
}
```
*(Note: `appsettings.json` is ignored by Git to prevent committing local keys to source control).*

### 3. Build via .NET CLI
Restore dependencies and compile the desktop app:
```powershell
# Restore NuGet packages
dotnet restore LumiereMediaPlayer.csproj

# Build Debug x64 package
dotnet build LumiereMediaPlayer.csproj -c Debug -p:Platform=x64

# Build Release x64 package
dotnet build LumiereMediaPlayer.csproj -c Release -p:Platform=x64
```

### 4. Build via Visual Studio
1. Open `LumiereMediaPlayer.slnx` (or `LumiereMediaPlayer.sln`) in Visual Studio 2026.
2. Select target configuration (`Debug` or `Release`) and architecture (`x64` or `ARM64`).
3. Press `F5` to build and launch.

---

## 📂 Repository Layout

```
Lumière Media Player/
├── docs/                          # 📁 Documentation Hub
│   ├── AZURE_DEVOPS_MIGRATION.md  # Azure DevOps Operations Guide
│   ├── AZURE_PROXY_SETUP.md       # Azure Proxy Architecture Guide
│   ├── INSTALL.md                 # Installation & Sideloading Guide
│   ├── LOCAL_BACKUP_GUIDE.md      # Local Fallback Backup Guide
│   ├── PRIVACY_POLICY.md          # Privacy Policy
│   └── PUBLISHING.md              # Publishing & Store Distribution
├── Assets/                        # Application branding, Fluent icons, and vector artwork
├── Controls/                      # Reusable UI controls (Transport, Queue, Media Cards)
├── Helpers/                       # Utilities (StreamingRouter, Deep-Link Parsers, Audio Helpers)
├── Models/                        # Data models (MediaItem, Playlist, QueueItem, Watchmode API)
├── Pages/                         # WinUI 3 Pages (Home, Library, Movies, TV, Settings, Streaming)
├── Properties/                    # Assembly metadata and PublishProfiles (win-x64.pubxml)
├── Services/                      # Application services (Playback, Audio DSP, HDR, Settings)
├── Signing/                       # Sideloading scripts and public certificate (LumiereMediaPlayer.cer)
├── Styles/                        # Fluent Design XAML dictionaries, brushes, and control templates
├── ViewModels/                    # MVVM ViewModel classes and RelayCommands
├── LumiereProxy/                  # .NET 10 Serverless Azure Function App backend
├── .github/workflows/             # GitHub Actions CI/CD workflows (build.yml)
├── azure-pipelines.yml            # Azure DevOps multi-stage CI/CD pipeline
├── LumiereMediaPlayer.csproj      # WinUI 3 Desktop Client Project File (.NET 10.0)
├── LICENSE                        # GNU GPLv3 License File
└── README.md                      # Project documentation landing page
```

---

## 🔄 CI/CD & DevOps Engineering

Lumière Media Player features a **Hybrid GitHub & Azure DevOps CI/CD Ecosystem**:

- **GitHub Actions ([.github/workflows/build.yml](.github/workflows/build.yml))**: Automated pull request checks, MSIX bundle creation, GitHub release creation, and GitHub Pages web installer deployment.
- **Azure DevOps Pipeline ([azure-pipelines.yml](azure-pipelines.yml))**: Multi-stage enterprise pipeline staging WinUI 3 MSIX artifacts, zip packaging Azure Functions backend drops, and managing automated Azure Cloud deployments under Azure's $0 Free Tier model.

---

## ⚖️ License

Lumière Media Player is free and open-source software licensed under the **[GNU General Public License v3.0 (GPLv3)](LICENSE)**.

```
Lumière Media Player - Native WinUI 3 Media Player
Copyright (C) 2026 Sourav / Lumière Media Player Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.
```
