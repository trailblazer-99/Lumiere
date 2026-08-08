using System;
using System.Management;
using System.Threading.Tasks;

namespace LumiereMediaPlayer.Services;

public class HardwareAnalysisResult
{
    public bool SupportsLocalAi { get; set; }
    public string GpuName { get; set; } = string.Empty;
    public long TotalPhysicalMemoryMB { get; set; }
}

public static class HardwareDetectionService
{
    /// <summary>
    /// Analyzes the hardware to determine if it meets the requirements for local AI (Ollama).
    /// Typically requires a dedicated GPU or at least 16GB of RAM.
    /// </summary>
    public static async Task<HardwareAnalysisResult> AnalyzeHardwareAsync()
    {
        return await Task.Run(() =>
        {
            var result = new HardwareAnalysisResult();
            
            try
            {
                // Check RAM
                using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (var item in searcher.Get())
                    {
                        if (item["TotalPhysicalMemory"] != null && long.TryParse(item["TotalPhysicalMemory"].ToString(), out long bytes))
                        {
                            result.TotalPhysicalMemoryMB = bytes / (1024 * 1024);
                        }
                    }
                }

                // Check GPU
                bool hasDedicatedGpu = false;
                using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                {
                    foreach (var item in searcher.Get())
                    {
                        string name = item["Name"]?.ToString() ?? "";
                        // Simple heuristic for dedicated GPUs
                        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || 
                            name.Contains("AMD Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                        {
                            hasDedicatedGpu = true;
                            result.GpuName = name;
                        }
                        else if (string.IsNullOrEmpty(result.GpuName))
                        {
                            result.GpuName = name; // fallback to integrated if no dedicated found
                        }
                    }
                }

                // AI is supported if they have a dedicated GPU OR they have >= 16GB RAM for CPU inference
                result.SupportsLocalAi = hasDedicatedGpu || result.TotalPhysicalMemoryMB >= 15000;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HardwareDetection] WMI query failed: {ex.Message}");
                // Fallback: assume false if we can't detect
                result.SupportsLocalAi = false;
            }

            return result;
        });
    }
}
