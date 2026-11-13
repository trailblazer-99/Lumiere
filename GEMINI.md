# 🛡️ Lumière Media Player — Architectural Guidelines & Workspace Rules

This repository implements **Lumière Media Player**, a native WinUI 3 / .NET 10 desktop media player for Windows 10/11.

---

## Key Subsystems & Architectural Rules

### 1. Direct Docking Video Presentation
- On `VideoPage` in windowed mode, `GlobalVideoPlayer` must be docked directly inside `VideoPlayerHost.Children.Insert(0, GlobalVideoPlayer)`.
- Never attempt to float `FloatingVideoContainer` over `VideoPage` via margin tracking; native XAML visual tree layout handles positioning and resizing without coordinate lag or navigation rail occlusion.
- In fullscreen mode, `GlobalVideoPlayer` transfers to `FloatingVideoContainer` for composition morphing, then docks back into `VideoPlayerHost` upon exit.

### 2. Symmetrical Lifecycles & Memory Management
- Every event subscription (`+=`) must have an exact unsubscription (`-=`):
  - ViewModels implementing service hooks must implement `IDisposable`.
  - Pages must unhook in `OnNavigatedFrom` or `Unloaded`.
- Wrap `ManagementObjectSearcher` in a `using` statement to cleanly dispose WMI/COM handles.

### 3. Concurrency & Async Contracts
- Mutate `ObservableCollection<T>` and UI controls strictly on the UI thread via `DispatcherQueue.TryEnqueue`.
- Zero rogue `async void` methods: only bounded UI event handlers with top-level `try-catch` may be `async void`.
- Thread-safe collections: use `ConcurrentDictionary` and `lock (_lock)` snapshots.

### 4. Build & Testing Standards
- Release x64 compilation: 0 warnings, 0 errors.
- Unit tests: 394/394 pass (`dotnet test Tests/LumiereMediaPlayer.Tests/LumiereMediaPlayer.Tests.csproj -c Release /p:Platform=x64`).
- Sentinel health suite: 34/34 pass (`tools/MonitoringKit/AppHealthCheck.ps1 -Quick`).
