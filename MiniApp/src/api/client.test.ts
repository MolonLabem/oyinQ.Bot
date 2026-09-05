import { afterEach, describe, expect, it, vi } from "vitest";
import { api, fallbackApiError } from "./client";

afterEach(() => vi.unstubAllGlobals());

describe("api errors", () => {
  it("keeps a specific server message", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ message: "Дата уже занята." }), { status: 409 })));
    await expect(api("/test")).rejects.toMatchObject({ message: "Дата уже занята.", status: 409 });
  });

  it("uses Russian messages for transport and empty server failures", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
    await expect(api("/test")).rejects.toMatchObject({ message: expect.stringContaining("Нет соединения"), status: 0 });
    expect(fallbackApiError(500)).toContain("временно недоступен");
  });
});
