# 🛡️ Lumière Media Player — Developer Guidelines & Anti-Pattern Prevention

This guide defines the architectural standards, code contracts, and automated verification safeguards required to ensure Lumière remains stable, leak-free, and high-performance.

---

## 1. Automated Safeguards

### Pre-Commit Sentinel
The repository includes a Git pre-commit hook located at `.git/hooks/pre-commit`.
Before any commit is accepted, it automatically executes:
```powershell
.\tools\MonitoringKit\AppHealthCheck.ps1 -Quick
```
If any static rule fails (e.g. rogue `async void`, missing `IDisposable`, broken locking), the commit is blocked.

### Live Code Watcher
Keep the live watcher running during active development sessions:
```powershell
.\tools\MonitoringKit\WatchCodebase.ps1
```
It watches code changes in real-time and provides sub-2-second feedback on modified files.

### Architectural Unit Tests
Located in `Tests/LumiereMediaPlayer.Tests/Architecture/ArchitecturalHygieneTests.cs`:
- Enforces interface registrations in DI.
- Asserts that all stateful ViewModels implement `IDisposable`.
- Verifies that zero rogue `async void` methods exist outside UI event boundaries.
- Asserts equality and identity contracts on media models.

---

## 2. The 5 Golden Architectural Rules

### Rule 1: UI Thread Discipline
WinUI 3 controls and `ObservableCollection<T>` must **never** be mutated from background threads.
```csharp
// ✅ CORRECT:
var dispatcher = App.MainDispatcher ?? App.MainWindowInstance?.DispatcherQueue;
if (dispatcher?.HasThreadAccess == true)
{
    UpdateCollection();
}
else
{
    dispatcher?.TryEnqueue(UpdateCollection);
}

// ❌ WRONG:
// Mutating bound collections or UI controls directly from a Task.Run or background callback.
```

### Rule 2: Symmetrical Lifecycles & Memory Leak Prevention
Every event subscription (`+=`) must have an exact unsubscription (`-=`):
- **Pages**: Unhook handlers in `OnNavigatedFrom` or `Unloaded`.
- **ViewModels**: Any ViewModel that hooks `+=` on services must implement `IDisposable` and unhook in `Dispose()`.
- **Controls**: Detach progress pointer handlers (`RemoveHandler`), dwell timers, and composition brushes on `Unloaded`.
- **Animations**: Stop infinite storyboards (`RepeatBehavior="Forever"`) on navigation or unload.

### Rule 3: Synchronized Shared State
Never expose mutable collections across threads without synchronization:
- Use `lock (_lock)` for shared collections (`List<MediaItem>`).
- Return defensive copies (`lock (_lock) { return _items.ToList(); }`) or use `ConcurrentDictionary<TKey, TValue>`.
- Use atomic disk serialization: write to a temporary `.tmp` file and execute `File.Move(..., overwrite: true)`.

### Rule 4: Fault-Isolated Async Boundaries
- **No `async void` outside UI event handlers**: Non-UI methods must return `Task` or `ValueTask`.
- **UI event handlers**: Any `async void` method must have an outermost `try-catch` boundary.
- **Resilient sub-resource loading**: One failing optional endpoint (e.g. 404 on scores or trailer) must **never** fail the entire parent view. Use `SafeFetchAsync<T>`.

### Rule 5: Zero-Trust Security & App Packaging
- **Sanitize Proxy Routes**: Never pass unsanitized route parameters into upstream URLs. Reject `..`, `\\`, and control characters.
- **Constant-Time Verification**: Use `CryptographicOperations.FixedTimeEquals` for app token headers.
- **MSIX Folder Constraints**: The package installation directory (`Package.Current.InstalledLocation`) is strictly read-only. Dynamic and cached assets must be stored in `ApplicationData.Current.LocalFolder` or local AppData.
