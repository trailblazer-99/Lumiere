# API Proxy Deployment Guide (Azure Functions)

This guide shows you how to deploy the secure API proxy backend using **Microsoft Azure Functions** directly from Visual Studio 2026.

Azure Functions consumption tier is **free for the first 1,000,000 requests per month**, meaning this setup will be completely free of cost for your app's users.

---

## Step 1: Create the Azure Functions Project in Visual Studio 2026
1. Open Visual Studio 2026 and select **Create a new project**.
2. Search for **Azure Functions** (C#) and click **Next**.
3. Name your project (e.g., `LumiereProxy`) and place it in a separate folder.
4. Select **.NET 8.0 (Long Term Support) - Isolated Worker** or **.NET 9.0**.
5. Set the trigger type to **Http trigger**.
6. Set the **Authorization level** to **Anonymous** (the proxy will verify the custom token in code) and click **Create**.

---

## Step 2: Implement the Proxy Code
Replace the default code in `Function1.cs` (or create a new file named `LumiereProxy.cs`) with the following C# implementation:

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Lumiere.Proxy
{
    public class ApiProxyFunction
    {
        private readonly ILogger<ApiProxyFunction> _logger;
        private readonly HttpClient _httpClient;

        public ApiProxyFunction(ILogger<ApiProxyFunction> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
        }

        [Function("Proxy")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "{service}/{*remainder}")] HttpRequest req,
            string service,
            string remainder)
        {
            // 1. Verify Secret App Token (prevents scrapers from abusing your proxy)
            if (!req.Headers.TryGetValue("X-Lumiere-App-Token", out var appToken) || 
                appToken != Environment.GetEnvironmentVariable("APP_TOKEN"))
            {
                return new UnauthorizedResult();
            }

            var query = req.QueryString.Value ?? "";
            string targetUrl = "";
            HttpResponseMessage response;

            try
            {
                switch (service.ToLower())
                {
                    case "tmdb":
                        string tmdbKey = Environment.GetEnvironmentVariable("TMDB_API_KEY") ?? "";
                        string connector = remainder.Contains("?") ? "&" : "?";
                        targetUrl = $"https://api.tmdb.org/3/{remainder}{query}{connector}api_key={tmdbKey}";
                        response = await _httpClient.GetAsync(targetUrl);
                        break;

                    case "watchmode":
                        string watchmodeKey = Environment.GetEnvironmentVariable("WATCHMODE_API_KEY") ?? "";
                        string wConnector = remainder.Contains("?") ? "&" : "?";
                        targetUrl = $"https://api.watchmode.com/v1/{remainder}{query}{wConnector}apiKey={watchmodeKey}";
                        response = await _httpClient.GetAsync(targetUrl);
                        break;

                    case "motn":
                        targetUrl = $"https://api.movieofthenight.com/v4/{remainder}{query}";
                        using (var motnReq = new HttpRequestMessage(HttpMethod.Get, targetUrl))
                        {
                            motnReq.Headers.Add("X-API-Key", Environment.GetEnvironmentVariable("MOTN_API_KEY"));
                            response = await _httpClient.SendAsync(motnReq);
                        }
                        break;

                    case "musicapi":
                        targetUrl = $"https://api.musicapi.com/public/{remainder}{query}";
                        using (var musicReq = new HttpRequestMessage(HttpMethod.Get, targetUrl))
                        {
                            musicReq.Headers.Add("Authorization", $"Bearer {Environment.GetEnvironmentVariable("MUSIC_API_KEY")}");
                            response = await _httpClient.SendAsync(musicReq);
                        }
                        break;

                    case "youtube":
                        string ytKey = Environment.GetEnvironmentVariable("YOUTUBE_API_KEY") ?? "";
                        string ytConnector = remainder.Contains("?") ? "&" : "?";
                        targetUrl = $"https://www.googleapis.com/youtube/v3/{remainder}{query}{ytConnector}key={ytKey}";
                        response = await _httpClient.GetAsync(targetUrl);
                        break;

                    default:
                        return new NotFoundObjectResult("Service Route Not Found");
                }

                var content = await response.Content.ReadAsStringAsync();
                var result = new ContentResult
                {
                    Content = content,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                    StatusCode = (int)response.StatusCode
                };
                return result;
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult($"Proxy Error: {ex.Message}");
            }
        }
    }
}
```

> [!TIP]
> Ensure you add the HTTP Client Factory dependency in `Program.cs`:
> ```csharp
> var host = new HostBuilder()
>     .ConfigureFunctionsWorkerDefaults()
>     .ConfigureServices(services => {
>         services.AddHttpClient();
>     })
>     .Build();
> ```

---

## Step 3: Publish to Azure
1. Right-click the `LumiereProxy` project in Visual Studio.
2. Select **Publish...**
3. Select **Azure** $\rightarrow$ **Azure Function App (Windows)**.
4. Sign in to your Azure account and select/create a free-tier App Service Plan.
5. Click **Publish**.

---

## Step 4: Configure App Settings on Azure Portal
Open the [Azure Portal](https://portal.azure.com/), find your Function App, and go to **Settings** $\rightarrow$ **Configuration** $\rightarrow$ **Application Settings**. Add the following key-value pairs:

* `APP_TOKEN`: `Lumiere-Desktop-App-Token-2026`
* `TMDB_API_KEY`: *Your Key*
* `WATCHMODE_API_KEY`: *Your Key*
* `MOTN_API_KEY`: *Your Key*
* `MUSIC_API_KEY`: *Your Key*
* `YOUTUBE_API_KEY`: *Your Key*

---

## Step 5: Update the Client App
1. Copy the Function App's base URL (e.g., `https://lumiere-proxy.azurewebsites.net/api`).
2. Open your local `appsettings.json` file.
3. Paste the URL under `"ProxyBaseUrl"`:
   ```json
   "UseProxy": true,
   "ProxyBaseUrl": "https://lumiere-proxy.azurewebsites.net/api"
   ```
