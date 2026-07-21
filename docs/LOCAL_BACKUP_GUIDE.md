# Local Fallback & Backup Guide
## Lumière Media Player

This guide explains how to use the automated **Local Backup & Fallback System** for Lumière Media Player. If Azure Pipelines, cloud CI/CD, or a remote repository ever mangles a build or experiences an outage, you can instantly fall back to a 100% verified local build on your computer.

---

## 🛡️ 1. Create a Local Fallback Backup (1-Click Command)

Run this PowerShell command in your project directory at any time to create a timestamped, standalone local backup:

```powershell
.\scripts\backup-local-build.ps1
```

### What This Script Does:
1. **Compiles a Local Release Package**: Builds a clean `.NET 10.0` WinUI 3 x64 release package locally.
2. **Creates a Stamp in `Backups/`**: Saves compiled binaries, `.msix` packages, and public certificates in `Backups/Backup_YYYYMMDD_HHMMSS/`.
3. **Creates a Local Git Restore Point**: Tags your Git repository with `local-backup-YYYYMMDD_HHMMSS` so you can revert source code instantly if needed.

---

## ⏪ 2. How to Restore from a Local Backup

If an Azure cloud build ever fails or mangles your app, choose one of the following fallback restore methods:

### Option A: Install from Local Backup Package (Quickest - 10 Seconds)
1. Open your project folder > go to `Backups/`.
2. Open the latest backup folder (e.g., `Backups/Backup_20260721_151300/MSIX_Packages/`).
3. Double-click the `.msix` or `.msixbundle` package to install and run your app instantly!

---

### Option B: Revert Local Code to Known-Good State via Git
To revert your source code back to your latest local backup tag:

```powershell
# View all local backup restore points
git tag -l "local-backup-*"

# Revert source code to your chosen backup tag
git checkout tags/local-backup-YYYYMMDD_HHMMSS
```

---

### Option C: Create a Permanent Local Stable Branch
To freeze a known-good local stable version on your PC:

```powershell
# Create a local stable branch
git branch local-stable-v1

# Whenever Azure pipeline has an issue, switch to your local stable branch:
git checkout local-stable-v1
```

---

## 📂 Backup Directory Layout

```
Lumière Media Player/
├── Backups/                        # Storage for local fallback builds (ignored by Git)
│   └── Backup_20260721_151300/     # Timestamped fallback package
│       ├── LumiereMediaPlayer.exe  # Standalone compiled desktop client
│       ├── LumiereMediaPlayer.cer  # Public sideloading certificate
│       └── MSIX_Packages/          # Tested local MSIX installer package
├── scripts/
│   └── backup-local-build.ps1      # 1-Click backup generator script
└── LOCAL_BACKUP_GUIDE.md           # Local fallback operations manual
```
