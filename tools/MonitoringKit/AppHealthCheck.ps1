<#
.SYNOPSIS
    Lumière Media Player - Codebase Health & Integrity Sentinel
    
.DESCRIPTION
    Automated health verification and anti-pattern detection suite.
    Runs static analysis, memory leak detection, concurrency checks,
    XAML performance validation, compilation checks, and unit tests.

.PARAMETER Quick
    Skip dotnet build and unit test execution for rapid static-only scan.

.PARAMETER SkipTests
    Run build check but skip test execution.

.PARAMETER OutputReport
    Path to save the generated markdown health report. Default: tools/MonitoringKit/reports/HealthReport_Latest.md

.EXAMPLE
    .\tools\MonitoringKit\AppHealthCheck.ps1
    .\tools\MonitoringKit\AppHealthCheck.ps1 -Quick
    .\tools\MonitoringKit\AppHealthCheck.ps1 -SkipTests
#>

[CmdletBinding()]
param(
    [switch]$Quick,
    [switch]$SkipTests,
    [string]$OutputReport = "tools/MonitoringKit/reports/HealthReport_Latest.md"
)

$ErrorActionPreference = "Continue"
$rootDir = (Get-Item $PSScriptRoot).Parent.Parent.FullName
Set-Location $rootDir

# Compatibility Helper for Relative Paths (PowerShell 5.1 & Core compatible)
function Get-RelPath([string]$basePath, [string]$targetPath) {
    if ($targetPath.StartsWith($basePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $targetPath.Substring($basePath.Length).TrimStart('\', '/')
    }
    return $targetPath
}

# Output Formatting Helpers
function Write-Header {
    param([string]$Title)
    Write-Host ""
    Write-Host "================================================================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor White
    Write-Host "================================================================================" -ForegroundColor Cyan
}

function Write-Section {
    param([string]$Title)
    Write-Host "`n[+] $Title" -ForegroundColor Yellow
}

function Write-Pass {
    param([string]$Message)
    Write-Host "  [PASS] $Message" -ForegroundColor Green
}

function Write-Fail {
    param([string]$Message, [string]$Detail = "")
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
    if ($Detail) {
        Write-Host "         $Detail" -ForegroundColor DarkYellow
    }
}

function Write-Warn {
    param([string]$Message, [string]$Detail = "")
    Write-Host "  [WARN] $Message" -ForegroundColor DarkYellow
    if ($Detail) {
        Write-Host "         $Detail" -ForegroundColor DarkGray
    }
}

$TotalChecks = 0
$PassedChecks = 0
$FailedChecks = 0
$WarningChecks = 0
$Findings = [System.Collections.Generic.List[psobject]]::new()

function Record-Result {
    param(
        [string]$Category,
        [string]$Rule,
        [string]$Status, # "PASS", "FAIL", "WARN"
        [string]$Details = "",
        [string]$File = "",
        [int]$Line = 0
    )
    $script:TotalChecks++
    if ($Status -eq "PASS") {
        $script:PassedChecks++
        Write-Pass "$Rule"
    } elseif ($Status -eq "FAIL") {
        $script:FailedChecks++
        Write-Fail "$Rule" $Details
    } else {
        $script:WarningChecks++
        Write-Warn "$Rule" $Details
    }
    
    $Findings.Add([PSCustomObject]@{
        Category = $Category
        Rule     = $Rule
        Status   = $Status
        Details  = $Details
        File     = $File
        Line     = $Line
    })
}

Write-Header "LUMIERE CODEBASE HEALTH & REGRESSION SENTINEL"
Write-Host "Root Directory: $rootDir" -ForegroundColor DarkGray
Write-Host "Timestamp:      $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor DarkGray

# -----------------------------------------------------------------------------
# 1. ARCHITECTURAL & CONCURRENCY CHECKS
# -----------------------------------------------------------------------------
Write-Section "1. Concurrency & Thread-Safety Rules"

# 1.1 Non-event async void methods:
# Rule: Services, ViewModels, Models, Helpers must NEVER declare async void.
# In UI code-behind (*.xaml.cs), async void is permitted only if it is an event handler
# OR has a top-level try/catch exception boundary.
$csFiles = Get-ChildItem -Path $rootDir -Recurse -Filter "*.cs" | Where-Object { 
    $_.FullName -notmatch "\\obj\\" -and 
    $_.FullName -notmatch "\\bin\\" -and 
    $_.FullName -notmatch "\\Tests\\" 
}

$asyncVoidIssues = @()
foreach ($file in $csFiles) {
    $lines = Get-Content $file.FullName
    $isUiCodeBehind = ($file.Name -match "\.xaml\.cs$")
    
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match "async\s+void\s+([A-Za-z0-9_]+)\s*\((.*)\)") {
            $methodName = $Matches[1]
            $params = $Matches[2]
            $relPath = Get-RelPath $rootDir $file.FullName

            if (-not $isUiCodeBehind) {
                # Never permitted in non-UI code
                $asyncVoidIssues += [PSCustomObject]@{
                    File = $relPath
                    Line = ($i + 1)
                    Method = $methodName
                    Reason = "async void forbidden outside UI code-behind (must return Task)"
                    Code = $line.Trim()
                }
            } else {
                # In UI code-behind, must be an event handler OR have try/catch boundary
                $isEventHandler = ($params -match "sender" -or $params -match "EventArgs" -or $params -match "object\s+e" -or $methodName -match "Click" -or $methodName -match "Loaded" -or $methodName -match "Unloaded" -or $methodName -match "Changed")
                
                # Check next 8 lines for 'try' block if not a standard event handler
                $hasTry = $false
                $maxLookahead = [System.Math]::Min($lines.Count - 1, $i + 8)
                for ($j = $i + 1; $j -le $maxLookahead; $j++) {
                    if ($lines[$j] -match "try\s*\{|\btry\b") {
                        $hasTry = $true
                        break
                    }
                }
                
                if (-not $isEventHandler -and -not $hasTry) {
                    $asyncVoidIssues += [PSCustomObject]@{
                        File = $relPath
                        Line = ($i + 1)
                        Method = $methodName
                        Reason = "UI async void lacks try-catch exception boundary and is not an event handler"
                        Code = $line.Trim()
                    }
                }
            }
        }
    }
}

