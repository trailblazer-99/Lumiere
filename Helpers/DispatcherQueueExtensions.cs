using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace LumiereMediaPlayer.Helpers;

/// <summary>
/// Bulletproof extensions for Microsoft.UI.Dispatching.DispatcherQueue.
/// Guarantees that synchronous and asynchronous delegates dispatched to the UI thread
/// cannot escape unhandled exceptions into CoreMessagingXP.dll or the WinUI message pump,
/// preventing 0xc000027b stowed exceptions and fatal CLR termination.
/// </summary>
public static class DispatcherQueueExtensions
{
    /// <summary>
    /// Safely enqueues a synchronous action with normal priority, catching and logging any thrown exceptions.
    /// </summary>
    public static bool SafeEnqueue(this DispatcherQueue? dispatcher, Action action, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        return SafeEnqueue(dispatcher, DispatcherQueuePriority.Normal, action, caller, file, line);
    }

    /// <summary>
    /// Safely enqueues a synchronous action with explicit priority, catching and logging any thrown exceptions.
    /// </summary>
    public static bool SafeEnqueue(this DispatcherQueue? dispatcher, DispatcherQueuePriority priority, Action action, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (dispatcher == null) return false;

        try
        {
            return dispatcher.TryEnqueue(priority, () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    var source = System.IO.Path.GetFileName(file);
                    App.LogCrash("DispatcherSafeEnqueue", $"[{source}:{line}] Exception in {caller}: {ex.Message}", ex.ToString());
                }
            });
        }
        catch (Exception ex)
        {
            var source = System.IO.Path.GetFileName(file);
            App.LogCrash("DispatcherTryEnqueueFail", $"[{source}:{line}] Could not enqueue {caller}: {ex.Message}", ex.ToString());
            return false;
        }
    }

    /// <summary>
    /// Safely enqueues an asynchronous Task-returning action with normal priority,
    /// ensuring any exceptions thrown before or after an await are captured and logged,
    /// preventing async void unhandled exceptions from escaping into CoreMessagingXP.
    /// </summary>
    public static bool SafeEnqueueAsync(this DispatcherQueue? dispatcher, Func<Task> asyncAction, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        return SafeEnqueueAsync(dispatcher, DispatcherQueuePriority.Normal, asyncAction, caller, file, line);
    }

    /// <summary>
    /// Safely enqueues an asynchronous Task-returning action with explicit priority,
    /// ensuring any exceptions thrown before or after an await are captured and logged.
    /// </summary>
    public static bool SafeEnqueueAsync(this DispatcherQueue? dispatcher, DispatcherQueuePriority priority, Func<Task> asyncAction, [CallerMemberName] string caller = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (dispatcher == null) return false;

        try
        {
            return dispatcher.TryEnqueue(priority, async () =>
            {
                try
                {
                    await asyncAction();
                }
                catch (Exception ex)
                {
                    var source = System.IO.Path.GetFileName(file);
                    App.LogCrash("DispatcherSafeEnqueueAsync", $"[{source}:{line}] Async exception in {caller}: {ex.Message}", ex.ToString());
                }
            });
        }
        catch (Exception ex)
        {
            var source = System.IO.Path.GetFileName(file);
            App.LogCrash("DispatcherTryEnqueueFail", $"[{source}:{line}] Could not enqueue async {caller}: {ex.Message}", ex.ToString());
            return false;
        }
    }
}
