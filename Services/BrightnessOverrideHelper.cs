using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Management;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Services;

/// <summary>
/// Uses both Win32 DDC/CI (Dxva2.dll) and WMI (WmiMonitorBrightness) to boost
/// display brightness to 100 % during HDR playback and restore it afterwards.
/// This covers both laptop internal panels and external monitors.
/// </summary>
internal sealed class BrightnessOverrideHelper : IDisposable
{
    private IntPtr _physicalMonitorHandle;
    private uint _savedDdcBrightness;
    private byte _savedWmiBrightness;
    private bool _ddcOverrideActive;
    private bool _wmiOverrideActive;
    private bool _disposed;

    /// <summary>
    /// Boost the monitor's brightness to its maximum level.
    /// Returns <c>true</c> if either WMI or DDC/CI override was applied successfully.
    /// </summary>
    public bool TryOverrideToMax(IntPtr hwnd)
    {
        if (IsOverrideActive)
        {
            return true;
        }

        bool success = false;
        try
        {

            // 1. Try WMI (for laptop internal displays)
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

            // 2. Try DDC/CI (for external monitors)
            try
            {
                var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (hMonitor != IntPtr.Zero)
                {
                    if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                    {
                        var monitors = new PHYSICAL_MONITOR[count];
                        if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors))
                        {
                            _physicalMonitorHandle = monitors[0].hPhysicalMonitor;
                            if (GetMonitorBrightness(_physicalMonitorHandle, out uint min, out uint current, out uint max))
                            {
                                _savedDdcBrightness = current;
                                // DDC/CI is a direct P/Invoke — no Task.Run wrapper needed.
                                SetMonitorBrightness(_physicalMonitorHandle, max);
                                _ddcOverrideActive = true;
                                success = true;
                                Debug.WriteLine($"[HDR Brightness] DDC/CI monitor override applied: {current} → {max} (range {min}–{max})");
                            }
                            if (!_ddcOverrideActive)
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
        // Restore WMI (internal)
        if (_wmiOverrideActive)
        {
            try
            {
                // Fire-and-forget restore — same reasoning as the override path.
                _ = Task.Run(() => SetWmiBrightness(_savedWmiBrightness));
                Debug.WriteLine($"[HDR Brightness] WMI laptop brightness restore queued → {_savedWmiBrightness}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] WMI restore failed: {ex.Message}");
            }
            finally
            {
                _wmiOverrideActive = false;
            }
        }

        // Restore DDC/CI (external)
        if (_ddcOverrideActive && _physicalMonitorHandle != IntPtr.Zero)
        {
            try
            {
                if (SetMonitorBrightness(_physicalMonitorHandle, _savedDdcBrightness))
                {
                    Debug.WriteLine($"[HDR Brightness] DDC/CI monitor brightness restored to {_savedDdcBrightness}");
                }
                var monitors = new[] { new PHYSICAL_MONITOR { hPhysicalMonitor = _physicalMonitorHandle } };
                DestroyPhysicalMonitors(1, monitors);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HDR Brightness] DDC/CI restore failed: {ex.Message}");
            }
            finally
            {
                _physicalMonitorHandle = IntPtr.Zero;
                _ddcOverrideActive = false;
            }
        }
    }

    public bool IsOverrideActive => _ddcOverrideActive || _wmiOverrideActive;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
    }

    // ── WMI Helper Methods ────────────────────────────────────────────

    private static byte? GetWmiBrightness()
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness"))
            {
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject mObj in collection)
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
            Debug.WriteLine($"[WMI Brightness] Get failed: {ex.Message}");
        }
        return null;
    }

    private static bool SetWmiBrightness(byte targetBrightness)
    {
        try
        {
            using (var mclass = new ManagementClass("WmiMonitorBrightnessMethods") { Scope = new ManagementScope(@"\\.\root\wmi") })
            {
                using (var instances = mclass.GetInstances())
                {
                    bool success = false;
                    foreach (ManagementObject instance in instances)
                    {
                        // Invoke on all instances (some drivers report Active=false incorrectly)
                        instance.InvokeMethod("WmiSetBrightness", new object[] { 1, targetBrightness });
                        success = true;
                    }
                    return success;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WMI Brightness] Set failed: {ex.Message}");
            return false;
        }
    }

    // ── DDC/CI P/Invoke ──────────────────────────────────────────────

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

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