if ($asyncVoidIssues.Count -eq 0) {
    Record-Result "Concurrency" "Zero rogue async void methods (all non-UI methods return Task; UI handlers bounded)" "PASS"
} else {
    foreach ($issue in $asyncVoidIssues) {
        Record-Result "Concurrency" "Rogue async void: $($issue.Method)" "FAIL" "$($issue.File):$($issue.Line) -> $($issue.Reason)" $issue.File $issue.Line
    }
}

# 1.2 Thread-Safe Collections in Shared Services
$sharedServices = @("Services/Streaming/WatchmodeService.cs", "Services/MediaLibraryService.cs")
foreach ($svc in $sharedServices) {
    $svcPath = Join-Path $rootDir $svc
    if (Test-Path $svcPath) {
        $content = Get-Content $svcPath -Raw
        if ($svc -match "WatchmodeService") {
            if ($content -match "ConcurrentDictionary") {
                Record-Result "Concurrency" "WatchmodeService uses thread-safe ConcurrentDictionary" "PASS"
            } else {
                Record-Result "Concurrency" "WatchmodeService cache thread-safety" "FAIL" "Found non-thread-safe dictionary for caching in WatchmodeService" $svc
            }
        }
        if ($svc -match "MediaLibraryService") {
            if ($content -match "lock\s*\(\s*_lock\s*\)") {
                Record-Result "Concurrency" "MediaLibraryService uses snapshot locking on collections" "PASS"
            } else {
                Record-Result "Concurrency" "MediaLibraryService snapshot locking" "FAIL" "Missing snapshot lock on public collection accessors" $svc
            }
        }
    }
}

# -----------------------------------------------------------------------------
# 2. MEMORY LEAKS & LIFECYCLE MANAGEMENT
# -----------------------------------------------------------------------------
Write-Section "2. Memory Leaks & Unmanaged Resource Teardown"

