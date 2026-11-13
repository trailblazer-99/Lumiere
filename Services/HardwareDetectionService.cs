using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;
using LumiereMediaPlayer.Models;

namespace LumiereMediaPlayer.Services;

public enum GpuType
{
    Integrated,
    Discrete,
    Unknown
}

public sealed class GpuDeviceInfo
{
    public string Name { get; set; } = string.Empty;
    public GpuType Type { get; set; } = GpuType.Unknown;
    public long DedicatedMemoryMB { get; set; }
    public string DriverVersion { get; set; } = string.Empty;
    public string DisplayLabel => $"{Name} ({Type}{(DedicatedMemoryMB > 0 ? $", {DedicatedMemoryMB} MB" : "")})";
}

public sealed class GpuEnvironmentInfo
{
    public bool HasDualGpu => AvailableGpus.Count > 1;
    public List<GpuDeviceInfo> AvailableGpus { get; set; } = [];
    public GpuDeviceInfo? IntegratedGpu => AvailableGpus.FirstOrDefault(g => g.Type == GpuType.Integrated);
    public GpuDeviceInfo? DiscreteGpu => AvailableGpus.FirstOrDefault(g => g.Type == GpuType.Discrete);
    public string Summary => HasDualGpu
        ? $"Dual GPU ({string.Join(" + ", AvailableGpus.Select(g => g.Name))})"
        : AvailableGpus.Count == 1 ? AvailableGpus[0].Name : "Standard Video Adapter";
}

public class HardwareAnalysisResult
{
    public bool SupportsLocalAi { get; set; }
    public string GpuName { get; set; } = string.Empty;
    public long TotalPhysicalMemoryMB { get; set; }
    public GpuEnvironmentInfo GpuEnvironment { get; set; } = new();
}

public static class HardwareDetectionService
{
    private static GpuEnvironmentInfo? _cachedGpuInfo;
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Analyzes the hardware to determine if it meets the requirements for local AI (Ollama).
    /// Typically requires a dedicated GPU or at least 16GB of RAM.
    /// </summary>
    public static async Task<HardwareAnalysisResult> AnalyzeHardwareAsync()
    {
        return await Task.Run(async () =>
        {
            var result = new HardwareAnalysisResult();

            try
            {
                // Check RAM
                using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                using (var coll = searcher.Get())
                {
                    foreach (var item in coll)
                    {
                        using (item)
                        {
                            if (item["TotalPhysicalMemory"] != null && long.TryParse(item["TotalPhysicalMemory"].ToString(), out long bytes))
                            {
                                result.TotalPhysicalMemoryMB = bytes / (1024 * 1024);
                            }
                        }
                    }
                }

                // Check GPUs and multi-GPU environment
                var gpuEnv = await DetectGpuEnvironmentAsync();
                result.GpuEnvironment = gpuEnv;

                bool hasDedicatedGpu = gpuEnv.DiscreteGpu != null;
                result.GpuName = gpuEnv.DiscreteGpu?.Name ?? gpuEnv.IntegratedGpu?.Name ?? string.Empty;

                // AI is supported if they have a dedicated GPU OR they have >= 16GB RAM for CPU inference
                result.SupportsLocalAi = hasDedicatedGpu || result.TotalPhysicalMemoryMB >= 15000;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareDetection] WMI query failed: {ex.Message}");
                // Fallback: assume false if we can't detect
                result.SupportsLocalAi = false;
            }

            return result;
        });
    }

    /// <summary>
    /// Enumerates all active video controllers, classifying them into Integrated and Discrete GPUs.
    /// </summary>
    public static async Task<GpuEnvironmentInfo> DetectGpuEnvironmentAsync(bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            lock (_cacheLock)
            {
                if (_cachedGpuInfo != null) return _cachedGpuInfo;
            }
        }

        return await Task.Run(() =>
        {
            var env = new GpuEnvironmentInfo();

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
                using var coll = searcher.Get();

                foreach (var item in coll)
                {
                    using (item)
                    {
                        string name = item["Name"]?.ToString()?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        long ramMb = 0;
                        if (item["AdapterRAM"] != null && long.TryParse(item["AdapterRAM"].ToString(), out long bytes))
                        {
                            ramMb = bytes / (1024 * 1024);
                        }

                        string driverVersion = item["DriverVersion"]?.ToString()?.Trim() ?? string.Empty;

                        // Classify GPU as Discrete vs Integrated
                        GpuType type;
                        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("AMD Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
                            ramMb > 1024)
                        {
                            type = GpuType.Discrete;
                        }
                        else
                        {
                            type = GpuType.Integrated;
                        }

                        env.AvailableGpus.Add(new GpuDeviceInfo
                        {
                            Name = name,
                            Type = type,
                            DedicatedMemoryMB = ramMb,
                            DriverVersion = driverVersion
                        });
                    }
                }

                lock (_cacheLock)
                {
                    _cachedGpuInfo = env;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareDetection] DetectGpuEnvironmentAsync failed: {ex.Message}");
            }

            return env;
        });
    }

    /// <summary>
    /// Updates the Windows Graphics Performance Preference for Lumière in HKCU\Software\Microsoft\DirectX\UserGpuPreferences.
    /// This causes Direct3D 11 and Media Foundation video hardware decoders to route to the user's preferred GPU.
    /// </summary>
    public static void ApplyGpuPreferenceToWindows(GpuPreference preference)
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            const string subKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
            using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
            if (key == null) return;

            switch (preference)
            {
                case GpuPreference.PowerSaving:
                    key.SetValue(exePath, "GpuPreference=1;");
                    Debug.WriteLine($"[HardwareDetection] Configured Windows GPU preference: PowerSaving (iGPU) for {exePath}");
                    break;
                case GpuPreference.HighPerformance:
                    key.SetValue(exePath, "GpuPreference=2;");
                    Debug.WriteLine($"[HardwareDetection] Configured Windows GPU preference: HighPerformance (dGPU) for {exePath}");
                    break;
                case GpuPreference.Auto:
                default:
                    // Remove custom override so Windows decides automatically
                    if (key.GetValue(exePath) != null)
                    {
                        key.DeleteValue(exePath, throwOnMissingValue: false);
                        Debug.WriteLine($"[HardwareDetection] Reset Windows GPU preference to Auto (Default) for {exePath}");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareDetection] Failed to update DirectX UserGpuPreferences: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the current Windows DirectX UserGpuPreferences for the Lumière process.
    /// </summary>
    public static GpuPreference GetCurrentWindowsGpuPreference()
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath)) return GpuPreference.Auto;

            const string subKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            if (key?.GetValue(exePath) is string prefStr)
            {
                if (prefStr.Contains("GpuPreference=1")) return GpuPreference.PowerSaving;
                if (prefStr.Contains("GpuPreference=2")) return GpuPreference.HighPerformance;
            }
        }
        catch { }

        return GpuPreference.Auto;
    }
}
