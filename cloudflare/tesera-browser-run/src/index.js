import puppeteer from "@cloudflare/puppeteer";

const TESERA_URL = "https://api.tesera.ru/games/carcassonne";

function json(body, status = 200, headers = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      ...headers,
    },
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method !== "GET") {
      return new Response("Not found", { status: 404 });
    }

    if (url.pathname === "/health") {
      return json({ status: "ok" });
    }

    // POC only: expose exactly one fixed Tesera resource so this cannot be
    // abused as a generic browser/proxy while we verify Browser Run egress.
    if (url.pathname !== "/games/carcassonne" || url.search) {
      return new Response("Not found", { status: 404 });
    }

    let browser;
    try {
      browser = await puppeteer.launch(env.BROWSER);
      const page = await browser.newPage();
      await page.setExtraHTTPHeaders({
        Accept: "application/json",
        "Accept-Language": "ru-RU,ru;q=0.9,en;q=0.8",
        Referer: "https://tesera.ru/",
      });

      const upstream = await page.goto(TESERA_URL, {
        waitUntil: "domcontentloaded",
        timeout: 20_000,
      });

      if (!upstream) {
        return json({
          ok: false,
          reason: "no_upstream_response",
        }, 502);
      }

      const upstreamStatus = upstream.status();
      const upstreamHeaders = upstream.headers();
      const body = await upstream.text();

      if (upstreamStatus !== 200) {
        return json({
          ok: false,
          reason: "upstream_http_error",
          upstreamStatus,
          contentType: upstreamHeaders["content-type"] ?? null,
        }, 502);
      }

      try {
        JSON.parse(body);
      } catch {
        return json({
          ok: false,
          reason: "upstream_not_json",
          upstreamStatus,
          contentType: upstreamHeaders["content-type"] ?? null,
        }, 502);
      }

      return new Response(body, {
        status: 200,
        headers: {
          "content-type": "application/json; charset=utf-8",
          "cache-control": "no-store",
          "x-oyinq-upstream-status": String(upstreamStatus),
        },
      });
    } catch (error) {
      console.error("Tesera Browser Run probe failed", error);
      return json({
        ok: false,
        reason: "browser_error",
      }, 502);
    } finally {
      if (browser) {
        await browser.close().catch(() => {});
      }
    }
  },
};
