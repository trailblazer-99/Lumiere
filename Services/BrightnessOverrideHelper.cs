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
    private const string EmergencyBaselineSettingKey = "HdrBaselineBrightness";
    private static readonly object s_globalSyncLock = new();
    private static readonly SemaphoreSlim s_wmiGate = new(1, 1);
    private static readonly SemaphoreSlim s_ddcGate = new(1, 1);
    private static long s_lastDdcTimestamp;
    private const int MinDdcInterCommandDelayMs = 50;
    private static byte? s_persistentBaselineWmi;
    private static uint? s_persistentBaselineDdc;
    private static bool s_isWmiOverrideActive;
    private static bool s_isDdcOverrideActive;
    private static bool s_winRtUnsupported = true;
    private static bool s_wmiUnsupported = false;
    private static bool s_ddcUnsupported = false;

    // ── Instance state ──────────────────────────────────────────────
    private readonly object _stateLock = new();
    private IntPtr _physicalMonitorHandle;
    private IntPtr _lastHwnd;
    private uint? _savedDdcBrightness;
    private byte? _savedWmiBrightness;
    private bool _ddcOverrideActive;
    private bool _wmiOverrideActive;
    private bool _winrtOverrideActive;
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
        if (IsOverrideActive)
        {
            ReleaseSynchronous();
        }
    }

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

            // If already actively overridden to max, retain current override
            if (_overrideRequested && (_wmiOverrideActive || _ddcOverrideActive || _winRtOverride != null))
            {
                _lastHwnd = hwnd;
                return true;
            }

            _lastHwnd = hwnd;
            _overrideRequested = true;

            // Cancel any pending WMI restore operations for this helper
            try
            {
                _wmiCts?.Cancel();
                _wmiCts?.Dispose();
            }
            catch { }

            _wmiCts = new CancellationTokenSource();
            cts = _wmiCts;
        }

        try
        {
            // 0. Try WinRT BrightnessOverride (Official Windows 10/11 system brightness override API for laptops/tablets)
            try
            {
                var winrtOverride = Windows.Graphics.Display.BrightnessOverride.GetDefaultForSystem();
                if (winrtOverride != null && winrtOverride.IsSupported)
                {
                    winrtOverride.SetBrightnessLevel(1.0, Windows.Graphics.Display.DisplayBrightnessOverrideOptions.None);
                    winrtOverride.StartOverride();
                    _winrtOverrideActive = true;
                    success = true;
                    Debug.WriteLine("[HDR Brightness] WinRT system BrightnessOverride applied: 100%");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] WinRT BrightnessOverride attempt failed: {ex.Message}");
            }

            // 1. Try WMI (for laptop internal displays if WinRT not supported)
            if (!_winrtOverrideActive)
            {
                try
                {
                    byte? currentWmi = GetWmiBrightness();
                    if (currentWmi.HasValue)
                    {
                        _savedWmiBrightness = currentWmi.Value;
                        // Fire-and-forget — WMI latency (~50–200 ms) must not block the UI thread.
                        _ = Task.Run(() => SetWmiBrightness(100));
                        _wmiOverrideActive = true;
                        success = true;
                        Debug.WriteLine($"[HDR Brightness] WMI laptop override applied: {currentWmi.Value} → 100");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HDR Brightness] WMI override attempt failed: {ex.Message}");
                }
            }

            // 2. Try DDC/CI (for external monitors)
            try
            {
                var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero)
                {
                    if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                    {
                        bo.SetBrightnessLevel(1.0, Windows.Graphics.Display.DisplayBrightnessOverrideOptions.None);
                        bo.StartOverride();
                        lock (_stateLock)
                        {
                            _winRtOverride = bo;
                        }
                        Debug.WriteLine("[HDR Brightness] Tier 0 WinRT BrightnessOverride applied: 100%");
                        return true;
                    }
                    else
                    {
                        s_winRtUnsupported = true;
                    }
                }
                catch (Exception ex)
                {
                    s_winRtUnsupported = true;
                    Debug.WriteLine($"[HDR Brightness] Tier 0 WinRT BrightnessOverride unsupported or failed: {ex.Message}");
                }
            }

            // Only attempt Tier 1 (WMI) and Tier 2 (DDC/CI) if Tier 0 was not applied.
            var token = cts.Token;
            _lastWmiTask = Task.Run(async () =>
            {
                try
                {
                    if (token.IsCancellationRequested) return;

                    bool tier1Applied = false;

                    // 1. Try WMI (Tier 1 for laptop internal displays). Only attempt if Tier 0 was not applied.
                    if (!s_wmiUnsupported)
                    {
                        try
                        {
                            // Ensure WMI only queries and captures baseline if _wmiOverrideActive is false and no other tier is active
                            byte? currentWmi = null;
                            bool shouldQueryWmi;
                            lock (s_globalSyncLock)
                            {
                                lock (_stateLock)
                                {
                                    shouldQueryWmi = !s_isWmiOverrideActive && !_wmiOverrideActive
                                                     && !s_isDdcOverrideActive && !_ddcOverrideActive
                                                     && _winRtOverride == null;
                                }
                            }

                            if (!shouldQueryWmi)
                            {
                                lock (s_globalSyncLock)
                                {
                                    if (s_isWmiOverrideActive)
                                    {
                                        lock (_stateLock)
                                        {
                                            _wmiOverrideActive = true;
                                            _savedWmiBrightness = s_persistentBaselineWmi;
                                        }
                                        tier1Applied = true;
                                        Debug.WriteLine($"[HDR Brightness] WMI laptop override already active: baseline={s_persistentBaselineWmi}%");
                                    }
                                }
                            }
                            else
                            {
                                currentWmi = await GetWmiBrightnessAsync(token).ConfigureAwait(false);

                                if (token.IsCancellationRequested) return;

                                if (currentWmi.HasValue)
                                {
                                    if (await s_wmiGate.WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false))
                                    {
                                        try
                                        {
                                            if (token.IsCancellationRequested) return;

                                            lock (s_globalSyncLock)
                                            {
                                                lock (_stateLock)
                                                {
                                                    if (!_overrideRequested || _disposed || token.IsCancellationRequested)
                                                    {
                                                        return;
                                                    }

                                                    // Save baseline only if not currently overridden by any tier
                                                    if (!s_isWmiOverrideActive && !_wmiOverrideActive
                                                        && !s_isDdcOverrideActive && !_ddcOverrideActive
                                                        && _winRtOverride == null)
                                                    {
                                                        s_persistentBaselineWmi = currentWmi.Value;
                                                        SaveEmergencyBaseline(currentWmi.Value);
                                                        Debug.WriteLine($"[HDR Brightness] Captured persistent baseline WMI brightness: {s_persistentBaselineWmi.Value}%");
                                                    }

                                                    _savedWmiBrightness = s_persistentBaselineWmi ?? currentWmi.Value;

                                                    if (SetWmiBrightness(100))
                                                    {
                                                        // Verify we were not released or disposed while SetWmiBrightness was executing
                                                        if (!_overrideRequested || _disposed)
                                                        {
                                                            byte revertTarget = _savedWmiBrightness ?? 70;
                                                            SetWmiBrightness(revertTarget);
                                                            _wmiOverrideActive = false;
                                                            s_isWmiOverrideActive = false;
                                                            ClearEmergencyBaseline();
                                                            Debug.WriteLine($"[HDR Brightness] WMI override cancelled mid-flight, immediately reverted to {revertTarget}%");
                                                            return;
                                                        }

                                                        _wmiOverrideActive = true;
                                                        s_isWmiOverrideActive = true;
                                                        tier1Applied = true;
                                                        Debug.WriteLine($"[HDR Brightness] WMI laptop override applied: baseline={_savedWmiBrightness}% -> 100%");
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            s_wmiGate.Release();
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("[HDR Brightness] WMI brightness unsupported or no WMI monitor instance found on this device.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[HDR Brightness] WMI override failed in background: {ex.Message}");
                        }
                    }

                    if (token.IsCancellationRequested) return;

                    // 2. Try DDC/CI (Tier 2 for external monitors).
                    // Strictly mutually exclusive fallback: only attempt if Tier 0 and Tier 1 were not applied.
                    if (!tier1Applied)
                    {
                        await TryApplyDdcOverrideAsync(hwnd, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HDR Brightness] Override fallback execution failed: {ex.Message}");
                }
            }, token);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] General override failed: {ex.Message}");
            return false;
        }
    }

    private async Task TryApplyDdcOverrideAsync(IntPtr hwnd, CancellationToken token)
    {
        if (s_ddcUnsupported) return;
        try
        {
            if (token.IsCancellationRequested) return;

            var targetHwnd = hwnd != IntPtr.Zero ? hwnd : GetDesktopWindow();
            if (targetHwnd == IntPtr.Zero) return;

            var hMonitor = MonitorFromWindow(targetHwnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero) return;

            if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
            {
                var monitors = new PHYSICAL_MONITOR[count];
                if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                {
                    IntPtr hPhysical = monitors[0].hPhysicalMonitor;

                    lock (_stateLock)
                    {
                        if (!_overrideRequested || _disposed || token.IsCancellationRequested)
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

                        _physicalMonitorHandle = hPhysical;
                    }

                    // Query brightness via safe DDC gate with rate limiting
                    var (getOk, min, current, max) = await SafeGetMonitorBrightnessAsync(hPhysical, TimeSpan.FromSeconds(3), token).ConfigureAwait(false);

                    if (getOk)
                    {
                        lock (s_globalSyncLock)
                        {
                            lock (_stateLock)
                            {
                                if (!s_isDdcOverrideActive && !_ddcOverrideActive
                                    && !s_isWmiOverrideActive && !_wmiOverrideActive
                                    && _winRtOverride == null)
                                {
                                    s_persistentBaselineDdc = current;
                                    SaveEmergencyBaseline((int)current);
                                    Debug.WriteLine($"[HDR Brightness] Captured persistent baseline DDC/CI brightness: {s_persistentBaselineDdc.Value} (range {min}-{max})");
                                }

                                _savedDdcBrightness = s_persistentBaselineDdc ?? current;
                            }
                        }

                        // Boost brightness via safe DDC gate with rate limiting
                        bool setOk = await SafeSetMonitorBrightnessAsync(hPhysical, max, TimeSpan.FromSeconds(3), token).ConfigureAwait(false);

                        bool needsRevert = false;
                        uint revertTarget = 100u;
                        lock (s_globalSyncLock)
                        {
                            lock (_stateLock)
                            {
                                if (setOk)
                                {
                                    if (!_overrideRequested || _disposed || token.IsCancellationRequested)
                                    {
                                        revertTarget = _savedDdcBrightness ?? 100u;
                                        needsRevert = true;
                                        _ddcOverrideActive = false;
                                        s_isDdcOverrideActive = false;
                                        _physicalMonitorHandle = IntPtr.Zero;
                                    }
                                    else
                                    {
                                        _ddcOverrideActive = true;
                                        s_isDdcOverrideActive = true;
                                        Debug.WriteLine($"[HDR Brightness] DDC/CI monitor override applied: baseline={_savedDdcBrightness} → {max} (range {min}–{max})");
                                    }
                                }
                            }
                        }

                        if (needsRevert)
                        {
                            await SafeSetMonitorBrightnessAsync(hPhysical, revertTarget, TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false);
                            ClearEmergencyBaseline();
                            DestroyPhysicalMonitors(count, monitors);
                            return;
                        }
                    }

                    lock (_stateLock)
                    {
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] DDC/CI override attempt failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Restore the monitor brightness to the level saved before the override.
    /// </summary>
    public void Release()
    {
        // Restore WinRT (system)
        if (_winrtOverrideActive)
        {
            try
            {
                var winrtOverride = Windows.Graphics.Display.BrightnessOverride.GetDefaultForSystem();
                winrtOverride?.StopOverride();
                Debug.WriteLine("[HDR Brightness] WinRT system BrightnessOverride stopped");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] WinRT restore failed: {ex.Message}");
            }
            finally
            {
                _winrtOverrideActive = false;
            }
        }

        // Restore WMI (internal)
        if (_wmiOverrideActive)
        {
            lock (_stateLock)
            {
                if (s_wmiUnsupported) shouldRestoreWmi = false;
                if (s_ddcUnsupported) shouldRestoreDdc = false;
                winRtToStop = _winRtOverride;
                _winRtOverride = null;
                _overrideRequested = false;

                ctsToCancel = _wmiCts;
                _wmiCts = null;

                if (_wmiOverrideActive)
                {
                    _wmiOverrideActive = false;
                    shouldRestoreWmi = true;
                }

                if (_ddcOverrideActive)
                {
                    _ddcOverrideActive = false;
                    shouldRestoreDdc = true;
                    hPhysical = _physicalMonitorHandle;
                    _physicalMonitorHandle = IntPtr.Zero;
                }

                if (s_isWmiOverrideActive)
                {
                    shouldRestoreWmi = true;
                }
                if (s_isDdcOverrideActive)
                {
                    shouldRestoreDdc = true;
                }
                targetWmi = s_persistentBaselineWmi ?? _savedWmiBrightness ?? (byte)70;
                targetDdc = s_persistentBaselineDdc ?? _savedDdcBrightness ?? 100u;
            }
        }

        try
        {
            ctsToCancel?.Cancel();
            ctsToCancel?.Dispose();
        }
        catch { }

        // Tier 0: Stop WinRT override
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

        // Tier 1: Restore WMI (internal display)
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
                                Debug.WriteLine($"[HDR Brightness] WMI laptop brightness restored -> {safeTarget}%");
                            }
                            lock (s_globalSyncLock)
                            {
                                s_isWmiOverrideActive = false;
                                s_persistentBaselineWmi = null; // Clear so subsequent media samples fresh system level
                            }
                            ClearEmergencyBaseline();
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
            Debug.WriteLine($"[HDR Brightness] WMI laptop brightness restore scheduled -> {safeTarget}%");
        }

        // Tier 2: Restore DDC/CI (external monitor)
        if (shouldRestoreDdc)
        {
            IntPtr lastHwnd = _lastHwnd;
            if (lastHwnd == IntPtr.Zero && App.MainWindowInstance != null)
            {
                try { lastHwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance); } catch { }
            }

            _lastDdcTask = Task.Run(async () =>
            {
                try
                {
                    if (hPhysical != IntPtr.Zero)
                    {
                        var (getOk, min, cur, max) = await SafeGetMonitorBrightnessAsync(hPhysical, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                        if (getOk)
                        {
                            uint safeTarget = Math.Clamp(targetDdc, min, max);
                            if (await SafeSetMonitorBrightnessAsync(hPhysical, safeTarget, TimeSpan.FromSeconds(3)).ConfigureAwait(false))
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
                                var (getOk, min, cur, max) = await SafeGetMonitorBrightnessAsync(monitors[0].hPhysicalMonitor, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                                if (getOk)
                                {
                                    uint safeTarget = Math.Clamp(targetDdc, min, max);
                                    if (await SafeSetMonitorBrightnessAsync(monitors[0].hPhysicalMonitor, safeTarget, TimeSpan.FromSeconds(3)).ConfigureAwait(false))
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
                        s_isDdcOverrideActive = false;
                        s_persistentBaselineDdc = null; // Clear so subsequent media samples fresh monitor level
                    }
                    ClearEmergencyBaseline();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HDR Brightness] DDC/CI restore failed: {ex.Message}");
                }
            });
        }
    }

    private void ReleaseSynchronous()
    {
        try
        {
            Windows.Graphics.Display.BrightnessOverride? winRtToStop;
            bool shouldRestoreWmi = false;
            byte targetWmi = 70;
            bool shouldRestoreDdc = false;
            uint targetDdc = 100;
            IntPtr hPhysical = IntPtr.Zero;
            IntPtr lastHwnd;

            lock (s_globalSyncLock)
            {
                lock (_stateLock)
                {
                    winRtToStop = _winRtOverride;
                    _winRtOverride = null;
                    _overrideRequested = false;

                    if (_wmiOverrideActive)
                    {
                        _wmiOverrideActive = false;
                        shouldRestoreWmi = true;
                    }

                    if (_ddcOverrideActive)
                    {
                        _ddcOverrideActive = false;
                        shouldRestoreDdc = true;
                        hPhysical = _physicalMonitorHandle;
                        _physicalMonitorHandle = IntPtr.Zero;
                    }

                    lastHwnd = _lastHwnd;

                    if (s_isWmiOverrideActive)
                    {
                        shouldRestoreWmi = true;
                    }
                    if (s_isDdcOverrideActive)
                    {
                        shouldRestoreDdc = true;
                    }
                    targetWmi = s_persistentBaselineWmi ?? _savedWmiBrightness ?? (byte)70;
                    targetDdc = s_persistentBaselineDdc ?? _savedDdcBrightness ?? 100u;

                    s_isWmiOverrideActive = false;
                    s_isDdcOverrideActive = false;
                    s_persistentBaselineWmi = null;
                    s_persistentBaselineDdc = null;
                }
            }

            try
            {
                _wmiCts?.Cancel();
                _wmiCts?.Dispose();
            }
            catch { }

            if (winRtToStop != null)
            {
                try { winRtToStop.StopOverride(); } catch { }
            }

            if (shouldRestoreWmi)
            {
                try
                {
                    byte safeTarget = Math.Clamp(targetWmi, (byte)0, (byte)100);
                    SetWmiBrightness(safeTarget);
                    Debug.WriteLine($"[HDR Brightness] WMI laptop brightness synchronously restored â†’ {safeTarget}%");
                }
                catch { }
            }

            if (shouldRestoreDdc)
            {
                try
                {
                    if (hPhysical != IntPtr.Zero)
                    {
                        var (getOk, min, cur, max) = SafeGetMonitorBrightnessSync(hPhysical, TimeSpan.FromSeconds(2));
                        if (getOk)
                        {
                            uint safeTarget = Math.Clamp(targetDdc, min, max);
                            SafeSetMonitorBrightnessSync(hPhysical, safeTarget, TimeSpan.FromSeconds(2));
                        }
                        var monitors = new[] { new PHYSICAL_MONITOR { hPhysicalMonitor = hPhysical } };
                        DestroyPhysicalMonitors(1, monitors);
                    }
                    else if (lastHwnd != IntPtr.Zero)
                    {
                        var hMonitor = MonitorFromWindow(lastHwnd, MONITOR_DEFAULTTONEAREST);
                        if (hMonitor != IntPtr.Zero && GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                        {
                            var monitors = new PHYSICAL_MONITOR[count];
                            if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                            {
                                var (getOk, min, cur, max) = SafeGetMonitorBrightnessSync(monitors[0].hPhysicalMonitor, TimeSpan.FromSeconds(2));
                                if (getOk)
                                {
                                    uint safeTarget = Math.Clamp(targetDdc, min, max);
                                    SafeSetMonitorBrightnessSync(monitors[0].hPhysicalMonitor, safeTarget, TimeSpan.FromSeconds(2));
                                }
                                DestroyPhysicalMonitors(count, monitors);
                            }
                        }
                    }
                }
                catch { }
            }

            ClearEmergencyBaseline();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] Synchronous release failed: {ex.Message}");
        }
    }

    private static void SaveEmergencyBaseline(int baseline)
    {
        if (baseline < 0 || baseline > 100) return;
        try
        {
            if (AppServices.Settings != null)
            {
                AppServices.Settings.Current.EmergencyHdrBaselineBrightness = baseline;
                AppServices.Settings.Save();
                Debug.WriteLine($"[HDR Brightness] Saved emergency baseline to Settings: {baseline}%");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] Failed to persist emergency baseline: {ex.Message}");
        }
    }

    private static void ClearEmergencyBaseline()
    {
        try
        {
            if (AppServices.Settings != null && AppServices.Settings.Current.EmergencyHdrBaselineBrightness.HasValue)
            {
                AppServices.Settings.Current.EmergencyHdrBaselineBrightness = null;
                AppServices.Settings.Save();
                Debug.WriteLine("[HDR Brightness] Cleared emergency baseline from Settings");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] Failed to clear emergency baseline: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks <c>AppSettings</c> on app startup or shutdown and restores baseline brightness
    /// if the app was previously terminated while overridden.
    /// </summary>
    public static void TryRestoreEmergencyBaseline()
    {
        try
        {
            int? raw = null;
            try
            {
                raw = AppServices.Settings?.Current?.EmergencyHdrBaselineBrightness;
            }
            catch { }

            if (!raw.HasValue) return;

            int baseline = raw.Value;
            if (baseline < 0 || baseline > 100)
            {
                ClearEmergencyBaseline();
                return;
            }

            Debug.WriteLine($"[HDR Brightness] Found emergency baseline in Settings: {baseline}%. Attempting restoration...");

            // 1. Try restoring via WMI
            try
            {
                byte safeWmi = (byte)Math.Clamp(baseline, 0, 100);
                if (SetWmiBrightness(safeWmi))
                {
                    Debug.WriteLine($"[HDR Brightness] Emergency baseline restored via WMI: {safeWmi}%");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] Emergency WMI restore failed: {ex.Message}");
            }

            // 2. Try restoring via DDC/CI
            try
            {
                IntPtr hwnd = IntPtr.Zero;
                if (App.MainWindowInstance != null)
                {
                    try { hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance); } catch { }
                }
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = GetDesktopWindow();
                }

                if (hwnd != IntPtr.Zero)
                {
                    var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                    if (hMonitor != IntPtr.Zero && GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                    {
                        var monitors = new PHYSICAL_MONITOR[count];
                        if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                        {
                            for (int i = 0; i < count; i++)
                            {
                                try
                                {
                                    var (getOk, min, cur, max) = SafeGetMonitorBrightnessSync(monitors[i].hPhysicalMonitor, TimeSpan.FromSeconds(2));
                                    if (getOk)
                                    {
                                        uint safeTarget = Math.Clamp((uint)baseline, min, max);
                                        if (SafeSetMonitorBrightnessSync(monitors[i].hPhysicalMonitor, safeTarget, TimeSpan.FromSeconds(2)))
                                        {
                                            Debug.WriteLine($"[HDR Brightness] Emergency baseline restored via DDC/CI on monitor {i}: {safeTarget}");
                                        }
                                    }
                                }
                                catch { }
                            }
                            DestroyPhysicalMonitors(count, monitors);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] Emergency DDC/CI restore failed: {ex.Message}");
            }

            ClearEmergencyBaseline();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] TryRestoreEmergencyBaseline failed: {ex.Message}");
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

    public bool IsOverrideActive => _winrtOverrideActive || _ddcOverrideActive || _wmiOverrideActive;

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Release();

        var timeout = (App.MainDispatcher?.HasThreadAccess == true)
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromMilliseconds(1000);
        WaitForPendingRestore(timeout);
    }

    public void WaitForPendingRestore(TimeSpan timeout)
    {
        try
        {
            Task? wmiTask;
            Task? ddcTask;
            lock (_stateLock)
            {
                wmiTask = _lastWmiTask;
                ddcTask = _lastDdcTask;
            }
            var tasks = new[] { wmiTask, ddcTask }.Where(t => t != null).ToArray();
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
                            byte b = Convert.ToByte(val);
                            if (b <= 100)
                            {
                                return b;
                            }
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
                        // Invoke on all instances (some drivers report Active=false incorrectly)
                        // Pass uint 0 (0u) as Timeout so ACPI brightness methods transition immediately without type mismatch
                        instance.InvokeMethod("WmiSetBrightness", new object[] { 0u, targetBrightness });
                        success = true;
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

    // ── DDC/CI Rate Limiting & Gating ─────────────────────────────────

    private static async Task EnforceDdcRateLimitAsync(CancellationToken token)
    {
        long last = Interlocked.Read(ref s_lastDdcTimestamp);
        if (last > 0)
        {
            double elapsedMs = (Stopwatch.GetTimestamp() - last) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < MinDdcInterCommandDelayMs)
            {
                int delayMs = (int)Math.Ceiling(MinDdcInterCommandDelayMs - elapsedMs);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                }
            }
        }
    }

    private static void EnforceDdcRateLimitSync()
    {
        long last = Interlocked.Read(ref s_lastDdcTimestamp);
        if (last > 0)
        {
            double elapsedMs = (Stopwatch.GetTimestamp() - last) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs < MinDdcInterCommandDelayMs)
            {
                int delayMs = (int)Math.Ceiling(MinDdcInterCommandDelayMs - elapsedMs);
                if (delayMs > 0)
                {
                    Thread.Sleep(delayMs);
                }
            }
        }
    }

    private static async Task<(bool success, uint min, uint current, uint max)> SafeGetMonitorBrightnessAsync(
        IntPtr hPhysicalMonitor, TimeSpan timeout, CancellationToken token = default)
    {
        if (hPhysicalMonitor == IntPtr.Zero) return (false, 0, 0, 0);

        if (!await s_ddcGate.WaitAsync(timeout, token).ConfigureAwait(false))
        {
            Debug.WriteLine("[HDR Brightness] DDC/CI gate timeout during GetMonitorBrightness");
            return (false, 0, 0, 0);
        }

        try
        {
            await EnforceDdcRateLimitAsync(token).ConfigureAwait(false);
            bool ok = GetMonitorBrightness(hPhysicalMonitor, out uint min, out uint current, out uint max);
            s_lastDdcTimestamp = Stopwatch.GetTimestamp();
            return (ok, min, current, max);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] SafeGetMonitorBrightnessAsync failed: {ex.Message}");
            return (false, 0, 0, 0);
        }
        finally
        {
            s_ddcGate.Release();
        }
    }

    private static async Task<bool> SafeSetMonitorBrightnessAsync(
        IntPtr hPhysicalMonitor, uint brightness, TimeSpan timeout, CancellationToken token = default)
    {
        if (hPhysicalMonitor == IntPtr.Zero) return false;

        if (!await s_ddcGate.WaitAsync(timeout, token).ConfigureAwait(false))
        {
            Debug.WriteLine("[HDR Brightness] DDC/CI gate timeout during SetMonitorBrightness");
            return false;
        }

        try
        {
            await EnforceDdcRateLimitAsync(token).ConfigureAwait(false);
            bool ok = SetMonitorBrightness(hPhysicalMonitor, brightness);
            s_lastDdcTimestamp = Stopwatch.GetTimestamp();
            return ok;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] SafeSetMonitorBrightnessAsync failed: {ex.Message}");
            return false;
        }
        finally
        {
            s_ddcGate.Release();
        }
    }

    private static (bool success, uint min, uint current, uint max) SafeGetMonitorBrightnessSync(
        IntPtr hPhysicalMonitor, TimeSpan timeout)
    {
        if (hPhysicalMonitor == IntPtr.Zero) return (false, 0, 0, 0);

        if (!s_ddcGate.Wait(timeout))
        {
            Debug.WriteLine("[HDR Brightness] DDC/CI gate timeout during synchronous GetMonitorBrightness");
            return (false, 0, 0, 0);
        }

        try
        {
            EnforceDdcRateLimitSync();
            bool ok = GetMonitorBrightness(hPhysicalMonitor, out uint min, out uint current, out uint max);
            s_lastDdcTimestamp = Stopwatch.GetTimestamp();
            return (ok, min, current, max);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] SafeGetMonitorBrightnessSync failed: {ex.Message}");
            return (false, 0, 0, 0);
        }
        finally
        {
            s_ddcGate.Release();
        }
    }

    private static bool SafeSetMonitorBrightnessSync(
        IntPtr hPhysicalMonitor, uint brightness, TimeSpan timeout)
    {
        if (hPhysicalMonitor == IntPtr.Zero) return false;

        if (!s_ddcGate.Wait(timeout))
        {
            Debug.WriteLine("[HDR Brightness] DDC/CI gate timeout during synchronous SetMonitorBrightness");
            return false;
        }

        try
        {
            EnforceDdcRateLimitSync();
            bool ok = SetMonitorBrightness(hPhysicalMonitor, brightness);
            s_lastDdcTimestamp = Stopwatch.GetTimestamp();
            return ok;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HDR Brightness] SafeSetMonitorBrightnessSync failed: {ex.Message}");
            return false;
        }
        finally
        {
            s_ddcGate.Release();
        }
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
