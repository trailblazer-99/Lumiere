# Install-Lumiere.ps1 - Universal Installer for Lumiere Media Player
# Works seamlessly on both x64 and ARM64 Windows 10/11 devices.
# Performs automated certificate trusting, sideloading enablement, and package installation.

param(
    [string]$PackagePath = "",
    [string]$CertificatePath = "",
    [switch]$NonInteractive,
    [switch]$SkipLaunch
)

$ErrorActionPreference = "Stop"

function Write-Step   ($msg) { Write-Host "`n[+] $msg" -ForegroundColor Cyan }
function Write-Ok     ($msg) { Write-Host "    [OK]   $msg" -ForegroundColor Green }
function Write-WarnMsg($msg) { Write-Host "    [WARN] $msg" -ForegroundColor Yellow }
function Write-ErrMsg ($msg) { Write-Host "    [FAIL] $msg" -ForegroundColor Red }

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       Lumiere Media Player - Universal Installer         " -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Architecture & OS Verification
Write-Step "Detecting System Architecture and Windows Version..."
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
$osVer = [System.Environment]::OSVersion.Version
$build = $osVer.Build

Write-Ok "Device Architecture: $arch"
Write-Ok "Windows Build: $build"

if ($build -lt 17763) {
    Write-ErrMsg "Windows 10 version 1809 (Build 17763) or newer is required. This device is running build $build."
    exit 1
}

if ($arch -ne "X64" -and $arch -ne "Arm64") {
    Write-ErrMsg "Unsupported architecture '$arch'. Lumiere supports X64 and ARM64."
    exit 1
}

