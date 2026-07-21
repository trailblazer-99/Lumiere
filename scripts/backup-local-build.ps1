# PowerShell Script: Create Local Backup Build for Lumière Media Player
# Generates a timestamped local MSIX release package in Backups/ as a fail-safe fallback

Param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " Lumière Media Player - Local Fallback Backup Build" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

# 1. Define Backup Target Directory
$ProjectRoot = $PSScriptRoot | Split-Path -Parent
$BackupDir = Join-Path $ProjectRoot "Backups"
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$TargetBackupPath = Join-Path $BackupDir "Backup_$Timestamp"

if (-not (Test-Path $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir | Out-Null
    Write-Host "[+] Created Backups directory: $BackupDir" -ForegroundColor Green
}

New-Item -ItemType Directory -Path $TargetBackupPath | Out-Null
Write-Host "[+] Staging backup folder: $TargetBackupPath" -ForegroundColor Green

# 2. Execute Local Clean Build
Write-Host "[*] Building local $Configuration ($Platform) package..." -ForegroundColor Yellow
Set-Location $ProjectRoot

dotnet build LumiereMediaPlayer.csproj `
    -c $Configuration `
    -p:Platform=$Platform `
    -p:GenerateAppxPackageOnBuild=true `
    -p:UapAppxPackageBuildMode=SideLoadOnly `
    -p:AppxBundle=Never

if ($LASTEXITCODE -ne 0) {
    Write-Error "Local build failed! Backup cancelled."
    exit 1
}

# 3. Copy Compiled Binaries and MSIX Packages to Local Backup Store
$SourceBinPath = Join-Path $ProjectRoot "bin\$Platform\$Configuration\net10.0-windows10.0.19041.0\win-$Platform"
$SourceAppPackages = Join-Path $ProjectRoot "AppPackages"

if (Test-Path $SourceBinPath) {
    Copy-Item -Path "$SourceBinPath\*" -Destination $TargetBackupPath -Recurse -Force
    Write-Host "[+] Staged compiled binaries to $TargetBackupPath" -ForegroundColor Green
}

if (Test-Path $SourceAppPackages) {
    $TargetPackageStore = Join-Path $TargetBackupPath "MSIX_Packages"
    New-Item -ItemType Directory -Path $TargetPackageStore -Force | Out-Null
    Get-ChildItem $SourceAppPackages | Copy-Item -Destination $TargetPackageStore -Recurse -Force
    Write-Host "[+] Staged MSIX packages to $TargetPackageStore" -ForegroundColor Green
}

# 4. Copy Sideloading Certificates
$CertPath = Join-Path $ProjectRoot "Signing\LumiereMediaPlayer.cer"
if (Test-Path $CertPath) {
    Copy-Item -Path $CertPath -Destination $TargetBackupPath -Force
    Write-Host "[+] Staged sideloading certificate" -ForegroundColor Green
}

# 5. Create Local Git Fallback Tag
try {
    $GitTag = "local-backup-$Timestamp"
    git tag -a $GitTag -m "Local stable backup build $Timestamp"
    Write-Host "[+] Created local Git restore tag: $GitTag" -ForegroundColor Green
} catch {
    Write-Host "[!] Git tag creation skipped (Git not available or clean state)." -ForegroundColor Yellow
}

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host " SUCCESS! Local Fallback Backup Complete" -ForegroundColor Green
Write-Host " Location: $TargetBackupPath" -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Cyan
