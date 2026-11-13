using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Helpers;

public static class MemoryTrimHelper
{
    private static int _isTrimming;

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

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
                // Non-blocking Gen 2 collection without LOH compaction
                GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: false);
                try
                {
                    EmptyWorkingSet(Process.GetCurrentProcess().Handle);
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
