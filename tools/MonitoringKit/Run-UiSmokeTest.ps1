<#
.SYNOPSIS
    Lumiere Windows UI Automation Smoke Test Harness.
.DESCRIPTION
    Launches compiled LumiereMediaPlayer.exe, interacts with the running app via native
    Windows UI Automation (UIAutomationClient), validates window lifecycle, navigation elements,
    fullscreen transition responsiveness, and audits crash telemetry.
.PARAMETER AppPath
    Path to the Lumiere executable. Defaults to the Release x64 binary.
.PARAMETER TimeoutSeconds
    Maximum seconds to wait for main window initialization.
.PARAMETER KeepOpen
    Leave the app running after the test completes.
#>
param(
    [string]$AppPath = "",
    [int]$TimeoutSeconds = 30,
    [switch]$KeepOpen
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = (Resolve-Path (Join-Path $scriptDir "..\..")).Path

if (-not $AppPath) {
    $AppPath = Join-Path $rootDir "bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\LumiereMediaPlayer.exe"
}

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  LUMIERE NATIVE UI AUTOMATION SMOKE TEST HARNESS" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "Target Executable: $AppPath" -ForegroundColor DarkGray
Write-Host "Root Directory:    $rootDir" -ForegroundColor DarkGray
Write-Host "Timestamp:         $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor DarkGray
Write-Host ""

if (-not (Test-Path $AppPath)) {
    Write-Host "[FAIL] Executable not found at: $AppPath" -ForegroundColor Red
    Write-Host "Please build the project first: dotnet build LumiereMediaPlayer.csproj -c Release /p:Platform=x64" -ForegroundColor Yellow
    exit 1
}

# Load UI Automation assemblies
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# Add Win32 User32 helper for robust window activation and direct key dispatch
if (-not ([System.Management.Automation.PSTypeName]'Win32MessageHelper').Type) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32MessageHelper {
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
"@
}

$Results = [System.Collections.Generic.List[psobject]]::new()

function Record-Step {
    param([string]$Scenario, [string]$Status, [string]$Details = "")
    $color = if ($Status -eq "PASS") { "Green" } elseif ($Status -eq "WARN") { "Yellow" } else { "Red" }
    Write-Host "  [$Status] $Scenario" -ForegroundColor $color
    if ($Details) {
        Write-Host "         $Details" -ForegroundColor DarkGray
    }
    $Results.Add([PSCustomObject]@{
        Scenario = $Scenario
        Status   = $Status
        Details  = $Details
        Time     = (Get-Date -Format "HH:mm:ss")
    })
}

$proc = $null
try {
    # -------------------------------------------------------------------------
    # Scenario 1: Launch Process
    # -------------------------------------------------------------------------
    Write-Host "[+] Scenario 1: Process Startup & Launch" -ForegroundColor White
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $AppPath
    $psi.WorkingDirectory = (Split-Path -Parent $AppPath)
    $psi.UseShellExecute = $true

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = [System.Diagnostics.Process]::Start($psi)

    if ($proc -and -not $proc.HasExited) {
        Record-Step "Process Launch" "PASS" "Started LumiereMediaPlayer.exe (PID: $($proc.Id))"
    } else {
        Record-Step "Process Launch" "FAIL" "Failed to start process or process exited immediately"
        exit 1
    }

    # -------------------------------------------------------------------------
    # Scenario 2: Main Window Discovery & UI Automation Root Hook
    # -------------------------------------------------------------------------
    Write-Host "[+] Scenario 2: UI Automation Discovery & HWND Hook" -ForegroundColor White
    $mainWindowElement = $null
    $elapsed = 0
    while ($elapsed -lt ($TimeoutSeconds * 1000)) {
        Start-Sleep -Milliseconds 600
        $elapsed += 600
        
        $procCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $proc.Id
        )

        $allWindows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            $procCondition
        )

        foreach ($win in $allWindows) {
            $wRect = $win.Current.BoundingRectangle
            if ($wRect.Width -gt 300 -and $wRect.Height -gt 250) {
                $mainWindowElement = $win
                break
            }
        }

        if ($mainWindowElement -ne $null) {
            break
        }
    }

    $sw.Stop()

    if ($mainWindowElement -ne $null) {
        $windowTitle = $mainWindowElement.Current.Name
        if (-not $windowTitle) { $windowTitle = "Lumière Media Player" }
        Record-Step "Window Hook" "PASS" "Found main UI Automation window '$windowTitle' in $($sw.ElapsedMilliseconds)ms"
    } else {
        Record-Step "Window Hook" "FAIL" "Timed out waiting for main window after $TimeoutSeconds seconds"
        exit 1
    }

    # -------------------------------------------------------------------------
    # Scenario 3: Window Pattern & State Verification
    # -------------------------------------------------------------------------
    Write-Host "[+] Scenario 3: Window State & Responsiveness" -ForegroundColor White
    $windowPattern = $null
    try {
        $windowPattern = $mainWindowElement.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern) -as [System.Windows.Automation.WindowPattern]
    } catch { }

    if ($windowPattern -ne $null) {
        $canMax = $windowPattern.Current.CanMaximize
        $canMin = $windowPattern.Current.CanMinimize
        $visualState = $windowPattern.Current.WindowVisualState
        $isModal = $windowPattern.Current.IsModal
        Record-Step "Window Pattern Supported" "PASS" "VisualState: $visualState, CanMaximize: $canMax, CanMinimize: $canMin, IsModal: $isModal"
    } else {
        Record-Step "Window Pattern Supported" "WARN" "WindowPattern not directly exposed on root element"
    }

    # Bounding Rectangle Check
    $rect = $mainWindowElement.Current.BoundingRectangle
    if ($rect.Width -gt 400 -and $rect.Height -gt 300) {
        Record-Step "Window Bounding Box" "PASS" "Valid dimensions: $($rect.Width) x $($rect.Height) at ($($rect.X), $($rect.Y))"
    } else {
        Record-Step "Window Bounding Box" "FAIL" "Invalid or zero window dimensions: $($rect.Width) x $($rect.Height)"
    }

    # -------------------------------------------------------------------------
    # Scenario 4: UI Child Hierarchy & Navigation Chrome Check
    # -------------------------------------------------------------------------
    Write-Host "[+] Scenario 4: UI Hierarchy & Navigation Chrome Audit" -ForegroundColor White
    $childElements = $mainWindowElement.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    $childCount = $childElements.Count
    if ($childCount -gt 5) {
        Record-Step "Visual Tree Population" "PASS" "Discovered $childCount interactive UI Automation elements in tree"
    } else {
        Record-Step "Visual Tree Population" "FAIL" "Insufficient UI Automation elements in visual tree ($childCount <= 5)"
    }

    # -------------------------------------------------------------------------
    # Scenario 5: Fullscreen <-> Windowed Transition Toggle
    # -------------------------------------------------------------------------
    Write-Host "[+] Scenario 5: Fullscreen / Windowed Transition Smoke Check" -ForegroundColor White
    $hwnd = [IntPtr]$mainWindowElement.Current.NativeWindowHandle
    [Win32MessageHelper]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 400

    $WM_KEYDOWN = 0x0100
    $WM_KEYUP = 0x0101
    $VK_F11 = [IntPtr]0x7A

    # Send F11 KeyDown/KeyUp message directly to HWND
    [Win32MessageHelper]::PostMessage($hwnd, $WM_KEYDOWN, $VK_F11, [IntPtr]::Zero) | Out-Null
    [Win32MessageHelper]::PostMessage($hwnd, $WM_KEYUP, $VK_F11, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 1500 # Wait for transition

    $fullscreenRect = $mainWindowElement.Current.BoundingRectangle
    if ($fullscreenRect.Width -ge $rect.Width -and $fullscreenRect.Height -ge $rect.Height -and ($fullscreenRect.Width -gt $rect.Width -or $fullscreenRect.Height -gt $rect.Height)) {
        Record-Step "Fullscreen Transition Request" "PASS" "Dispatched F11 toggle. Window updated to $($fullscreenRect.Width) x $($fullscreenRect.Height)"
    } else {
        Record-Step "Fullscreen Transition Request" "FAIL" "Window bounds did not expand for fullscreen ($($rect.Width)x$($rect.Height) -> $($fullscreenRect.Width)x$($fullscreenRect.Height))"
    }

    # Restore windowed mode
    [Win32MessageHelper]::PostMessage($hwnd, $WM_KEYDOWN, $VK_F11, [IntPtr]::Zero) | Out-Null
    [Win32MessageHelper]::PostMessage($hwnd, $WM_KEYUP, $VK_F11, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 1500 # Wait for exit animation

    $restoredRect = $mainWindowElement.Current.BoundingRectangle
    if ([Math]::Abs($restoredRect.Width - $rect.Width) -le 20 -and [Math]::Abs($restoredRect.Height - $rect.Height) -le 20) {
        Record-Step "Exit Fullscreen Transition" "PASS" "Dispatched F11 restore. Window restored to $($restoredRect.Width) x $($restoredRect.Height)"
    } else {
        Record-Step "Exit Fullscreen Transition" "FAIL" "Window failed to restore expected bounds ($($restoredRect.Width)x$($restoredRect.Height) vs $($rect.Width)x$($rect.Height))"
    }

    # -------------------------------------------------------------------------
    # Scenario 6: Crash Telemetry & Unhandled Exception Audit
    # -------------------------------------------------------------------------
    Write-Host "[+] Scenario 6: Crash Telemetry & Exception Log Audit" -ForegroundColor White
    $appData = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::ApplicationData)
    $localAppData = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::LocalApplicationData)
    $lumiereDataDir = Join-Path $appData "LumiereMediaPlayer"
    $lumiereLocalDir = Join-Path $localAppData "LumiereMediaPlayer"
    $crashCandidatePaths = @(
        (Join-Path $lumiereLocalDir "crash.txt"),
        (Join-Path $lumiereLocalDir "crash.log"),
        (Join-Path $lumiereDataDir "crash.txt"),
        (Join-Path $lumiereDataDir "crash.log")
    )
    $playbackLogPath = Join-Path $lumiereDataDir "playback_log.txt"

    $hasNewCrash = $false
    foreach ($crashLogPath in $crashCandidatePaths) {
        if (Test-Path $crashLogPath) {
            $crashLastWrite = (Get-Item $crashLogPath).LastWriteTime
            if ((Get-Date) - $crashLastWrite -lt [TimeSpan]::FromMinutes(2)) {
                $hasNewCrash = $true
                Record-Step "Crash Log Audit" "FAIL" "Found recent crash log entry in $crashLogPath written at $crashLastWrite"
            }
        }
    }

    if (-not $hasNewCrash) {
        Record-Step "Crash Log Audit" "PASS" "Zero unhandled crashes detected during session"
    }

    if (Test-Path $playbackLogPath) {
        $pbContent = (Get-Content $playbackLogPath -Tail 20) -join "`n"
        if ($pbContent -match "OCCLUSION DETECTED") {
            Record-Step "Visual Diagnostics Telemetry" "WARN" "VisualDiagnostics detected and neutralized an occlusion event"
        } else {
            Record-Step "Visual Diagnostics Telemetry" "PASS" "No swapchain occlusion anomalies recorded in playback log"
        }
    } else {
        Record-Step "Visual Diagnostics Telemetry" "PASS" "Clean initial startup log state"
    }

    # -------------------------------------------------------------------------
    # Scenario 7: Graceful Window Shutdown
    # -------------------------------------------------------------------------
    Write-Host "[+] Scenario 7: Graceful Teardown & Process Exit" -ForegroundColor White
    if (-not $KeepOpen) {
        $closedGracefully = $false
        if ($windowPattern -ne $null) {
            try {
                $windowPattern.Close()
                $closedGracefully = $true
            } catch { }
        }

        if (-not $closedGracefully) {
            $WM_CLOSE = 0x0010
            [Win32MessageHelper]::PostMessage($hwnd, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        }

        # Wait up to 5 seconds for exit
        $proc.WaitForExit(5000) | Out-Null

        if ($proc.HasExited) {
            Record-Step "Graceful Shutdown" "PASS" "Process exited cleanly (ExitCode: $($proc.ExitCode))"
        } else {
            $proc.Kill()
            Record-Step "Graceful Shutdown" "FAIL" "Process hung after close request and required forceful termination"
        }
    } else {
        Record-Step "Shutdown" "PASS" "App left running per -KeepOpen switch"
    }

} catch {
    Record-Step "Automation Exception" "FAIL" $_.Exception.Message
} finally {
    if ($proc -and -not $proc.HasExited -and -not $KeepOpen) {
        try { $proc.Kill() } catch { }
    }
}

