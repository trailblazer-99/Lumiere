<#
.SYNOPSIS
    Lumière Media Player - Live Codebase Watcher & Continuous Sentinel
    
.DESCRIPTION
    Monitors source code files (*.cs, *.xaml, *.csproj) in real-time.
    Upon detecting file modifications, debounces changes and automatically
    runs the fast health sentinel (AppHealthCheck.ps1 -Quick) to give
    immediate feedback to developers and AI pair programmers.

.PARAMETER IncludeTests
    When specified, runs full verification (compilation + unit tests) on change.

.EXAMPLE
    .\tools\MonitoringKit\WatchCodebase.ps1
    .\tools\MonitoringKit\WatchCodebase.ps1 -IncludeTests
#>

[CmdletBinding()]
param(
    [switch]$IncludeTests
)

$rootDir = (Get-Item $PSScriptRoot).Parent.Parent.FullName
$healthCheckScript = Join-Path $PSScriptRoot "AppHealthCheck.ps1"

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "  LUMIERE CONTINUOUS CODEBASE WATCHER" -ForegroundColor White
Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host "Root Directory: $rootDir" -ForegroundColor DarkGray
Write-Host "Monitoring:     *.cs, *.xaml, *.csproj" -ForegroundColor DarkGray
Write-Host "Mode:           $(if ($IncludeTests) { 'Full Verification (Static + Build + Tests)' } else { 'Fast Sentinel (Static Rules Only)' })" -ForegroundColor Yellow
Write-Host "Press Ctrl+C to terminate watcher.`n" -ForegroundColor DarkCyan

# Create FileSystemWatcher
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $rootDir
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true
$watcher.NotifyFilter = [System.IO.NotifyFilters]::LastWrite -bor [System.IO.NotifyFilters]::FileName

$lastTrigger = [DateTime]::MinValue
$debounceWindowMs = 1500

$action = {
    $path = $Event.SourceEventArgs.FullPath
    $changeType = $Event.SourceEventArgs.ChangeType
    
    # Filter out generated artifacts, bin, obj, git, and reports
    if ($path -match "\\bin\\" -or 
        $path -match "\\obj\\" -or 
        $path -match "\\\.git\\" -or 
        $path -match "\\reports\\" -or
        $path -notmatch "\.(cs|xaml|csproj)$") {
        return
    }

    $now = [DateTime]::Now
    if (($now - $script:lastTrigger).TotalMilliseconds -lt $script:debounceWindowMs) {
        return
    }
    $script:lastTrigger = $now

    $relPath = if ($path.StartsWith($script:rootDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        $path.Substring($script:rootDir.Length).TrimStart('\', '/')
    } else { $path }

    Write-Host "`n--------------------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host "[$([DateTime]::Now.ToString('HH:mm:ss'))] Change detected in: $relPath ($changeType)" -ForegroundColor Cyan
    Write-Host "Running integrity scan..." -ForegroundColor DarkYellow
    
    $argsList = if ($script:IncludeTests) { @("-OutputReport", "tools/MonitoringKit/reports/HealthReport_Latest.md") } 
                else { @("-Quick", "-OutputReport", "tools/MonitoringKit/reports/HealthReport_Latest.md") }
    
    try {
        & powershell -ExecutionPolicy Bypass -File $script:healthCheckScript @argsList
    } catch {
        Write-Host "Error executing sentinel: $_" -ForegroundColor Red
    }
}

Register-ObjectEvent $watcher "Changed" -Action $action | Out-Null
Register-ObjectEvent $watcher "Created" -Action $action | Out-Null
Register-ObjectEvent $watcher "Deleted" -Action $action | Out-Null
Register-ObjectEvent $watcher "Renamed" -Action $action | Out-Null

# Keep script running
try {
    while ($true) {
        Start-Sleep -Seconds 1
    }
} finally {
    $watcher.EnableRaisingEvents = $false
    $watcher.Dispose()
    Get-EventSubscriber | Where-Object { $_.SourceIdentifier -match "System.IO.FileSystemWatcher" } | Unregister-Event
    Write-Host "`nCodebase watcher terminated." -ForegroundColor Gray
}
