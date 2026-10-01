# Lumière Media Player - Monitoring Kit & Health Sentinel

## 1. Overview & Purpose

The **Lumière Monitoring Kit** is an automated, zero-regression health verification suite. It guards the entire application stack against architectural regressions, memory leaks, thread starvation, UI virtualization freezes, and unhandled crashes.

Whenever you or an AI assistant make changes to Lumière, running this kit guarantees that:
1. No memory leaks are introduced in ViewModels, XAML pages, composition visuals, or Win32 handles.
2. No UI thread freezes or virtualization bottlenecks are introduced.
3. No concurrency race conditions or unhandled `async void` crashes are committed.
4. All 82 unit and stress tests pass with 0 errors and 0 warnings under Release x64 compilation.

---

## 2. Quick Start

All tools are located in `tools/MonitoringKit/`.

### A. Pre-Change & Post-Change Verification
Before touching code, and after completing any feature or bug fix, run:
```powershell
# Full scan: Static analysis + Release x64 build + Unit test execution
.\tools\MonitoringKit\AppHealthCheck.ps1
```

### B. Fast Static Analysis (Instant Feedback)
If you only want to verify static rules (memory leaks, threading, bindings, security) in under 3 seconds without rebuilding:
```powershell
.\tools\MonitoringKit\AppHealthCheck.ps1 -Quick
```

### C. Build Check Without Unit Tests
```powershell
.\tools\MonitoringKit\AppHealthCheck.ps1 -SkipTests
```

### D. Real-Time Background File Watcher
Keep a terminal open while writing code to receive instant feedback whenever a `.cs`, `.xaml`, or `.csproj` file is saved:
```powershell
.\tools\MonitoringKit\WatchCodebase.ps1
```
*(To run the full compiler and test suite on each save, add `-IncludeTests`)*.

---

## 3. The 8 Audit Domains & Architectural Invariants

The sentinel automatically enforces 32 strict architectural rules across 8 domains:

### Domain 1: Concurrency & Thread-Safety
- **Rogue `async void` Prevention:**
  - Services, ViewModels, Models, and Helpers must **never** declare `async void` (they must return `Task` or `ValueTask`).
  - In UI code-behind (`*.xaml.cs`), `async void` is only permitted if it is a standard event handler (`object sender, ...EventArgs e`) OR is strictly guarded by a top-level `try { ... } catch` exception boundary.
- **Shared Service Caching:** Shared state (e.g., `WatchmodeService`) must use thread-safe collections (`ConcurrentDictionary`) to prevent race conditions during concurrent API calls.
- **Library Collection Snapshots:** `MediaLibraryService` must use lock-synchronized snapshotting (`lock (_lock)`) when exposing public track collections to prevent `Collection was modified` exceptions during background scans.

### Domain 2: Memory Leaks & Resource Teardown
- **ViewModel Event Subscriptions:**
  - Any ViewModel that subscribes to service events (`+=`) must implement `IDisposable` and detach (`-=`) in `Dispose()`.
  - Unmanaged/long-lived singleton events will hold transient ViewModels in memory indefinitely if not detached.
- **Composition Shadows & Glass Teardown:**
  - Custom composition elements like `GlassContainer` must explicitly detach child visuals (`SetElementChildVisual(this, null)`) upon `Dispose()`.
  - Virtualized card grids (`StreamingMoviesPage`, `StreamingTvShowsPage`, `StreamingMusicPage`) must detach `DropShadow` composition visuals in `Card_Unloaded`.
- **WMI & Hardware Handles:**
  - `ManagementObjectSearcher` queries in `HardwareDetectionService`, `HdrPipelineService`, and `BrightnessOverrideHelper` must be enclosed in `using` blocks to prevent COM memory exhaustion.
  - Win32 GDI/display handles (`HMONITOR`, `HDC`) must be released once query passes are finished.

### Domain 3: UI/UX Rendering, Virtualization & Jitter
- **Template Namescope Crossing:**
  - `{Binding ElementName=...}` is forbidden inside `DataTemplate`. It breaks template scope, forces WPF/WinUI into slow reflection fallback chains, and introduces frame stutter during fast scrolling.
