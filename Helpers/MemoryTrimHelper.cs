using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Helpers;

public static class MemoryTrimHelper
{
    private static int _isTrimming;

    public static void TrimWorkingSet()
    {
        try
        {
            // 1. Trim LRU bitmap caches in ImageBindHelper
            ImageBindHelper.TrimCaches();

            // 2. Gentle non-blocking collection without hard page eviction
            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        }
        catch { }
    }

    public static void TrimWorkingSetAsync(int delayMs = 350)
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
                TrimWorkingSet();
            }
            finally
            {
                Interlocked.Exchange(ref _isTrimming, 0);
            }
        });
    }
}