# 2.1 ViewModels with event subscriptions must implement IDisposable and detach
$vmFiles = Get-ChildItem -Path (Join-Path $rootDir "ViewModels") -Filter "*.cs"
foreach ($vm in $vmFiles) {
    $relPath = Get-RelPath $rootDir $vm.FullName
    $content = Get-Content $vm.FullName -Raw
    
    # Check if VM subscribes to events with +=
    $hasSubscriptions = ($content -match "\+=\s*[A-Za-z0-9_]" -or $content -match "\+=\s*\(")
    $implementsDisposable = ($content -match ":.*IDisposable" -or $content -match "public\s+void\s+Dispose\s*\(\s*\)")
    $hasDetachment = ($content -match "-=\s*[A-Za-z0-9_]" -or $content -match "-=\s*\(")

    if ($hasSubscriptions) {
        if ($implementsDisposable -and $hasDetachment) {
            Record-Result "Memory" "ViewModel Event Lifecycle: $($vm.BaseName)" "PASS"
        } else {
            Record-Result "Memory" "ViewModel Event Lifecycle: $($vm.BaseName)" "FAIL" "$relPath subscribes to events but lacks IDisposable / proper detachment" $relPath
        }
    } else {
        Record-Result "Memory" "ViewModel Event Lifecycle: $($vm.BaseName) (Stateless/No Events)" "PASS"
    }
}

# 2.2 GlassContainer teardown
$glassContainerPath = Join-Path $rootDir "Controls/GlassContainer.cs"
if (Test-Path $glassContainerPath) {
    $glassContent = Get-Content $glassContainerPath -Raw
    if ($glassContent -match "SetElementChildVisual\s*\(\s*this\s*,\s*null\s*\)") {
        Record-Result "Memory" "GlassContainer visual decoupling on disposal" "PASS"
    } else {
        Record-Result "Memory" "GlassContainer visual decoupling" "FAIL" "GlassContainer does not clear ElementChildVisual on Dispose" "Controls/GlassContainer.cs"
    }
}

# 2.3 Streaming Page ShadowHolder recycling teardown
$streamingPages = @("Pages/StreamingMoviesPage.xaml.cs", "Pages/StreamingTvShowsPage.xaml.cs", "Pages/StreamingMusicPage.xaml.cs")
foreach ($sp in $streamingPages) {
    $spPath = Join-Path $rootDir $sp
    if (Test-Path $spPath) {
        $spContent = Get-Content $spPath -Raw
        if ($spContent -match "Card_Unloaded" -and $spContent -match "DropShadow") {
            Record-Result "Memory" "Composition Shadow recycling teardown in $sp" "PASS"
        } else {
            Record-Result "Memory" "Composition Shadow recycling teardown in $sp" "FAIL" "Missing Card_Unloaded shadow detachment in $sp" $sp
        }
    }
}

# 2.4 WMI & Monitor Handle cleanup
$wmiFiles = @("Services/HardwareDetectionService.cs", "Services/HdrPipelineService.cs", "Services/BrightnessOverrideHelper.cs")
foreach ($wf in $wmiFiles) {
    $wfPath = Join-Path $rootDir $wf
    if (Test-Path $wfPath) {
        $wfContent = Get-Content $wfPath -Raw
        if ($wfContent -match "ManagementObjectSearcher" -and ($wfContent -notmatch "using\s*var\s*searcher" -and $wfContent -notmatch "using\s*\(\s*var\s*searcher")) {
            Record-Result "Memory" "WMI Resource Disposal in $wf" "FAIL" "ManagementObjectSearcher is not wrapped in using statement" $wf
        } else {
            Record-Result "Memory" "WMI Resource Disposal in $wf" "PASS"
        }
    }
}

# -----------------------------------------------------------------------------
# 3. UI/UX PERFORMANCE & VIRTUALIZATION
# -----------------------------------------------------------------------------
Write-Section "3. UI/UX Rendering, Jitter & Virtualization"

# 3.1 Template namescope crossing: {Binding ElementName=...} inside DataTemplate
$xamlFiles = Get-ChildItem -Path $rootDir -Recurse -Filter "*.xaml" | Where-Object { 
    $_.FullName -notmatch "\\obj\\" -and 
    $_.FullName -notmatch "\\bin\\" 
}

$bindingElementNameInTemplate = @()
foreach ($xaml in $xamlFiles) {
    $lines = Get-Content $xaml.FullName
    $inDataTemplate = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match "<DataTemplate") { $inDataTemplate = $true }
        if ($line -match "</DataTemplate>") { $inDataTemplate = $false }
        
        if ($inDataTemplate -and $line -match '\{Binding\s+ElementName=') {
            $relPath = Get-RelPath $rootDir $xaml.FullName
            $bindingElementNameInTemplate += [PSCustomObject]@{
                File = $relPath
                Line = ($i + 1)
                Code = $line.Trim()
            }
        }
    }
}

