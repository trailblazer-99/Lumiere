export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname;

    // Verify Secret App Token (prevents general scraping and rate limit abuse)
    const appToken = request.headers.get("X-Lumiere-App-Token");
    if (appToken !== env.APP_TOKEN) {
      return new Response("Unauthorized", { status: 401 });
    }

    // CORS Headers for standard compatibility
    const corsHeaders = {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, HEAD, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, X-Lumiere-App-Token",
    };

    if (request.method === "OPTIONS") {
      return new Response(null, { headers: corsHeaders });
    }

    try {
      // 1. TMDB Proxy Routing
      if (path.startsWith("/tmdb/")) {
        const targetPath = path.replace("/tmdb/", "");
        const targetUrl = new URL(`https://api.tmdb.org/3/${targetPath}${url.search}`);
        targetUrl.searchParams.set("api_key", env.TMDB_API_KEY);
        
        const response = await fetch(targetUrl.toString(), {
          method: request.method,
          headers: request.headers,
          body: request.body
        });
        return new Response(response.body, { ...response, headers: { ...response.headers, ...corsHeaders } });
      }

      // 2. Watchmode Proxy Routing
      if (path.startsWith("/watchmode/")) {
        const targetPath = path.replace("/watchmode/", "");
        const targetUrl = new URL(`https://api.watchmode.com/v1/${targetPath}${url.search}`);
        targetUrl.searchParams.set("apiKey", env.WATCHMODE_API_KEY);
        
        const response = await fetch(targetUrl.toString(), {
          method: request.method,
          headers: request.headers,
          body: request.body
        });
        return new Response(response.body, { ...response, headers: { ...response.headers, ...corsHeaders } });
      }

      // 3. Movie of the Night (MOTN) Proxy Routing
      if (path.startsWith("/motn/")) {
        const targetPath = path.replace("/motn/", "");
        const targetUrl = new URL(`https://api.movieofthenight.com/v4/${targetPath}${url.search}`);
        
        const headers = new Headers(request.headers);
        headers.set("X-API-Key", env.MOTN_API_KEY);
        
        const response = await fetch(targetUrl.toString(), {
          method: request.method,
          headers: headers,
          body: request.body
        });
        return new Response(response.body, { ...response, headers: { ...response.headers, ...corsHeaders } });
      }

      // 4. MusicAPI Proxy Routing
      if (path.startsWith("/musicapi/")) {
        const targetPath = path.replace("/musicapi/", "");
        const targetUrl = new URL(`https://api.musicapi.com/public/${targetPath}${url.search}`);
        
        const headers = new Headers(request.headers);
        headers.set("Authorization", `Bearer ${env.MUSIC_API_KEY}`);
        
        const response = await fetch(targetUrl.toString(), {
          method: request.method,
          headers: headers,
          body: request.body
        });
        return new Response(response.body, { ...response, headers: { ...response.headers, ...corsHeaders } });
      }

      // 5. YouTube Proxy Routing
      if (path.startsWith("/youtube/")) {
        const targetPath = path.replace("/youtube/", "");
        const targetUrl = new URL(`https://www.googleapis.com/youtube/v3/${targetPath}${url.search}`);
        targetUrl.searchParams.set("key", env.YOUTUBE_API_KEY);
        
        const response = await fetch(targetUrl.toString(), {
          method: request.method,
          headers: request.headers,
          body: request.body
        });
        return new Response(response.body, { ...response, headers: { ...response.headers, ...corsHeaders } });
      }

      return new Response("Service Route Not Found", { status: 404 });
    } catch (err) {
      return new Response("Proxy Error: " + err.message, { status: 500 });
    }
  }
};
