const TESERA_ORIGIN = "https://api.tesera.ru";

const ALLOWED_PATHS = [
  /^\/games\/[A-Za-z0-9._~-]+$/,
  /^\/collections\/(?:base\/)?(?:own|Own)\/[A-Za-z0-9._~-]+$/,
];

export default {
  async fetch(request, env) {
    if (request.method !== "GET") {
      return new Response("Method Not Allowed", { status: 405 });
    }

    if (!env.PROXY_TOKEN) {
      return new Response("Worker is not configured", { status: 503 });
    }

    const authorization = request.headers.get("Authorization");
    if (authorization !== `Bearer ${env.PROXY_TOKEN}`) {
      return new Response("Not Found", { status: 404 });
    }

    const incoming = new URL(request.url);
    if (!ALLOWED_PATHS.some((pattern) => pattern.test(incoming.pathname))) {
      return new Response("Not Found", { status: 404 });
    }

    const upstreamUrl = new URL(incoming.pathname + incoming.search, TESERA_ORIGIN);
    const upstreamHeaders = new Headers();
    upstreamHeaders.set("Accept", "application/json");
    upstreamHeaders.set("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
    upstreamHeaders.set("User-Agent", "oyinQ.Bot/1.0");
    upstreamHeaders.set("Referer", "https://tesera.ru/");

    const response = await fetch(upstreamUrl, {
      method: "GET",
      headers: upstreamHeaders,
      redirect: "follow",
    });

    const headers = new Headers();
    const contentType = response.headers.get("Content-Type");
    if (contentType) {
      headers.set("Content-Type", contentType);
    }
    headers.set("Cache-Control", "no-store");

    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers,
    });
  },
};