if ($bindingElementNameInTemplate.Count -eq 0) {
    Record-Result "UI/Virtualization" "Zero ElementName bindings across DataTemplate namescopes" "PASS"
} else {
    foreach ($issue in $bindingElementNameInTemplate) {
        Record-Result "UI/Virtualization" "Slow ElementName binding across DataTemplate in $($issue.File)" "FAIL" "$($issue.File):$($issue.Line) -> $($issue.Code)" $issue.File $issue.Line
    }
}

# 3.2 Slider seek feedback loop guard
$glassTransportPath = Join-Path $rootDir "Controls/GlassTransportBar.xaml.cs"
if (Test-Path $glassTransportPath) {
    $gtContent = Get-Content $glassTransportPath -Raw
    if ($gtContent -match "_isProgrammaticUpdate") {
        Record-Result "UI/Virtualization" "GlassTransportBar slider feedback loop guarded" "PASS"
    } else {
        Record-Result "UI/Virtualization" "GlassTransportBar slider feedback loop" "FAIL" "Missing _isProgrammaticUpdate guard to prevent seek feedback storm" "Controls/GlassTransportBar.xaml.cs"
    }
}

# 3.3 MusicLibrary Checkbox virtualization performance
$musicLibraryPagePath = Join-Path $rootDir "Pages/MusicLibraryPage.xaml"
if (Test-Path $musicLibraryPagePath) {
    $mlpContent = Get-Content $musicLibraryPagePath -Raw
    if ($mlpContent -match 'Checked="OnTrackChecked"' -or $mlpContent -match 'Unchecked="OnTrackUnchecked"') {
        Record-Result "UI/Virtualization" "ListView Item virtualization Checkbox event storms" "FAIL" "MusicLibraryPage uses Checked/Unchecked events instead of Click handler" "Pages/MusicLibraryPage.xaml"
    } else {
        Record-Result "UI/Virtualization" "ListView Item CheckBox uses Click event (Virtualization Safe)" "PASS"
    }
}

# 3.4 MenuFlyoutPresenter visibility contrast
$mediaPlayerStylesPath = Join-Path $rootDir "Styles/MediaPlayerStyles.xaml"
if (Test-Path $mediaPlayerStylesPath) {
    $mpsContent = Get-Content $mediaPlayerStylesPath -Raw
    if ($mpsContent -match 'Style\s+x:Key="CinematicMenuFlyoutPresenterStyle"' -and $mpsContent -match 'MenuFlyoutPresenterBackground') {
        Record-Result "UI/Virtualization" "CinematicMenuFlyoutPresenterStyle has legible background" "PASS"
    } else {
        Record-Result "UI/Virtualization" "CinematicMenuFlyoutPresenterStyle legibility" "WARN" "CinematicMenuFlyoutPresenterStyle might lack opaque background resource" "Styles/MediaPlayerStyles.xaml"
    }
}

# -----------------------------------------------------------------------------
# 4. EQUALITY & DATA INTEGRITY
# -----------------------------------------------------------------------------
Write-Section "4. Contract & Data Integrity"

# 4.1 MediaItem Equals and GetHashCode consistency
$mediaItemPath = Join-Path $rootDir "Models/MediaItem.cs"
if (Test-Path $mediaItemPath) {
    $miContent = Get-Content $mediaItemPath -Raw
    if ($miContent -match "IdentityKey" -and $miContent -match "override bool Equals" -and $miContent -match "override int GetHashCode") {
        Record-Result "DataContract" "MediaItem unified IdentityKey Equals/GetHashCode contract" "PASS"
    } else {
        Record-Result "DataContract" "MediaItem Equals/GetHashCode contract" "FAIL" "MediaItem lacks unified IdentityKey hash/equality consistency" "Models/MediaItem.cs"
    }
}

# -----------------------------------------------------------------------------
# 5. SECURITY & NETWORK PROTOCOLS
# -----------------------------------------------------------------------------
Write-Section "5. Security & Network Protocols"

# 5.1 Insecure http:// calls in outbound network requests:
# Scans for actual outbound requests (HttpClient, HttpRequestMessage, new Uri) to remote unencrypted http:// endpoints
# (excludes local loopback http://localhost or standard schema declarations)
$insecureNetworkIssues = @()
foreach ($file in $csFiles) {
    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # Match remote http calls: e.g. "http://something.com" when used in network requests, excluding schemas and loopback
        if ($line -match '"http://(?!localhost|127\.0\.0\.1|schemas\.|www\.w3\.org)' -and 
            $line -notmatch '^\s*//' -and 
            $line -notmatch 'StartsWith\(' -and 
            $line -notmatch 'Replace\(' -and 
            $line -notmatch 'Equals\(' -and 
            $line -notmatch 'xmlns') {
            
            $relPath = Get-RelPath $rootDir $file.FullName
            $insecureNetworkIssues += [PSCustomObject]@{
                File = $relPath
                Line = ($i + 1)
                Code = $line.Trim()
            }
        }
    }
}

