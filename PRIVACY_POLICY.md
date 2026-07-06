# Privacy Policy for FluentMediaPlayer (Lumiere)

**Last Updated: July 6, 2026**

This Privacy Policy describes how FluentMediaPlayer (also referred to as "the Application", "Lumiere", "we", "us", or "our") handles information when you use the software. 

We are committed to protecting your privacy. The Application is designed to process your media library locally and query public streaming APIs without collecting, storing, or transmitting your personal data.

---

## 1. Information We Access and Process

The Application accesses and processes information strictly to facilitate playback and media cataloging:

### A. Local Files and Directory Metadata
* **Access**: The Application accesses directories on your local device containing audio and video files, but only when you explicitly select them to add to your library.
* **Processing**: We read metadata (such as track title, album, artist, and duration) to display them in your local library interface.
* **Storage**: This metadata is cached locally on your device in an isolated application folder. It is **never** uploaded to our servers, shared with third parties, or collected by us.

### B. Geolocation / Region Detection
* **Access**: The Application makes request calls to free public IP-lookup services (such as `ip-api.com` and `ipwho.is`) to detect your country code.
* **Usage**: This is used exclusively to filter Watch Provider availability (e.g., TMDB / Watchmode streaming links) so the listings are relevant to your geographical region. We do not store, log, or track your IP address or precise location.

### C. Developer Credentials
* **Access**: For certain integrations (such as Twitch), the Application allows you to input your own developer credentials (e.g., Twitch Client Secret) to bypass rate limiting.
* **Storage**: These credentials are encrypted and stored locally on your device using Windows App isolated settings storage (`LocalSettings`). They are never shared, transmitted, or stored on external servers.

---

## 2. Third-Party Services

The Application interacts with third-party application programming interfaces (APIs) to display streaming content. When you use these features, the third-party providers may collect details in accordance with their own privacy policies:

* **YouTube Data API**: Used to search and retrieve public video listings. By using this feature, you agree to be bound by the [YouTube Terms of Service](https://www.youtube.com/t/terms) and [Google Privacy Policy](https://policies.google.com/privacy).
* **Twitch Helix API & WebView2**: Used to browse streams and display live broadcasts. Transactions or log-ins inside the Twitch player are processed directly by Twitch Interactive, Inc. in accordance with the [Twitch Privacy Notice](https://www.twitch.tv/p/en/legal/privacy-notice/).
* **The Movie Database (TMDB) & Watchmode**: Used to query movie, TV show, and streaming availability catalog metadata.
* **MusicAPI & iTunes Search API**: Used to query track listings and cross-platform streaming links (e.g., Spotify, Apple Music, YouTube Music).

We do not control and are not responsible for the privacy practices of these third-party services. We encourage you to read their respective privacy policies.

---

## 3. Data Collection and Retention

* **No Personal Data Collection**: We do not collect, harvest, store, or share any personally identifiable information (such as names, email addresses, physical addresses, or device identifiers).
* **Telemetry**: The Application does not contain any third-party tracking, profiling, or analytical analytics SDKs.

---

## 4. Children's Privacy

Since the Application does not collect any personal data, it does not knowingly collect or request personal information from children.

---

## 5. Changes to This Privacy Policy

We may update this Privacy Policy from time to time to reflect changes in our practices or for operational, legal, or regulatory reasons. Any updates will be posted directly within the repository.

---

## 6. Contact Us

If you have any questions or feedback regarding this Privacy Policy or the security of your data, you can open an issue in the project's GitHub repository:
[Lumiere GitHub Repository](https://github.com/trailblazer-99/Lumiere)