- **Seek Slider Feedback Storm Guard:**
  - Media player sliders (`GlassTransportBar`, `TransportBar`) must feature programmatic update guards (`_isProgrammaticUpdate`) to prevent cyclical seek requests when position updates arrive from playback sessions.
- **ListView Item Virtualization:**
  - Multi-select track items must use standard `Click` handlers rather than `Checked`/`Unchecked` events. Binding `Checked` on recycled item templates triggers cascading selection storms when scrolling through large libraries.
- **Flyout Contrast:**
  - `CinematicMenuFlyoutPresenterStyle` and context popups must have opaque theme backgrounds (`{ThemeResource MenuFlyoutPresenterBackground}`) to ensure 100% legibility in both light and dark modes.

### Domain 4: Contract & Data Integrity
- **Unified Identity Key:**
  - `MediaItem` implements a unified identity key (`IdentityKey`) combining normalized paths and streaming IDs. Both `Equals()` and `GetHashCode()` are derived from this key, guaranteeing reflexive symmetry and hash table stability.

### Domain 5: Security & Network Protocols
- **Strict TLS Enforcement:**
  - All external network requests (TMDB, Watchmode, AntiGravity location services, GitHub update endpoints) must use `https://`.
  - Cleartext `http://` calls are restricted strictly to local developer daemons (e.g. `http://localhost:11434` for Ollama AI) or string prefix sanitizers.

### Domain 6: Update System & Crash Telemetry Policy
- **User-Prompted Updates:**
  - `UpdateService` alerts the user with an "Update Available" notification prompt instead of silently replacing running binaries in the background.
- **Bounded Crash Logging:**
  - Unhandled crash logs (`crash.txt` / `crash.log`) must be bounded with automated 2MB file rollover to `crash.bak.txt` to prevent disk starvation.

### Domain 7: Build & Compiler Verification
- **Zero Warnings, Zero Errors:**
  - `dotnet build LumiereMediaPlayer.csproj -c Release -p:Platform=x64` must produce `0 Warning(s)` and `0 Error(s)` under .NET 10.0 Windows SDK (`net10.0-windows10.0.19041.0`).

### Domain 8: Unit & Regression Suite
- **100% Test Pass Rate:**
  - All 82 unit tests in `Tests/LumiereMediaPlayer.Tests` must execute and pass without failure.

---

## 4. Diagnostics & Troubleshooting Playbook

When `AppHealthCheck.ps1` reports a failure, consult this table for the exact solution:

| Diagnostic Failure | Underlying Cause | Corrective Action |
|---|---|---|
| **ViewModel Event Lifecycle** | A ViewModel subscribes to an event with `+=` but lacks `IDisposable` or `Dispose()` detachment. | Add `IDisposable` to the class, store the `EventHandler` in a field, and unsubscribe with `-=` inside `Dispose()`. |
| **Rogue async void** | A non-UI method was declared `async void`, or a UI handler lacks a `try/catch` block. | Change the method signature to `async Task`. If in a UI event handler, wrap the method body in a `try { ... } catch (Exception ex)` block. |
| **ElementName inside DataTemplate** | A XAML `DataTemplate` attempts to bind to an element outside its scope with `{Binding ElementName=...}`. | Replace with ViewModel command delegation, attached properties, or relative element traversal. |
| **Compilation Error: Platform mismatch** | Running `dotnet test` or `dotnet build` without `-p:Platform=x64`. | Always supply `-p:Platform=x64` so output paths align with `bin\x64\Release\net10.0-windows10.0.19041.0\`. |
| **Insecure http:// endpoint** | A new remote API call was added using unencrypted HTTP. | Update the endpoint URL to use `https://`. |

---

## 5. Automated CI/CD Integration

To run the sentinel automatically on every pull request or push, add the following step to `.github/workflows/build.yml`:

```yaml
- name: Run Lumiere Codebase Sentinel
  shell: pwsh
  run: |
    .\tools\MonitoringKit\AppHealthCheck.ps1
```

If any check fails, the workflow terminates with an exit code of `1` and uploads `tools/MonitoringKit/reports/HealthReport_Latest.md` as an artifact.
