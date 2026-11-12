# Installation Guide for Lumière Media Player

This guide provides complete instructions for installing Lumière Media Player seamlessly on any Windows 10 or Windows 11 device, including **x64** (Intel/AMD) and **ARM64** (Snapdragon X Elite, Surface Pro, ThinkPad, Dell, HP Copilot+ PCs).

---

## System Requirements

- **Operating System**: Windows 10 version 1809 (Build 17763) or newer / Windows 11 (Recommended).
- **Architecture**: x64 (AMD64/Intel) or ARM64 (Qualcomm Snapdragon X Series / Microsoft SQ).
- **Runtime**: Microsoft Edge WebView2 Runtime (Included by default in Windows 11 and recent Windows 10).

---

## Method 1: Universal One-Line PowerShell Install (Recommended for All Devices)

This method works automatically on **both ARM64 and x64** devices, fully handling certificate trust, sideloading permissions, and package deployment in a single step:

1. Press `Win + X` and select **Terminal (Admin)** or **Windows PowerShell (Admin)**.
2. Paste and run:
   ```powershell
   powershell -ExecutionPolicy Bypass -Command "irm https://trailblazer-99.github.io/Lumiere/install.ps1 | iex"
   ```
3. The installer detects your device architecture, trusts the release certificate into `LocalMachine\TrustedPeople`, and installs the native package.

---

## Method 2: Sideloading the `.msixbundle` Release Package

When downloading the pre-built `.msixbundle` directly from GitHub Releases:

### Step 1: Enable Developer Mode / Sideloading

1. Open Windows **Settings** (`Win + I`).
2. Navigate to:
   - **Windows 11**: **System** > **For developers** (or **Privacy & security** > **For developers**).
   - **Windows 10**: **Update & Security** > **For developers**.
3. Toggle **Developer Mode** to **ON** and confirm the prompt.

### Step 2: Install the Security Certificate

Windows requires the package certificate (`LumiereMediaPlayer.cer`) to be installed in the **Trusted People** store under **Local Machine**:

1. Download `LumiereMediaPlayer.cer` from the release assets or portal.
2. Double-click `LumiereMediaPlayer.cer` and click **Install Certificate...**.
3. Select **Local Machine** (requires administrator elevation) and click **Next**.
4. Choose **Place all certificates in the following store**.
5. Click **Browse...** and select **Trusted People**.
6. Click **OK**, then **Next**, then **Finish**.

### Step 3: Run the Package Installer

- **Option A (Automated)**: Double-click `Install.cmd` or right-click `scripts\Install-Lumiere.ps1` and select **Run with PowerShell**.
- **Option B (Direct MSIX)**: Double-click `LumiereMediaPlayer_*.msixbundle` and click **Install** in the Windows App Installer dialog.
- **Option C (PowerShell CLI)**:
  ```powershell
  Add-AppxPackage -Path ".\LumiereMediaPlayer_1.0.1.X_x64_arm64.msixbundle"
  ```

---

## In-App Updates & Privacy Policy

Lumière conforms to modern Windows application regulations and respects user bandwidth:
1. **User-Controlled Checking**: The installer does **not** perform unprompted background update checks on launch.
2. **Explicit Toggle**: Update checks only run when explicitly triggered by clicking **Check for updates** in Settings, or when the user toggles on **Check for updates automatically** in Settings.
3. **Seamless In-App Update**: When an update is detected, an update notification and badge appears. Users can click **Update** directly within the Settings page to download and apply the update without manually visiting external websites.
4. **Development Device Protection**: On devices where the application source code is actively being developed, automatic update overwrites are safely suppressed to prevent replacing development builds with public releases.

---

## Troubleshooting Installation Issues

### Error: "This app package is not signed with a trusted certificate" (`0x800B0109`)
- **Cause**: `LumiereMediaPlayer.cer` was not imported into **Local Machine > Trusted People** (often imported into Current User by mistake).
- **Fix**: Run Method 1 (Quick Install PowerShell command) or re-import the certificate selecting **Local Machine**.

### Error on ARM64 Laptop: "The app package cannot be opened" (`0x80D03002`)
- **Cause**: Windows 11 blocks opening `.appinstaller` files downloaded from web browsers (CVE-2021-43890), or AppInstaller fails on redirected download links.
- **Fix**: Download the raw `.msixbundle` directly and double-click it, or run Method 1 (PowerShell one-liner).

### Error: "A higher version of this app is already installed"
- **Cause**: A newer development build is installed on your device.
- **Fix**: Uninstall any existing version from **Settings > Apps > Installed apps**, then run the installer.
