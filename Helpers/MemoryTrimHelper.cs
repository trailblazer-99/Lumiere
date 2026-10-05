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

    public static void TrimWorkingSet()
    {
        try
        {
            // 1. Trim LRU bitmap caches in ImageBindHelper
            ImageBindHelper.TrimCaches();

            // 2. Full generational collection with finalizers to release COM RCWs and unmanaged buffers
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            // 3. Reclaim unreferenced and clean physical memory pages from the process working set
            using var process = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(process.Handle, (IntPtr)(-1), (IntPtr)(-1));
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
