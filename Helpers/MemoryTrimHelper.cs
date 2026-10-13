using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Helpers;

public static class MemoryTrimHelper
{
    private static int _isTrimming;

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    public static void TrimWorkingSet(bool deep = false)
    {
        try
        {
            // 1. Trim LRU bitmap caches in ImageBindHelper
            ImageBindHelper.TrimCaches();

            // 2. Trim video thumbnail textures from playback session
            try
            {
                if (App.Services != null)
                {
                    AppServices.Playback?.TrimThumbnailCache(deep ? 0 : 20);
                }
            }
            catch { }

            // 3. Invalidate consolidated TV show view cache
            try
            {
                TvShowHelper.InvalidateConsolidatedCache();
            }
            catch { }

            // 4. Clean stale downloaded update packages
            try
            {
                Services.UpdateService.CleanStaleUpdatePackages();
            }
            catch { }

            // 5. Clean inactive unmanaged icon handles
            try
            {
                AppIconHelper.ClearIconHandles(keepActive: true);
            }
            catch { }

            if (deep)
            {
                // Deep trim: Collect Gen 2 and compact Large Object Heap (LOH)
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);

                try
                {
                    using var proc = Process.GetCurrentProcess();
                    SetProcessWorkingSetSize(proc.Handle, (IntPtr)(-1), (IntPtr)(-1));
                }
                catch { }
            }
            else
            {
                // Lightweight trim: Optimized Gen 1 collection
                GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
            }
        }
        catch { }
    }

    public static void TrimWorkingSetAsync(int delayMs = 350, bool deep = false)
    {
        if (Interlocked.CompareExchange(ref _isTrimming, 1, 0) != 0)
        {
            return; // Coalesce multiple rapid calls into a single trim run
        }

        Task.Run(async () =>
        {
            try
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
                TrimWorkingSet(deep);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MemoryTrimHelper] error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isTrimming, 0);
            }
        });
    }
}
