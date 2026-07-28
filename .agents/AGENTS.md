# Lumière Media Player - Development Rules

Always adhere to these guidelines for all tasks in this project:
1. **Lead Developer Persona**: Act as the Software Engineering Lead for this project. Guide the architecture and maintain clean code patterns.
2. **C# & Native Focus**: Build exclusively with C# and native Windows patterns to ensure a high-performance, native Windows experience.
3. **Fluent Design System**: Strictly adhere to the latest Windows Fluent Design guidelines (Mica/Acrylic materials, modern typography, proper margins/padding, rounded corners, theme shadows, smooth micro-interactions, and visual transitions).

## 4. WinUI 3 Concurrency & Threading Guardrails
- **UI Thread Access (`AppWindow`)**: Never call `AppWindow.SetPresenter(...)` or modify window presenters from background threads or async continuations without wrapping in `DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => { ... })`.
- **No UI-Thread Garbage Collection**: Never call `System.GC.Collect()` or `System.GC.WaitForPendingFinalizers()` synchronously in UI event handlers (`OnUnloaded`, page navigation). Allow .NET generational GC to reclaim memory asynchronously.
- **`async void` Exception Safety**: Never override virtual navigation methods (`OnNavigatedTo`) or event handlers with unprotected `async void`. Always wrap the entire body of required `async void` handlers in an explicit `try-catch` block with diagnostic logging to prevent unhandled thread-pool crashes.
- **Startup & DWM Deadlock Protection**: Keep UI-thread initialization in `MainWindow` and `App.OnLaunched` lightweight and non-blocking. Always wrap display pipeline (HDR/color management) and DWM P/Invoke styling in `try-catch` blocks so window activation succeeds even if a native service fails.

## 5. WinUI 3 Memory & UI Performance Optimization
- **Bounded Bitmap Image Decoding**: Always specify explicit `DecodePixelWidth` (e.g., `240` for grid cards, `120` for thumbnails) when creating `BitmapImage(new Uri(url))` instances for remote posters or album art. Never decode full native 4K/1080p images into GPU/system memory.
- **In-Place Collection Updates**: When sorting or filtering `ObservableCollection` lists bound to `GridView`/`ListView` controls, check `SequenceEqual` first and use in-place slot updating (`Collection[i] = newItems[i]`) when counts match. Avoid `Clear()` followed by `foreach (.Add)` loops to prevent destroying and recreating visual tree item containers.
- **O(1) Library Deduplication**: When loading or scanning large media collections, always use `HashSet<string>` (case-insensitive) for seen paths/IDs to achieve O(N) deduplication rather than O(N²) `.Any(...)` scanning.
- **Timer & Visualizer Lifecycle**: Always check `!IsPlaying` inside high-frequency animation timers (e.g., 50 FPS audio visualizers) and return early when paused to conserve CPU and laptop battery.

## 6. Media Pipeline & Storage File Safety
- **MediaSource File Unlocking**: `MediaSource.CreateFromStorageFile` holds a shared read lock on physical files. Always call `.Reset()` and `.Dispose()` on `MediaSource` and set `MediaPlayer.Source = null;` before attempting to delete, rename, or edit ID3 tags of local media files.
- **Streaming API Resiliency**: All HTTP requests to external streaming APIs (TMDB, Watchmode, iTunes, Proxy) must use static `HttpClient` instances configured with explicit timeouts (10–15 seconds) or pass a `CancellationToken` so UI loading spinners never hang indefinitely during network outages.
