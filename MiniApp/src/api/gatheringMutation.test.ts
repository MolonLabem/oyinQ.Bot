import { afterEach, describe, expect, it, vi } from "vitest";
vi.mock("../telegram/webApp", () => ({ telegram: { initData: "signed", confirm: vi.fn() } }));
import { telegram } from "../telegram/webApp";
import { gatheringMutation, json } from "./client";

afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks(); });
describe("подтверждение возможного пересечения", () => {
  it("повторяет только подтверждённое предупреждение, сохраняя параметры", async () => {
    const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ code: "gathering_schedule_conflict", message: "Возможное пересечение", conflicts: [{ publicId: "g", gameName: "Игра", startsAtUtc: "2026-09-04T12:00:00Z", community: "Клуб", communityKey: "c", timeZoneId: "Asia/Almaty" }] }), { status: 409 })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetch); vi.mocked(telegram.confirm).mockResolvedValue(true);
    await gatheringMutation("/gatherings/g/join", json("POST", { communityKey: "c" }));
    expect(telegram.confirm).toHaveBeenCalledWith(expect.stringContaining("Возможное пересечение"));
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ communityKey: "c", confirmScheduleConflict: true });
    expect(fetch).toHaveBeenCalledTimes(2);
  });
  it("не превращает отказ в доступе в подтверждаемое предупреждение", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: "forbidden", message: "Нужна регистрация" }), { status: 403 }));
    vi.stubGlobal("fetch", fetch);
    await expect(gatheringMutation("/gatherings/g/join", json("POST", { communityKey: "c" }))).rejects.toThrow("Нужна регистрация");
    expect(telegram.confirm).not.toHaveBeenCalled(); expect(fetch).toHaveBeenCalledTimes(1);
  });
});
