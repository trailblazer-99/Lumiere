# CheckInstaller.ps1 - Verifies that the Lumiere Media Player install/update
# pipeline is healthy from the user's perspective. Run this on any machine
# where the app is or will be installed.

param(
    [switch]$Verbose
)

$exitCode = 0

function Write-Ok   ($msg) { Write-Host "[OK]   $msg" -ForegroundColor Green }
function Write-Warn ($msg) { Write-Host "[WARN] $msg" -ForegroundColor Yellow; $script:exitCode = 1 }
function Write-Fail ($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red;    $script:exitCode = 1 }

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Lumiere Media Player - Install Check"     -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Windows version
Write-Host "--- Windows Version ---"
$os = [System.Environment]::OSVersion.Version
$build = $os.Build
$friendlyName = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction SilentlyContinue).DisplayVersion
if ($friendlyName) {
    $verMsg = "Windows version: $friendlyName, Build $build"
    Write-Ok $verMsg
} else {
    Write-Ok "Windows build: $build"
}

if ($build -lt 17763) {
    Write-Fail "Minimum supported build is 17763 - Windows 10 version 1809. This machine is too old."
} elseif ($build -lt 19041) {
    Write-Warn "Build $build is below 19041. The .appinstaller one-click method requires Windows 10 2004+. Manual .msixbundle sideloading will still work."
}
Write-Host ""

# 2. Developer Mode
Write-Host "--- Developer Mode ---"
try {
    $devMode = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" -ErrorAction Stop).AllowDevelopmentWithoutDevLicense
    if ($devMode -eq 1) {
        Write-Ok "Developer Mode is ON."
    } else {
        Write-Fail "Developer Mode is OFF. Go to Settings > System > For developers and turn it ON."
    }
} catch {
    Write-Warn "Could not read Developer Mode registry key. Ensure Developer Mode is enabled in Settings."
}
Write-Host ""

# 3. Certificate trust
Write-Host "--- Certificate Trust ---"
$expectedThumbprint = "9BA44958B5874C2D566CC281F1F1634A6BBD5207"

$found = $false
try {
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new("TrustedPeople", "LocalMachine")
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    $certs = $store.Certificates.Find(
        [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
        $expectedThumbprint,
        $false
    )
    if ($certs.Count -gt 0) {
        $cert = $certs[0]
        $certMsg = "Lumiere certificate is trusted - Thumbprint: $($cert.Thumbprint)"
        Write-Ok $certMsg
        if ($cert.NotAfter -le [datetime]::Now) {
            Write-Fail "Certificate EXPIRED on $($cert.NotAfter). A new certificate is needed."
        } elseif ($cert.NotAfter -le [datetime]::Now.AddDays(90)) {
            Write-Warn "Certificate expires soon: $($cert.NotAfter)."
        } else {
            Write-Ok "Certificate valid until $($cert.NotAfter)."
        }
        $found = $true
    }
    $store.Close()
} catch {
    Write-Warn "Could not open LocalMachine\TrustedPeople store. Try running as Administrator."
}

if (-not $found) {
    Write-Fail "Lumiere certificate NOT found in the Trusted People store."
    Write-Host "       Download LumiereMediaPlayer.cer from https://trailblazer-99.github.io/Lumiere/" -ForegroundColor Gray
    Write-Host "       Right-click > Install Certificate > Local Machine > Trusted People." -ForegroundColor Gray
}
Write-Host ""

# 4. App Installer availability
Write-Host "--- App Installer ---"
$appInstaller = Get-AppxPackage -Name "Microsoft.DesktopAppInstaller" -ErrorAction SilentlyContinue
if ($appInstaller) {
    $aiMsg = "App Installer is present - v$($appInstaller.Version)"
    Write-Ok $aiMsg
    $installerVersion = [version]$appInstaller.Version
    if ($installerVersion -lt [version]"1.19") {
        Write-Warn "App Installer version is old. Update it from the Microsoft Store for best .appinstaller support."
    }
} else {
    Write-Fail "Microsoft App Installer is NOT installed. Install it from the Microsoft Store."
}
Write-Host ""

# 5. Network connectivity to GitHub
Write-Host "--- Network Connectivity ---"
$urls = @(
    @{ Label = "GitHub Pages - .appinstaller"; Url = "https://trailblazer-99.github.io/Lumiere/LumiereMediaPlayer.appinstaller" },
    @{ Label = "GitHub Releases - bundle download"; Url = "https://github.com/trailblazer-99/Lumiere/releases" }
)

foreach ($entry in $urls) {
    try {
        $response = Invoke-WebRequest -Uri $entry.Url -Method Head -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
        $netMsg = "$($entry.Label) - reachable, HTTP $($response.StatusCode)"
        Write-Ok $netMsg
    } catch {
        Write-Fail "$($entry.Label) - UNREACHABLE. Check your internet or firewall."
        if ($Verbose) {
            Write-Host "       URL: $($entry.Url)" -ForegroundColor Gray
            Write-Host "       Error: $($_.Exception.Message)" -ForegroundColor Gray
        }
    }
}
Write-Host ""

# 6. Existing Lumiere installation
Write-Host "--- Installed App ---"
$app = Get-AppxPackage -Name "*LumireMediaPlayer*" -ErrorAction SilentlyContinue
if (-not $app) {
    $app = Get-AppxPackage -Name "*LumiereMediaPlayer*" -ErrorAction SilentlyContinue
}

if ($app) {
    Write-Ok "Lumiere Media Player is installed: v$($app.Version)"
    Write-Host "       Package: $($app.PackageFullName)" -ForegroundColor Gray
    Write-Host "       Install location: $($app.InstallLocation)" -ForegroundColor Gray
} else {
    Write-Host "[INFO] Lumiere Media Player is not currently installed." -ForegroundColor Cyan
}
Write-Host ""

# Summary
Write-Host "=========================================" -ForegroundColor Cyan
if ($exitCode -eq 0) {
    Write-Host " All checks passed! Ready to install/update." -ForegroundColor Green
} else {
    Write-Host " Some checks failed - review the output above." -ForegroundColor Yellow
}
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

exit $exitCode
