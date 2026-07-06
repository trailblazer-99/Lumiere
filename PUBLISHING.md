# Microsoft Store Publishing Guide

This guide details the step-by-step process for preparing, packaging, and submitting the FluentMediaPlayer WinUI 3 application to the Microsoft Store.

---

## Step 1: Create a Partner Center App Reservation

Before you can package the app for the Store, you must reserve your app's name:
1. Log in to the [Microsoft Partner Center](https://partner.microsoft.com/dashboard).
2. Navigate to **Windows & Xbox** and click **Create a new app**.
3. Enter your desired name (e.g., `FluentMediaPlayer`) and click **Reserve product name**.

---

## Step 2: Associate the Project with the Microsoft Store

Associating the project link-signs your AppxManifest with the developer identity assigned by the Store:
1. Open the project in **Visual Studio 2022**.
2. Right-click the **FluentMediaPlayer** project in Solution Explorer.
3. Select **Publish** > **Associate App with the Store...**
4. Click **Next** and sign in with the developer Microsoft Account associated with your Partner Center registration.
5. Select the reserved app name from the list and click **Associate**.
6. This process updates the `Package.appxmanifest` `<Identity>` properties (Name, Publisher, PublisherDisplayName) and creates a local `Package.StoreAssociation.xml` file.

---

## Step 3: Prepare the Package Manifest

Double-check the settings in your `Package.appxmanifest`:
1. **Capabilities**: Review requested capabilities. Currently, the app requests:
   - `runFullTrust` (Required for WinUI 3 desktop applications).
   - `systemAIModels` (For native ML/AI integration, if utilized).
2. **Visual Assets**: Ensure all assets in `Assets/` are high-resolution.
   - Visual Studio's Manifest Editor has a **Visual Assets** tab to easily generate all required scale variants from a single high-quality master image.
   - Minimally required store assets (already included):
     - `StoreLogo.png`
     - `Square150x150Logo.scale-200.png`
     - `Square44x44Logo.scale-200.png`
     - `Wide310x150Logo.scale-200.png`
     - `SplashScreen.scale-200.png`

---

## Step 4: Generate the MSIX Store Upload Package

1. In Visual Studio, right-click the **FluentMediaPlayer** project.
2. Select **Publish** > **Create App Packages...**
3. Choose **Microsoft Store** (using the associated app name) and click **Next**.
4. Select **Release** as the build configuration.
5. Select architectures:
   - **x64** (Standard Windows desktop)
   - **ARM64** (Windows on ARM)
   - *Note: Leave x86 unchecked as modern WinUI 3 targets x64/ARM64.*
6. Set the version increment logic (e.g., `1.0.1.0`).
7. Click **Create**.
8. Once the build completes, click on the output location link. You will find a folder named `AppPackages/` containing a `.msixupload` file (e.g., `FluentMediaPlayer_1.0.1.0_x64_arm64.msixupload`).

---

## Step 5: Submit the Package for Certification

1. Go to the [Partner Center Dashboard](https://partner.microsoft.com/dashboard).
2. Click on your reserved app name and select **Start submission**.
3. Under **Packages**, drag and drop your generated `.msixupload` file.
4. Fill out the submission details:
   - **Pricing and availability**: Free, or set pricing.
   - **Properties**: Set Category (Music & Video) and details.
   - **Age ratings**: Fill out the questionnaire for the appropriate rating.
   - **Store listings**: Add description, keywords, features, and upload at least 4-5 high-quality application screenshots showing the Fluent UI layout.
5. Click **Submit to the Store**.
6. The app will enter Microsoft's automated and manual certification review (usually takes between 24-72 hours). Once passed, it will be published to the Store!
