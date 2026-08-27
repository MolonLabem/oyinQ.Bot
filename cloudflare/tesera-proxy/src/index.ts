const UPSTREAM_ORIGIN = "https://api.tesera.ru";
const AUTH_SCHEME = "Bearer ";
const COLLECTION_PATH = /^\/collections\/(?:base\/)?(?:own|Own)\/([^/]+)$/;
const GAME_PATH = /^\/games\/([^/]+)$/;
const COLLECTION_QUERY_KEYS = new Set(["GamesType", "Limit", "Offset"]);

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    try {
      if (request.method !== "GET") {
        log("warn", "unsupported_method", request.method, url.pathname);
        return jsonError("Method not allowed", 405, { Allow: "GET" });
      }

      if (!(await isAuthorized(request.headers.get("Authorization"), env.TESERA_PROXY_SECRET))) {
        log("warn", "authentication_failure", request.method, url.pathname);
        return jsonError("Unauthorized", 401);
      }

      if (!isAllowedRequest(url)) {
        log("warn", "rejected_path", request.method, url.pathname);
        return jsonError("Not found", 404);
      }

      const upstreamUrl = new URL(url.pathname + url.search, UPSTREAM_ORIGIN);
      const headers = new Headers({
        Accept: request.headers.get("Accept") ?? "application/json",
        "User-Agent": "OyinQ-Tesera-Proxy/1.0",
      });
      const upstreamResponse = await fetch(upstreamUrl, {
        method: "GET",
        headers,
        redirect: "manual",
      });

      log(
        upstreamResponse.ok ? "log" : "warn",
        upstreamResponse.ok ? "proxy_success" : "tesera_http_failure",
        request.method,
        url.pathname,
        upstreamResponse.status,
      );

      const responseHeaders = new Headers();
      const contentType = upstreamResponse.headers.get("Content-Type");
      if (contentType) responseHeaders.set("Content-Type", contentType);

      return new Response(upstreamResponse.body, {
        status: upstreamResponse.status,
        statusText: upstreamResponse.statusText,
        headers: responseHeaders,
      });
    } catch (error) {
      console.error(JSON.stringify({
        event: "worker_error",
        method: request.method,
        path: url.pathname,
        error: error instanceof Error ? error.message : "Unknown error",
      }));
      return jsonError("Bad gateway", 502);
    }
  },
} satisfies ExportedHandler<Env>;

async function isAuthorized(header: string | null, expectedSecret: string | undefined): Promise<boolean> {
  if (!expectedSecret) return false;

  const provided = header?.startsWith(AUTH_SCHEME) ? header.slice(AUTH_SCHEME.length) : "";
  const encoder = new TextEncoder();
  const [providedHash, expectedHash] = await Promise.all([
    crypto.subtle.digest("SHA-256", encoder.encode(provided)),
    crypto.subtle.digest("SHA-256", encoder.encode(expectedSecret)),
  ]);
  return crypto.subtle.timingSafeEqual(providedHash, expectedHash);
}

function isAllowedRequest(url: URL): boolean {
  const collectionMatch = COLLECTION_PATH.exec(url.pathname);
  if (collectionMatch) {
    if (!isSafeSegment(collectionMatch[1])) return false;
    if ([...url.searchParams.keys()].some((key) => !COLLECTION_QUERY_KEYS.has(key))) return false;
    if ([...COLLECTION_QUERY_KEYS].some((key) => url.searchParams.getAll(key).length > 1)) return false;
    if (url.searchParams.has("GamesType") && url.searchParams.get("GamesType") !== "SelfGame") return false;
    if (!isBoundedInteger(url.searchParams.get("Limit"), 1, 100)) return false;
    if (!isBoundedInteger(url.searchParams.get("Offset"), 0, 1_000_000)) return false;
    return true;
  }

  const gameMatch = GAME_PATH.exec(url.pathname);
  return gameMatch !== null && isSafeSegment(gameMatch[1]) && url.search === "";
}

function isSafeSegment(encodedSegment: string | undefined): boolean {
  if (!encodedSegment || encodedSegment.length > 300) return false;
  try {
    const value = decodeURIComponent(encodedSegment);
    return value.length > 0
      && value.length <= 100
      && value !== "."
      && value !== ".."
      && !/[\\/\u0000-\u001f\u007f]/.test(value);
  } catch {
    return false;
  }
}

function isBoundedInteger(value: string | null, min: number, max: number): boolean {
  if (value === null) return true;
  if (!/^\d+$/.test(value)) return false;
  const number = Number(value);
  return Number.isSafeInteger(number) && number >= min && number <= max;
}

function jsonError(message: string, status: number, headers?: HeadersInit): Response {
  return Response.json({ error: message }, { status, headers });
}

function log(
  severity: "log" | "warn",
  event: string,
  method: string,
  path: string,
  status?: number,
): void {
  console[severity](JSON.stringify({ event, method, path, ...(status ? { status } : {}) }));
}
