import { env } from "cloudflare:workers";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import worker from "../src/index";
import { network } from "./network";

async function invoke(path: string, init?: RequestInit): Promise<Response> {
  const request = new Request(`https://proxy.example${path}`, init);
  return worker.fetch(request, env);
}

const authorized = { Authorization: "Bearer test-secret" };

describe("Tesera proxy", () => {
  it("fails closed when the secret binding is empty", async () => {
    const request = new Request("https://proxy.example/games/carcassonne", { headers: authorized });
    const response = await worker.fetch(request, { TESERA_PROXY_SECRET: "" });

    expect(response.status).toBe(401);
  });

  it("rejects missing and bad authentication", async () => {
    expect((await invoke("/games/carcassonne")).status).toBe(401);
    expect((await invoke("/games/carcassonne", { headers: { Authorization: "Bearer bad" } })).status).toBe(401);
  });

  it("rejects unsupported methods and paths", async () => {
    expect((await invoke("/games/carcassonne", { method: "POST", headers: authorized })).status).toBe(405);
    expect((await invoke("/proxy?url=https://example.com", { headers: authorized })).status).toBe(404);
    expect((await invoke("/games/a/b", { headers: authorized })).status).toBe(404);
    expect((await invoke("/collections/own/user?Limit=101", { headers: authorized })).status).toBe(404);
    expect((await invoke("/collections/own/user?Limit=100&Limit=1", { headers: authorized })).status).toBe(404);
  });

  it("forwards allowed collection query strings without secret headers", async () => {
    network.use(http.get("https://api.tesera.ru/collections/base/own/test-user", ({ request }) => {
      const url = new URL(request.url);
      expect(url.searchParams.get("GamesType")).toBe("SelfGame");
      expect(url.searchParams.get("Limit")).toBe("100");
      expect(url.searchParams.get("Offset")).toBe("20");
      expect(request.headers.get("Authorization")).toBeNull();
      expect(request.headers.get("Cookie")).toBeNull();
      return HttpResponse.json({ games: [] });
    }));

    const response = await invoke(
      "/collections/base/own/test-user?GamesType=SelfGame&Limit=100&Offset=20",
      { headers: { ...authorized, Cookie: "private=true", "X-Forwarded-For": "127.0.0.1" } },
    );

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ games: [] });
  });

  it("forwards game responses and upstream failures transparently", async () => {
    network.use(
      http.get("https://api.tesera.ru/games/carcassonne", () => HttpResponse.json({ alias: "carcassonne" }, { status: 200 })),
      http.get("https://api.tesera.ru/games/missing", () => HttpResponse.json({ error: "missing" }, { status: 404 })),
    );

    const success = await invoke("/games/carcassonne", { headers: authorized });
    expect(success.status).toBe(200);
    expect(await success.json()).toEqual({ alias: "carcassonne" });

    const failure = await invoke("/games/missing", { headers: authorized });
    expect(failure.status).toBe(404);
    expect(await failure.json()).toEqual({ error: "missing" });
  });
});
