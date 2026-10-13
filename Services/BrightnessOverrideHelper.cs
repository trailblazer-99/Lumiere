using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Uses both Win32 DDC/CI (Dxva2.dll) and WMI (WmiMonitorBrightness) to boost
/// display brightness to 100 % during HDR playback and restore it afterwards.
/// This covers both laptop internal panels and external monitors.
/// </summary>
internal sealed class BrightnessOverrideHelper : IDisposable
{
    // ── Global persistent baseline tracking across app lifecycle ─────
    private static readonly object s_globalSyncLock = new();
    private static readonly SemaphoreSlim s_wmiGate = new(1, 1);
    private static byte? s_persistentBaselineWmi;
    private static uint? s_persistentBaselineDdc;
    private static bool s_isWmiOverrideActive;
    private static bool s_isDdcOverrideActive;

    // ── Instance state ──────────────────────────────────────────────
    private readonly object _stateLock = new();
    private IntPtr _physicalMonitorHandle;
    private IntPtr _lastHwnd;
    private uint _savedDdcBrightness;
    private byte _savedWmiBrightness;
    private bool _ddcOverrideActive;
    private bool _wmiOverrideActive;
    private bool _disposed;
    private volatile bool _overrideRequested;
    private CancellationTokenSource? _wmiCts;
    private Task? _lastWmiTask;
    private Task? _lastDdcTask;
    private Windows.Graphics.Display.BrightnessOverride? _winRtOverride;