if ($insecureNetworkIssues.Count -eq 0) {
    Record-Result "Security" "Zero insecure remote http:// network calls (all remote APIs use TLS/HTTPS)" "PASS"
} else {
    foreach ($issue in $insecureNetworkIssues) {
        Record-Result "Security" "Insecure http:// network endpoint in $($issue.File)" "FAIL" "$($issue.File):$($issue.Line) -> $($issue.Code)" $issue.File $issue.Line
    }
}

# -----------------------------------------------------------------------------
# 6. UPDATE SERVICE & CRASH TELEMETRY POLICY
# -----------------------------------------------------------------------------
Write-Section "6. Update System & Crash Telemetry Policy"

# 6.1 Update Notification Flow vs Auto-Install
$updateSvcPath = Join-Path $rootDir "Services/UpdateService.cs"
if (Test-Path $updateSvcPath) {
    $usContent = Get-Content $updateSvcPath -Raw
    if ($usContent -match "UpdateAvailable" -or $usContent -match "PushNotification" -or $usContent -match "Prompt") {
        Record-Result "UpdateSystem" "UpdateService uses user-prompted notification pattern" "PASS"
    } else {
        Record-Result "UpdateSystem" "UpdateService notification policy" "WARN" "Verify UpdateService does not auto-apply without user confirmation" "Services/UpdateService.cs"
    }
}

# 6.2 Crash log bounded rollover in App.xaml.cs
$appXamlCsPath = Join-Path $rootDir "App.xaml.cs"
if (Test-Path $appXamlCsPath) {
    $appContent = Get-Content $appXamlCsPath -Raw
    if (($appContent -match "crash\.(?:txt|log)") -and ($appContent -match "Length\s*>" -or $appContent -match "2\s*\*\s*1024\s*\*\s*1024" -or $appContent -match "\.bak")) {
        Record-Result "Telemetry" "Crash logging rollover protection (bounded log size <= 2MB)" "PASS"
    } else {
        Record-Result "Telemetry" "Crash logging rollover protection" "FAIL" "App.xaml.cs crash logger does not implement bounded file size rollover" "App.xaml.cs"
    }
}

# -----------------------------------------------------------------------------
# 7. BUILD & COMPILATION HEALTH (Unless Quick)
# -----------------------------------------------------------------------------
if (-not $Quick) {
    Write-Section "7. Build & Compilation Verification (Release x64)"
    $buildCmd = "dotnet build LumiereMediaPlayer.csproj -c Release -p:Platform=x64 --nologo"
    Write-Host "  Executing: $buildCmd" -ForegroundColor DarkGray
    $buildOutput = Invoke-Expression $buildCmd 2>&1 | Out-String
    
    if ($LASTEXITCODE -eq 0 -and $buildOutput -match "0 Warning\(s\)" -and $buildOutput -match "0 Error\(s\)") {
        Record-Result "Build" "LumiereMediaPlayer compiles with 0 Warnings and 0 Errors" "PASS"
    } else {
        $warningMatch = [regex]::Match($buildOutput, '(\d+)\s+Warning\(s\)')
        $errorMatch = [regex]::Match($buildOutput, '(\d+)\s+Error\(s\)')
        $diag = "Warnings: $($warningMatch.Groups[1].Value), Errors: $($errorMatch.Groups[1].Value)"
        Record-Result "Build" "LumiereMediaPlayer compilation" "FAIL" $diag "LumiereMediaPlayer.csproj"
    }
    
    # -----------------------------------------------------------------------------
    # 8. UNIT TESTS & REGRESSION SUITE (Unless SkipTests)
    # -----------------------------------------------------------------------------
    if (-not $SkipTests) {
        Write-Section "8. Unit Tests & Regression Suite"
        $testCmd = "dotnet test Tests/LumiereMediaPlayer.Tests/LumiereMediaPlayer.Tests.csproj -c Release -p:Platform=x64 --no-build --nologo"
        Write-Host "  Executing: $testCmd" -ForegroundColor DarkGray
        $testOutput = Invoke-Expression $testCmd 2>&1 | Out-String
        
        $passedMatch = [regex]::Match($testOutput, 'Passed:\s*(\d+)')
        $failedMatch = [regex]::Match($testOutput, 'Failed:\s*(\d+)')
        $totalMatch = [regex]::Match($testOutput, 'Total:\s*(\d+)')
        
        if ($LASTEXITCODE -eq 0 -and $failedMatch.Success -and [int]$failedMatch.Groups[1].Value -eq 0) {
            $totalCount = if ($totalMatch.Success) { $totalMatch.Groups[1].Value } else { "All" }
            Record-Result "Tests" "All unit and stress tests passed ($totalCount/$totalCount)" "PASS"
        } else {
            $failCount = if ($failedMatch.Success) { $failedMatch.Groups[1].Value } else { "Unknown" }
            Record-Result "Tests" "Unit test suite" "FAIL" "Failed tests count: $failCount" "Tests/LumiereMediaPlayer.Tests"
        }
    } else {
        Write-Section "8. Unit Tests & Regression Suite (SKIPPED by parameter)"
    }
} else {
    Write-Section "7 & 8. Build & Unit Tests (SKIPPED by -Quick parameter)"
}

