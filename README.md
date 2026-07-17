# Lumière Media Player

Lumière Media Player is a Windows desktop audio and video player built with WinUI 3 and the Windows App SDK. It combines a local media library, queue and playlist management, configurable playback controls, HDR-aware video playback, and optional streaming discovery in one Fluent-style interface.

The application is designed to keep media playback local while using a small optional proxy service for API-backed discovery. It does not ship third-party API secrets in the desktop client.

## What it does

### Local media and playback

- Scans user-selected folders for audio and video files.
- Reads media metadata and artwork with TagLibSharp.
- Maintains a queue, playlists, playback history, recently played items, and resume positions.
- Supports play, pause, seeking, skip intervals, playback speed, shuffle, repeat, and automatic next-track behavior.
- Includes audio balance, mono audio, bass boost, volume normalization, equalizer presets, crossfade, and gapless-playback settings.

### Video and display features

- Hardware-accelerated Windows Media playback.
- Fullscreen and picture-in-picture/compact-overlay modes.
- Subtitle language, font-size, background-opacity, and caption controls.
- Configurable aspect ratios, including Auto, 16:9, 4:3, 21:9, and Fill.
- HDR detection and display-aware HDR output handling for HDR10, HLG, and Dolby Vision content where supported by Windows and the display.
- HDR tone mapping, peak-brightness, real-time playback, and HDR badge settings.

### Streaming and discovery

- YouTube and Twitch pages hosted through WebView2.
- Movie and TV discovery through Watchmode and TMDB-backed metadata.
- Region-aware provider and episode details for movies and shows.
- Music search through the optional MusicAPI proxy with iTunes fallback.
- External links for services such as Spotify, Apple Music, YouTube Music, Amazon Music, Tidal, and SoundCloud when available.
- Saved streaming items and watchlists stored locally under the user profile.

### Fluent desktop experience

- WinUI 3 navigation and Windows App SDK windowing.
- Mica/Acrylic-style backdrops, accent colors, animated transitions, compact density, media-card glow, and timeline preview settings.
- Accessibility options including high contrast, large text, reduced motion, screen-reader optimization, captions, color-blind modes, keyboard focus indicators, and larger click targets.

## Requirements

- Windows 10 version 1809 (build 17763) or newer; Windows 11 is recommended.
- .NET 10 SDK.
- Visual Studio 2026 with WinUI 3/Windows App SDK and .NET desktop development support, or the equivalent MSBuild and Windows SDK tooling.
- Microsoft Edge WebView2 Runtime for the YouTube and Twitch pages.
- A display and graphics driver with HDR support for HDR-specific features.

The main application targets `net10.0-windows10.0.19041.0` and currently supports x86, x64, and ARM64 project platforms. Release packaging is configured for x64 and ARM64.

## Get started

Clone the repository:

```powershell
git clone https://github.com/trailblazer-99/Lumiere.git
cd Lumiere
```

Create the local configuration file:

```powershell
Copy-Item appsettings.json.example appsettings.json
```

For proxy-backed discovery, update `appsettings.json`:

```json
{
  "UseProxy": true,
  "ProxyBaseUrl": "https://your-function-app.azurewebsites.net/api",
  "ProxyAppToken": "your-app-token"
}
```

`ProxyBaseUrl` should point to the deployed Azure Functions HTTP base route, and `ProxyAppToken` must match the token configured by the proxy. The local `appsettings.json` file is ignored by Git and should never contain production secrets committed to the repository.

Without a configured proxy, the local player remains available. Movie/TV metadata and MusicAPI discovery require the proxy; music search can fall back to iTunes. YouTube and Twitch are browser-based pages and use WebView2 directly.

## Build and run

### Visual Studio

1. Open `LumiereMediaPlayer.slnx`.
2. Select `Debug` or `Release`.
3. Select `x64` for standard Windows PCs or `ARM64` for Windows on ARM.
4. Press `F5`.

### .NET CLI

Restore the application dependencies:

```powershell
dotnet restore LumiereMediaPlayer.csproj
```

Build the application:

```powershell
dotnet build LumiereMediaPlayer.csproj -c Debug -p:Platform=x64
```

Build a Release package for x64 or ARM64:

```powershell
dotnet build LumiereMediaPlayer.csproj -c Release -p:Platform=x64
dotnet build LumiereMediaPlayer.csproj -c Release -p:Platform=ARM64
```

The generated executable is placed below `bin/<Platform>/<Configuration>/`. MSIX test packages and upload containers are written below `AppPackages/`.

## Packaging and signing

The project produces architecture-specific `.msix` packages and `.msixupload` containers. The upload container is intended for Store submission; a test-layout MSIX is produced for local deployment.

The repository defaults to unsigned package generation so a clean checkout can build without a developer-specific certificate. To create a signed local package, configure a certificate in Visual Studio or pass a certificate thumbprint available in the current user certificate store:

```powershell
dotnet build LumiereMediaPlayer.csproj `
  -c Release `
  -p:Platform=x64 `
  -p:AppxPackageSigningEnabled=true `
  -p:PackageCertificateThumbprint=<certificate-thumbprint>
```

The public certificate used by the project is in `Signing/LumiereMediaPlayer.cer`. Keep private keys and passwords out of source control. See `PUBLISHING.md` for Store packaging guidance and `.github/workflows/build.yml` for the automated release flow.

## Optional API proxy

`LumiereProxy/` is a separate .NET 10 isolated Azure Functions project. It keeps service credentials on the server and exposes the routes used by the desktop client for:

- TMDB metadata.
- Watchmode catalog, provider, season, and episode data.
- Movie of the Night data.
- MusicAPI search.

Deploy and configure it using [AZURE_PROXY_SETUP.md](AZURE_PROXY_SETUP.md). The proxy's application settings hold the service credentials; the desktop app only receives the proxy URL and app token.

## Repository layout

| Path | Purpose |
| --- | --- |
| `Pages/` | Home, library, playback, playlists, settings, and streaming pages |
| `ViewModels/` | MVVM state and commands for the UI |
| `Services/` | Playback, settings, history, HDR, metadata, and streaming services |
| `Controls/` | Reusable transport, queue, and media-card controls |
| `Models/` | Media, settings, playlist, queue, and streaming models |
| `LumiereProxy/` | Optional Azure Functions API proxy |
| `Assets/` and `Styles/` | Application artwork and Fluent UI resources |
| `Signing/` | Public certificate and sideloading assets |
| `AppPackages/` | Local build output; ignored by Git |

## Troubleshooting

### Streaming pages are empty

Check that `UseProxy` is `true`, `ProxyBaseUrl` is reachable, and `ProxyAppToken` matches the Azure Function's `APP_TOKEN`. Results can also vary by region and provider availability.

### YouTube or Twitch does not load

Install or repair the Microsoft Edge WebView2 Runtime, then restart the application. The pages depend on the external services and their current browser policies.

### HDR is unavailable

Confirm that Windows HDR is enabled, the display and GPU support HDR, and the media contains HDR metadata. The application can fall back to SDR when the display or content does not support HDR.

### A package says another version is already installed

Windows can block a packaged build when an unpackaged development copy is already registered. Remove the old development registration or launch the package from the matching Visual Studio configuration before installing the new MSIX.

## CI/CD

GitHub Actions runs on pushes and pull requests targeting `main`, `master`, or `dev`. The workflow restores dependencies, builds x64 and ARM64 packages, creates release artifacts, and publishes the deployment website for release builds.

## License

Lumière Media Player is distributed under the [Apache License 2.0](LICENSE).