# 2. Administrator Elevation Check
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-WarnMsg "Administrator permissions required to install certificates and configure sideloading."
    Write-Step "Elevating installer with Administrator privileges..."
    
    $scriptPath = $MyInvocation.MyCommand.Definition
    if (-not $scriptPath -or -not (Test-Path $scriptPath)) {
        # Running from memory / one-liner (irm | iex)
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes((irm https://trailblazer-99.github.io/Lumiere/install.ps1)))
        $args = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"
    } else {
        $args = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`""
        if ($PackagePath) { $args += " -PackagePath `"$PackagePath`"" }
        if ($CertificatePath) { $args += " -CertificatePath `"$CertificatePath`"" }
        if ($SkipLaunch) { $args += " -SkipLaunch" }
        if ($NonInteractive) { $args += " -NonInteractive" }
    }

    try {
        Start-Process -FilePath "powershell.exe" -ArgumentList $args -Verb RunAs -Wait
        exit 0
    } catch {
        Write-ErrMsg "Elevation was cancelled or failed. Please run PowerShell as Administrator and run the installer."
        exit 1
    }
}

# 3. Enable Sideloading / Developer Mode
Write-Step "Verifying Sideloading and AppModel Configuration..."
try {
    $regPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
    Set-ItemProperty -Path $regPath -Name "AllowAllTrustedApps" -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $regPath -Name "AllowDevelopmentWithoutDevLicense" -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue
    Write-Ok "Sideloading policies confirmed (AllowAllTrustedApps = 1)."
} catch {
    Write-WarnMsg "Could not set sideloading registry policy automatically: $($_.Message)"
}

# 4. Certificate Installation (LocalMachine\TrustedPeople)
Write-Step "Configuring Security Certificate..."
$expectedThumbprint = "9BA44958B5874C2D566CC281F1F1634A6BBD5207"
$certFound = $false

try {
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new("TrustedPeople", "LocalMachine")
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $existing = $store.Certificates.Find(
        [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
        $expectedThumbprint,
        $false
    )
    if ($existing.Count -gt 0) {
        $certFound = $true
        Write-Ok "Lumiere certificate is already trusted in LocalMachine\TrustedPeople."
    } else {
        # Locate certificate file
        $certCandidate = $CertificatePath
        if (-not $certCandidate -or -not (Test-Path $certCandidate)) {
            $localDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
            if ($localDir -and (Test-Path (Join-Path $localDir "LumiereMediaPlayer.cer"))) {
                $certCandidate = Join-Path $localDir "LumiereMediaPlayer.cer"
            } elseif ($localDir -and (Test-Path (Join-Path $localDir "..\Signing\LumiereMediaPlayer.cer"))) {
                $certCandidate = Join-Path $localDir "..\Signing\LumiereMediaPlayer.cer"
            } else {
                # Download certificate from GitHub Pages
                $certCandidate = Join-Path $env:TEMP "LumiereMediaPlayer.cer"
                Write-Host "    Downloading certificate from GitHub Pages..." -ForegroundColor Gray
                Invoke-WebRequest -Uri "https://trailblazer-99.github.io/Lumiere/LumiereMediaPlayer.cer" -OutFile $certCandidate -UseBasicParsing -TimeoutSec 15
            }
        }

        $certObj = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($certCandidate)
        $store.Add($certObj)
        Write-Ok "Imported Lumiere certificate (Thumbprint: $($certObj.Thumbprint)) into LocalMachine\TrustedPeople."
        $certFound = $true
    }
    $store.Close()
} catch {
    Write-ErrMsg "Failed to import certificate: $($_.Message)"
    exit 1
}

# 5. Acquire Package (.msixbundle / .msix)
Write-Step "Resolving Installation Package..."
$targetPackage = $PackagePath

if (-not $targetPackage -or -not (Test-Path $targetPackage)) {
    # Check current directory
    $localDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
    if ($localDir) {
        $localBundles = Get-ChildItem -Path $localDir -Filter "*.msixbundle" -ErrorAction SilentlyContinue
        if ($localBundles.Count -gt 0) {
            $targetPackage = $localBundles[0].FullName
            Write-Ok "Found local bundle: $($localBundles[0].Name)"
        } else {
            # Search for architecture-specific msix
            $filterPattern = if ($arch -eq "Arm64") { "*arm64*.msix" } else { "*x64*.msix" }
            $localMsix = Get-ChildItem -Path $localDir -Filter $filterPattern -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($localMsix) {
                $targetPackage = $localMsix.FullName
                Write-Ok "Found local architecture package: $($localMsix.Name)"
            }
        }
    }
}

if (-not $targetPackage -or -not (Test-Path $targetPackage)) {
    Write-Host "    Querying latest release from GitHub API..." -ForegroundColor Gray
    try {
        $headers = @{ "User-Agent" = "Lumiere-Installer/$arch" }
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/trailblazer-99/Lumiere/releases/latest" -Headers $headers -TimeoutSec 15
        $bundleAsset = $release.assets | Where-Object { $_.name -like "*.msixbundle" } | Select-Object -First 1

        if (-not $bundleAsset) {
            throw "No .msixbundle asset found in latest GitHub release."
        }

        $downloadUrl = $bundleAsset.browser_download_url
        $tempBundle = Join-Path $env:TEMP $bundleAsset.name
        Write-Host "    Downloading $($bundleAsset.name) ($([math]::Round($bundleAsset.size / 1MB, 1)) MB)..." -ForegroundColor Cyan
        
        # Use curl if available for high-speed download with progress, otherwise Invoke-WebRequest
        if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
            & curl.exe -L -# -o $tempBundle $downloadUrl
        } else {
            Invoke-WebRequest -Uri $downloadUrl -OutFile $tempBundle -UseBasicParsing
        }

        if (Test-Path $tempBundle) {
            $targetPackage = $tempBundle
            Write-Ok "Downloaded release bundle to $tempBundle."
        } else {
            throw "Failed to download release bundle."
        }
    } catch {
        Write-ErrMsg "Failed to download package: $($_.Message)"
        Write-Host "    You can download the package manually from: https://github.com/trailblazer-99/Lumiere/releases/latest" -ForegroundColor Gray
        exit 1
    }
}

# 6. Install Package using Add-AppxPackage
Write-Step "Installing Lumiere Media Player for $arch..."
Write-Host "    Package: $targetPackage" -ForegroundColor Gray

try {
    # Check if app is running and ask to close
    $proc = Get-Process -Name "LumiereMediaPlayer" -ErrorAction SilentlyContinue
    if ($proc) {
        Write-WarnMsg "Closing running instance of Lumiere Media Player for clean installation..."
        $proc | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
    }

    Add-AppxPackage -Path $targetPackage -ForceApplicationShutdown
    Write-Ok "App installation completed successfully!"
} catch {
    Write-ErrMsg "Installation failed: $($_.Message)"
    Write-Host "    If an older version is conflicting, try uninstalling Lumiere from Settings > Installed apps first." -ForegroundColor Gray
    exit 1
}

# 7. Verification & Summary
Write-Step "Verifying Installed Package..."
$installed = Get-AppxPackage -Name "*LumireMediaPlayer*" -ErrorAction SilentlyContinue
if (-not $installed) {
    $installed = Get-AppxPackage -Name "*LumiereMediaPlayer*" -ErrorAction SilentlyContinue
}

if ($installed) {
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "  Lumiere Media Player v$($installed.Version) installed successfully!" -ForegroundColor Green
    Write-Host "  Architecture: $arch" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host ""

    if (-not $SkipLaunch -and -not $NonInteractive) {
        Write-Host "Launching Lumiere Media Player..." -ForegroundColor Cyan
        try {
            $appUserModelId = "$($installed.PackageFamilyName)!App"
            Start-Process "shell:AppsFolder\$appUserModelId"
        } catch {
            Write-Host "Lumiere is ready in your Start Menu." -ForegroundColor Gray
        }
    }
} else {
    Write-WarnMsg "Package installed but could not query package identity immediately. Check your Start Menu."
}

if (-not $NonInteractive) {
    Write-Host "`nPress Enter to exit..."
    Read-Host
}
