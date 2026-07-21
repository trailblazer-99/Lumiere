# Installation Guide for Lumière Media Player

This guide provides step-by-step instructions for installing Lumière Media Player on Windows 10 and Windows 11.

---

## System Requirements

- **Operating System**: Windows 10 version 1809 (Build 17763) or newer / Windows 11 (Recommended).
- **Architecture**: x64 or ARM64.
- **Runtime**: Microsoft Edge WebView2 Runtime (Included by default in Windows 11 and recent Windows 10 updates).

---

## Method 1: Web / App Installer (Recommended One-Click Install)

If you downloaded the `.appinstaller` file or are installing from the release site:

1. Open the `.appinstaller` file or click **Install** on the web release page (`https://trailblazer-99.github.io/Lumiere/LumiereMediaPlayer.appinstaller`).
2. The native **Windows App Installer** dialog will launch.
3. Click **Install** (or **Update** if an earlier version is present).
4. Launch **Lumière Media Player** from your Start Menu once complete!

---

## Method 2: Sideloading the `.msixbundle` / `.msix` Release Package

When downloading the pre-built `.msixbundle` or `.msix` package directly from GitHub Releases:

### Step 1: Enable Developer Mode (Required for Sideloading)

Because the app is package-signed with a project security certificate:

1. Open Windows **Settings** (`Win + I`).
2. Navigate to:
   - **Windows 11**: **System** > **For developers** (or **Privacy & security** > **For developers**).
   - **Windows 10**: **Update & Security** > **For developers**.
3. Toggle **Developer Mode** to **ON**.
4. Click **Yes** on the prompt confirming you want to enable Developer Mode.

### Step 2: Install the Security Certificate

Windows requires the package's public certificate (`LumiereMediaPlayer.cer`) to be installed in the Trusted Root Certification Authorities store:

1. Locate `LumiereMediaPlayer.cer` in your downloaded release folder (or inside `Signing/LumiereMediaPlayer.cer`).
2. Right-click `LumiereMediaPlayer.cer` and select **Install Certificate**.
3. In the Certificate Import Wizard:
   - Select **Local Machine** and click **Next**. (Click **Yes** if prompted by User Account Control / UAC).
   - Choose **Place all certificates in the following store**.
   - Click **Browse...** and select **Trusted Root Certification Authorities**.
   - Click **OK**, then click **Next**.
   - Click **Finish**. You should see a message stating *"The import was successful."*

### Step 3: Run the Package Installer

Now install the application:

#### Option A: Double-Click Package
1. Double-click `LumiereMediaPlayer.msixbundle` (or `LumiereMediaPlayer.msix`).
2. Click **Install** in the Windows App Installer window.

#### Option B: PowerShell Script (`Add-AppDevPackage.ps1`)
If your download includes `Add-AppDevPackage.ps1`:
1. Right-click `Add-AppDevPackage.ps1` and select **Run with PowerShell**.
2. Follow the on-screen prompts to automatically trust the certificate and install the package dependencies.

---

## Troubleshooting Installation Issues

### Error: "This app package is not signed with a trusted certificate" (`0x800B0109`)
- **Cause**: The `LumiereMediaPlayer.cer` certificate was not imported into the **Trusted Root Certification Authorities** store under **Local Machine**.
- **Fix**: Follow **Step 2** above carefully. Make sure to choose **Local Machine** (not Current User) and select **Trusted Root Certification Authorities**.

### Error: "Developer mode is disabled"
- **Cause**: Windows blocks non-Store MSIX packages unless Developer Mode or Sideloading is enabled.
- **Fix**: Follow **Step 1** above to turn on **Developer Mode** in Windows Settings.

### Error: "A higher version of this app is already installed"
- **Cause**: An existing development copy or newer version is installed on your machine.
- **Fix**: Uninstall any existing version of **Lumière Media Player** from **Settings > Apps > Installed apps**, then run the installer again.

### Error: "App Installer failed to update"
- **Fix**: Launch PowerShell as Administrator and run:
  ```powershell
  Add-AppxPackage -Path "C:\path\to\LumiereMediaPlayer.msixbundle"
  ```

---

## Launching the App

Once installed, **Lumière Media Player** will appear in your **Start Menu** and **Search**. You can pin it to your Taskbar for quick access!