# -----------------------------------------------------------------------------
# SUMMARY & REPORT GENERATION
# -----------------------------------------------------------------------------
Write-Header "CODEBASE HEALTH SUMMARY"
Write-Host "Total Checks Run: $TotalChecks" -ForegroundColor White
Write-Host "Passed:           $PassedChecks" -ForegroundColor Green
Write-Host "Warnings:         $WarningChecks" -ForegroundColor Yellow
Write-Host "Failed:           $FailedChecks" -ForegroundColor $(if ($FailedChecks -eq 0) { "Green" } else { "Red" })

# Ensure reports directory exists
$reportDir = [System.IO.Path]::GetDirectoryName((Join-Path $rootDir $OutputReport))
if (-not (Test-Path $reportDir)) {
    New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
}

$fullReportPath = Join-Path $rootDir $OutputReport
$md = [System.Text.StringBuilder]::new()
[void]$md.AppendLine("# Lumiere Media Player - Codebase Health Report")
[void]$md.AppendLine("**Generated:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  ")
[void]$md.AppendLine("**Overall Status:** $(if ($FailedChecks -eq 0) { 'PASSED - ALL SYSTEMS HEALTHY' } else { 'FAILED - ISSUES DETECTED' })  ")
[void]$md.AppendLine()
[void]$md.AppendLine("### Summary Metrics")
[void]$md.AppendLine("| Metric | Count |")
[void]$md.AppendLine("|---|---|")
[void]$md.AppendLine("| **Total Checks** | $TotalChecks |")
[void]$md.AppendLine("| **Passed** | $PassedChecks |")
[void]$md.AppendLine("| **Warnings** | $WarningChecks |")
[void]$md.AppendLine("| **Failed** | $FailedChecks |")
[void]$md.AppendLine()
[void]$md.AppendLine("### Check Results Breakdown")
[void]$md.AppendLine("| Category | Rule / Target | Status | Details | Location |")
[void]$md.AppendLine("|---|---|---|---|---|")

foreach ($item in $Findings) {
    $statusIndicator = switch ($item.Status) {
        "PASS" { "PASS" }
        "FAIL" { "FAIL" }
        "WARN" { "WARN" }
    }
    $loc = if ($item.File) { if ($item.Line -gt 0) { "$($item.File):$($item.Line)" } else { $item.File } } else { "-" }
    $det = if ($item.Details) { $item.Details -replace '\|', '\|' } else { "-" }
    [void]$md.AppendLine("| $($item.Category) | $($item.Rule) | $statusIndicator | $det | $loc |")
}

[System.IO.File]::WriteAllText($fullReportPath, $md.ToString(), [System.Text.Encoding]::UTF8)
Write-Host "`nHealth Report saved to: $fullReportPath" -ForegroundColor Cyan

if ($FailedChecks -gt 0) {
    Write-Host "`n[ERROR] Health check detected $FailedChecks failed checks." -ForegroundColor Red
    exit 1
} else {
    Write-Host "`n[SUCCESS] Codebase is 100% clean and healthy!" -ForegroundColor Green
    exit 0
}