# -----------------------------------------------------------------------------
# Generate Markdown Report
# -----------------------------------------------------------------------------
$reportFolder = Join-Path $rootDir "tools\MonitoringKit\reports"
if (-not (Test-Path $reportFolder)) {
    New-Item -ItemType Directory -Path $reportFolder -Force | Out-Null
}

$reportPath = Join-Path $reportFolder "UiSmokeReport_Latest.md"
$totalPassed = ($Results | Where-Object { $_.Status -eq "PASS" }).Count
$totalFailed = ($Results | Where-Object { $_.Status -eq "FAIL" }).Count
$totalWarned = ($Results | Where-Object { $_.Status -eq "WARN" }).Count

$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine("# Lumiere UI Automation Smoke Test Report")
[void]$md.AppendLine("**Timestamp:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  ")
[void]$md.AppendLine("**Overall Status:** $(if ($totalFailed -eq 0) { 'PASSED' } else { 'FAILED' })  ")
[void]$md.AppendLine()
[void]$md.AppendLine("### Summary")
[void]$md.AppendLine("| Metric | Value |")
[void]$md.AppendLine("|---|---|")
[void]$md.AppendLine("| **Total Scenarios** | $($Results.Count) |")
[void]$md.AppendLine("| **Passed** | $totalPassed |")
[void]$md.AppendLine("| **Warnings** | $totalWarned |")
[void]$md.AppendLine("| **Failed** | $totalFailed |")
[void]$md.AppendLine()
[void]$md.AppendLine("### Step Results")
[void]$md.AppendLine("| Scenario | Status | Details | Time |")
[void]$md.AppendLine("|---|---|---|---|")

foreach ($r in $Results) {
    [void]$md.AppendLine("| $($r.Scenario) | $($r.Status) | $($r.Details) | $($r.Time) |")
}

[System.IO.File]::WriteAllText($reportPath, $md.ToString(), [System.Text.Encoding]::UTF8)

Write-Host ""
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  SMOKE TEST SUMMARY: Passed: $totalPassed, Warnings: $totalWarned, Failed: $totalFailed" -ForegroundColor $(if ($totalFailed -eq 0) { "Green" } else { "Red" })
Write-Host "  Report: $reportPath" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan

if ($totalFailed -gt 0) {
    exit 1
} else {
    exit 0
}