    /// <summary>
    /// Boost the monitor's brightness to its maximum level.
    /// Returns <c>true</c> if either WinRT, WMI, or DDC/CI override was applied successfully.
    /// </summary>
    public bool TryOverrideToMax(IntPtr hwnd)
    {
        CancellationTokenSource cts;
        lock (_stateLock)
        {
            if (_disposed) return false;

            _lastHwnd = hwnd;
            _overrideRequested = true;

            // Cancel any pending WMI boost/restore operations for this helper
            try
            {
                _wmiCts?.Cancel();
                _wmiCts?.Dispose();
            }
            catch { }

            _wmiCts = new CancellationTokenSource();
            cts = _wmiCts;
        }

        bool success = false;
        try
        {
            // 0. Try WinRT BrightnessOverride (Tier 0 for modern laptops/tablets)
            try
            {
                var bo = Windows.Graphics.Display.BrightnessOverride.GetDefaultForSystem();
                if (bo != null && bo.IsSupported)
                {
                    bo.SetBrightnessLevel(1.0, Windows.Graphics.Display.DisplayBrightnessOverrideOptions.None);
                    bo.StartOverride();
                    lock (_stateLock)
                    {
                        _winRtOverride = bo;
                    }
                    success = true;
                    Debug.WriteLine("[HDR Brightness] Tier 0 WinRT BrightnessOverride applied: 100%");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] Tier 0 WinRT BrightnessOverride unsupported or failed: {ex.Message}");
            }

            // 1. Try WMI (for laptop internal displays)
            try
            {
                var token = cts.Token;
                _lastWmiTask = Task.Run(async () =>
                {
                    try
                    {
                        if (token.IsCancellationRequested) return;

                        // Only read baseline if no override is currently active
                        byte? currentWmi = null;
                        bool shouldQueryWmi;
                        lock (s_globalSyncLock)
                        {
                            shouldQueryWmi = !s_isWmiOverrideActive && !_wmiOverrideActive;
                        }

                        if (shouldQueryWmi)
                        {
                            currentWmi = await GetWmiBrightnessAsync(token).ConfigureAwait(false);
                        }

                        if (token.IsCancellationRequested) return;

                        if (!await s_wmiGate.WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false))
                        {
                            Debug.WriteLine("[HDR Brightness] WMI gate timeout during override attempt");
                            return;
                        }

                        try
                        {
                            if (token.IsCancellationRequested) return;

                            lock (_stateLock)
                            {
                                if (!_overrideRequested || _disposed || token.IsCancellationRequested)
                                {
                                    return;
                                }

                                lock (s_globalSyncLock)
                                {
                                    // Save baseline only if not currently overridden
                                    if (currentWmi.HasValue && !s_isWmiOverrideActive && !_wmiOverrideActive)
                                    {
                                        s_persistentBaselineWmi = currentWmi.Value;
                                        Debug.WriteLine($"[HDR Brightness] Captured persistent baseline WMI brightness: {s_persistentBaselineWmi.Value}%");
                                    }

                                    _savedWmiBrightness = s_persistentBaselineWmi ?? currentWmi ?? (byte)70;
                                }

                                if (SetWmiBrightness(100))
                                {
                                    // Verify we weren't cancelled while SetWmiBrightness was executing
                                    if (!_overrideRequested || _disposed || token.IsCancellationRequested)
                                    {
                                        // Immediately revert back to baseline!
                                        byte revertTarget = _savedWmiBrightness;
                                        SetWmiBrightness(revertTarget);
                                        _wmiOverrideActive = false;
                                        lock (s_globalSyncLock) { s_isWmiOverrideActive = false; }
                                        Debug.WriteLine($"[HDR Brightness] WMI override cancelled mid-flight, immediately reverted to {revertTarget}");
                                        return;
                                    }

                                    _wmiOverrideActive = true;
                                    lock (s_globalSyncLock)
                                    {
                                        s_isWmiOverrideActive = true;
                                    }
                                    Debug.WriteLine($"[HDR Brightness] WMI laptop override applied: baseline={_savedWmiBrightness} → 100");
                                }
                            }
                        }
                        finally
                        {
                            s_wmiGate.Release();
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[HDR Brightness] WMI override failed in background: {ex.Message}");
                    }
                }, token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] WMI override attempt failed: {ex.Message}");
            }

            // 2. Try DDC/CI (for external monitors)
            try
            {
                var targetHwnd = hwnd != IntPtr.Zero ? hwnd : GetDesktopWindow();
                if (targetHwnd != IntPtr.Zero)
                {
                    _lastDdcTask = Task.Run(() =>
                    {
                        try
                        {
                            var hMonitor = MonitorFromWindow(targetHwnd, MONITOR_DEFAULTTONEAREST);
                            if (hMonitor != IntPtr.Zero)
                            {
                                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                                {
                                    var monitors = new PHYSICAL_MONITOR[count];
                                    if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                                    {
                                        lock (_stateLock)
                                        {
                                            if (!_overrideRequested || _disposed)
                                            {
                                                DestroyPhysicalMonitors(count, monitors);
                                                return;
                                            }

                                            if (_physicalMonitorHandle != IntPtr.Zero)
                                            {
                                                try
                                                {
                                                    var oldMon = new[] { new PHYSICAL_MONITOR { hPhysicalMonitor = _physicalMonitorHandle } };
                                                    DestroyPhysicalMonitors(1, oldMon);
                                                }
                                                catch { }
                                                _physicalMonitorHandle = IntPtr.Zero;
                                            }

                                            _physicalMonitorHandle = monitors[0].hPhysicalMonitor;
                                            if (GetMonitorBrightness(_physicalMonitorHandle, out uint min, out uint current, out uint max))
                                            {
                                                lock (s_globalSyncLock)
                                                {
                                                    if (!s_isDdcOverrideActive && !_ddcOverrideActive)
                                                    {
                                                        s_persistentBaselineDdc = current;
                                                        Debug.WriteLine($"[HDR Brightness] Captured persistent baseline DDC/CI brightness: {s_persistentBaselineDdc.Value} (range {min}-{max})");
                                                    }

                                                    _savedDdcBrightness = s_persistentBaselineDdc ?? current;
                                                }

                                                if (SetMonitorBrightness(_physicalMonitorHandle, max))
                                                {
                                                    _ddcOverrideActive = true;
                                                    lock (s_globalSyncLock)
                                                    {
                                                        s_isDdcOverrideActive = true;
                                                    }
                                                    Debug.WriteLine($"[HDR Brightness] DDC/CI monitor override applied: baseline={_savedDdcBrightness} → {max} (range {min}–{max})");
                                                }
                                            }

                                            if (_ddcOverrideActive)
                                            {
                                                if (count > 1)
                                                {
                                                    var extraMonitors = new PHYSICAL_MONITOR[count - 1];
                                                    Array.Copy(monitors, 1, extraMonitors, 0, (int)count - 1);
                                                    DestroyPhysicalMonitors(count - 1, extraMonitors);
                                                }
                                            }
                                            else
                                            {
                                                DestroyPhysicalMonitors(count, monitors);
                                                _physicalMonitorHandle = IntPtr.Zero;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[HDR Brightness] DDC/CI override attempt failed: {ex.Message}");
                        }
                    });
                    success = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] DDC/CI override attempt failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] General override failed: {ex.Message}");
        }

        return success;
    }

    /// <summary>
    /// Restore the monitor brightness to the level saved before the override.
    /// </summary>
    public void Release()
    {
        CancellationTokenSource? ctsToCancel;
        bool shouldRestoreWmi;
        byte targetWmi;
        Windows.Graphics.Display.BrightnessOverride? winRtToStop = null;

        lock (_stateLock)
        {
            winRtToStop = _winRtOverride;
            _winRtOverride = null;
            shouldRestoreWmi = _wmiOverrideActive || _overrideRequested;
            _overrideRequested = false;

            ctsToCancel = _wmiCts;
            _wmiCts = null;

            lock (s_globalSyncLock)
            {
                targetWmi = s_persistentBaselineWmi ?? (_savedWmiBrightness > 0 ? _savedWmiBrightness : (byte)70);
                shouldRestoreWmi = shouldRestoreWmi || s_isWmiOverrideActive;
            }

            _wmiOverrideActive = false;
        }

        if (winRtToStop != null)
        {
            try
            {
                winRtToStop.StopOverride();
                Debug.WriteLine("[HDR Brightness] Tier 0 WinRT BrightnessOverride restored.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] Tier 0 WinRT StopOverride error: {ex.Message}");
            }
        }

        try
        {
            ctsToCancel?.Cancel();
            ctsToCancel?.Dispose();
        }
        catch { }

        // Restore WMI (internal display)
        if (shouldRestoreWmi)
        {
            byte safeTarget = Math.Clamp(targetWmi, (byte)0, (byte)100);
            _lastWmiTask = Task.Run(async () =>
            {
                try
                {
                    if (await s_wmiGate.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false))
                    {
                        try
                        {
                            lock (_stateLock)
                            {
                                if (_overrideRequested && !_disposed)
                                {
                                    return; // A new override request was made while waiting
                                }
                            }

                            if (SetWmiBrightness(safeTarget))
                            {
                                Debug.WriteLine($"[HDR Brightness] WMI laptop brightness restored → {safeTarget}");
                            }
                            lock (s_globalSyncLock)
                            {
                                s_isWmiOverrideActive = false;
                                s_persistentBaselineWmi = null; // Clear so subsequent media samples fresh system level
                            }
                        }
                        finally
                        {
                            s_wmiGate.Release();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HDR Brightness] WMI background restore error: {ex.Message}");
                }
            });
            Debug.WriteLine($"[HDR Brightness] WMI laptop brightness restore scheduled → {safeTarget}");
        }

        // Restore DDC/CI (external monitor)
        bool shouldRestoreDdc;
        uint targetDdc;
        IntPtr hPhysical;

        lock (_stateLock)
        {
            hPhysical = _physicalMonitorHandle;
            _physicalMonitorHandle = IntPtr.Zero;
            shouldRestoreDdc = _ddcOverrideActive;
            _ddcOverrideActive = false;

            lock (s_globalSyncLock)
            {
                shouldRestoreDdc = shouldRestoreDdc || s_isDdcOverrideActive;
                targetDdc = s_persistentBaselineDdc ?? _savedDdcBrightness;
                s_isDdcOverrideActive = false;
            }
        }

        if (shouldRestoreDdc)
        {
            IntPtr lastHwnd = _lastHwnd;
            if (lastHwnd == IntPtr.Zero && App.MainWindowInstance != null)
            {
                try { lastHwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance); } catch { }
            }

            _lastDdcTask = Task.Run(() =>
            {
                try
                {
                    if (hPhysical != IntPtr.Zero)
                    {
                        if (GetMonitorBrightness(hPhysical, out uint min, out uint cur, out uint max))
                        {
                            uint safeTarget = Math.Clamp(targetDdc, min, max);
                            if (SetMonitorBrightness(hPhysical, safeTarget))
                            {
                                Debug.WriteLine($"[HDR Brightness] DDC/CI monitor brightness restored to {safeTarget}");
                            }
                        }
                        var monitors = new[] { new PHYSICAL_MONITOR { hPhysicalMonitor = hPhysical } };
                        DestroyPhysicalMonitors(1, monitors);
                    }
                    else if (lastHwnd != IntPtr.Zero)
                    {
                        // Fallback in case physical handle was previously closed
                        var hMonitor = MonitorFromWindow(lastHwnd, MONITOR_DEFAULTTONEAREST);
                        if (hMonitor != IntPtr.Zero && GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                        {
                            var monitors = new PHYSICAL_MONITOR[count];
                            if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                            {
                                if (GetMonitorBrightness(monitors[0].hPhysicalMonitor, out uint min, out uint cur, out uint max))
                                {
                                    uint safeTarget = Math.Clamp(targetDdc, min, max);
                                    if (SetMonitorBrightness(monitors[0].hPhysicalMonitor, safeTarget))
                                    {
                                        Debug.WriteLine($"[HDR Brightness] DDC/CI fallback restored to {safeTarget}");
                                    }
                                }
                                DestroyPhysicalMonitors(count, monitors);
                            }
                        }
                    }

                    lock (s_globalSyncLock)
                    {
                        s_persistentBaselineDdc = null; // Clear so subsequent media samples fresh monitor level
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HDR Brightness] DDC/CI restore failed: {ex.Message}");
                }
            });
        }
    }

    public bool IsOverrideActive
    {
        get
        {
            lock (_stateLock)
            {
                return _ddcOverrideActive || _wmiOverrideActive || _overrideRequested || _winRtOverride != null;
            }
        }
    }

    public static bool IsAnyOverrideActive
    {
        get
        {
            lock (s_globalSyncLock)
            {
                return s_isWmiOverrideActive || s_isDdcOverrideActive;
            }
        }
    }

    internal static void ResetBaselineForTesting()
    {
        lock (s_globalSyncLock)
        {
            s_persistentBaselineWmi = null;
            s_persistentBaselineDdc = null;
            s_isWmiOverrideActive = false;
            s_isDdcOverrideActive = false;
        }
    }

    internal static byte? PersistentBaselineWmi
    {
        get
        {
            lock (s_globalSyncLock)
            {
                return s_persistentBaselineWmi;
            }
        }
    }

    internal static uint? PersistentBaselineDdc
    {
        get
        {
            lock (s_globalSyncLock)
            {
                return s_persistentBaselineDdc;
            }
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Release();

        WaitForPendingRestore(TimeSpan.FromMilliseconds(1000));
    }

    public void WaitForPendingRestore(TimeSpan timeout)
    {
        try
        {
            var tasks = new[] { _lastWmiTask, _lastDdcTask }.Where(t => t != null).ToArray();
            if (tasks.Length > 0)
            {
                Task.WaitAll(tasks!, timeout);
            }
        }
        catch { }
    }

    // ── WMI Helper Methods ────────────────────────────────────────────

    private static Task<byte?> GetWmiBrightnessAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested) return null;
            return GetWmiBrightness();
        }, cancellationToken);
    }

    private static byte? GetWmiBrightness()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness"));
            using var collection = searcher.Get();
            if (collection != null)
            {
                foreach (ManagementObject mObj in collection)
                {
                    using (mObj)
                    {
                        var val = mObj["CurrentBrightness"];
                        if (val != null)
                        {
                            return Convert.ToByte(val);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WMI Brightness] Get failed (hardware or WMI absent): {ex.Message}");
        }
        return null;
    }

    private static bool SetWmiBrightness(byte targetBrightness)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\wmi");
            scope.Connect();
            var path = new ManagementPath("WmiMonitorBrightnessMethods");
            using var mclass = new ManagementClass(scope, path, null);
            using var instances = mclass.GetInstances();
            if (instances != null)
            {
                bool success = false;
                foreach (ManagementObject instance in instances)
                {
                    using (instance)
                    {
                        try
                        {
                            using var inParams = instance.GetMethodParameters("WmiSetBrightness");
                            if (inParams != null)
                            {
                                inParams["Timeout"] = 0u;
                                inParams["Brightness"] = targetBrightness;
                                instance.InvokeMethod("WmiSetBrightness", inParams, null);
                                success = true;
                            }
                        }
                        catch (Exception exInner)
                        {
                            Debug.WriteLine($"[WMI Brightness] Instance InvokeMethod failed: {exInner.Message}");
                        }
                    }
                }
                return success;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WMI Brightness] Set failed (hardware or WMI absent): {ex.Message}");
            return false;
        }
        return false;
    }

    // ── DDC/CI P/Invoke ──────────────────────────────────────────────

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, out uint numberOfPhysicalMonitors);

    [DllImport("dxva2.dll")]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint physicalMonitorArraySize,
        [Out] PHYSICAL_MONITOR[] physicalMonitorArray);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitors(
        uint physicalMonitorArraySize,
        [In] PHYSICAL_MONITOR[] physicalMonitorArray);

    [DllImport("dxva2.dll")]
    private static extern bool GetMonitorBrightness(
        IntPtr hMonitor, out uint minimumBrightness,
        out uint currentBrightness, out uint maximumBrightness);

    [DllImport("dxva2.dll")]
    private static extern bool SetMonitorBrightness(
        IntPtr hMonitor, uint newBrightness);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }
}
