# API Proxy Deployment Guide (Cloudflare Workers)

To secure your API keys from being reverse-engineered or extracted from the desktop application binaries, FluentMediaPlayer (Lumiere) routes API requests through a secure serverless proxy.

Follow this quick guide to deploy your own proxy in under 5 minutes for free (Cloudflare's Free Tier includes **100,000 requests per day** with no credit card required).

---

## Step 1: Create a Cloudflare Worker
1. Log in or sign up at the [Cloudflare Dashboard](https://dash.cloudflare.com/).
2. Navigate to **Workers & Pages** in the left sidebar.
3. Click **Create** $\rightarrow$ **Create Worker**.
4. Name your Worker (e.g., `lumiere-api-proxy`) and click **Deploy**.
5. Once deployed, click **Edit Code** to open the web editor.

---

## Step 2: Add the Proxy Code
1. Open [Scripts/cloudflare_worker.js](file:///c:/Users/soura/source/repos/FluentMediaPlayer/Scripts/cloudflare_worker.js) in your codebase.
2. Copy the entire contents of that file.
3. In the Cloudflare web editor, delete any default code in `worker.js` and paste the copied proxy code.
4. Click **Save and deploy**.

---

## Step 3: Configure Environment Variables (API Keys)
To store your keys securely on Cloudflare:
1. Go back to your Worker's dashboard in Cloudflare.
2. Navigate to **Settings** $\rightarrow$ **Variables** (under the "Variables and Secrets" section).
3. Add the following **Secrets** (encrypts them at rest):
   - `APP_TOKEN`: A secret token of your choice that the C# app will send to verify itself (Default: `Lumiere-Desktop-App-Token-2026`).
   - `TMDB_API_KEY`: Your The Movie Database API Key.
   - `WATCHMODE_API_KEY`: Your Watchmode API Key.
   - `MOTN_API_KEY`: Your Movie of the Night API Key.
   - `MUSIC_API_KEY`: Your MusicAPI.com Bearer Token.
   - `YOUTUBE_API_KEY`: Your Google/YouTube Data API v3 Key.
4. Click **Deploy** to save the variables.

---

## Step 4: Configure the Client App
Now tell your desktop app to use the new proxy:
1. Copy your Worker's public URL from the Cloudflare Dashboard (e.g., `https://lumiere-api-proxy.yourname.workers.dev`).
2. Open your local `appsettings.json` file.
3. Set `"UseProxy"` to `true`.
4. Paste your Worker's public URL into `"ProxyBaseUrl"`:
   ```json
   "UseProxy": true,
   "ProxyBaseUrl": "https://lumiere-api-proxy.yourname.workers.dev",
   "ProxyAppToken": "Lumiere-Desktop-App-Token-2026"
   ```
5. Run the app! All API calls will now route securely through your serverless proxy.

---

> [!NOTE]
> **Resilient Fallback:** If the proxy goes down, fails, or is not configured (`ProxyBaseUrl` is left blank), the application will automatically and transparently fall back to making direct API calls from the client using the local keys. This prevents the app from crashing and ensures continuous functionality.
