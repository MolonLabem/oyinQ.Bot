import { afterEach, describe, expect, it, vi } from "vitest";
import { currentLocalMinute, formatCalendarDate, formatDate, formatInstant, formatLocalDateTime, formatLocalTime, formatShortDate, formatTime, formatUpdatedAt } from "./format";

afterEach(() => vi.unstubAllGlobals());

describe("day-first date and time presentation", () => {
  it.each(["en-US", "en-GB", "ru-RU", "th-TH", "ar-SA", "en-US-u-hc-h12", "bad_locale", undefined])(
    "keeps day-first dates and 24-hour times with %s device preferences", language => {
      vi.stubGlobal("navigator", language ? { language, languages: [language] } : undefined);
      expect(formatDate("2026-11-06")).toBe("06.11.2026");
      expect(formatShortDate("2026-11-06")).toBe("06.11.2026");
      expect(formatLocalDateTime("2026-11-06T13:05")).toBe("06.11.2026, 13:05");
      expect(formatLocalTime("00:00")).toBe("00:00");
      expect(formatLocalTime("23:45")).toBe("23:45");
      expect(formatCalendarDate("2026-11-06", { day: "numeric", month: "long" })).toBe("6 ноября");
      expect(formatTime("2026-11-06T13:05:00Z")).toMatch(/^\d{2}:\d{2}$/);
      expect(formatUpdatedAt("2026-11-06T13:05:00Z")).toMatch(/^06\.11\.2026, \d{2}:\d{2}$/);
    });

  it("keeps community wall dates and times without a device-timezone conversion", () => {
    expect(formatLocalDateTime("2026-10-10T00:15")).toBe("10.10.2026, 00:15");
    expect(formatLocalDateTime("2026-10-12T23:45")).toBe("12.10.2026, 23:45");
    expect(currentLocalMinute("Asia/Qyzylorda", new Date("2026-11-05T19:00:00Z"))).toBe("2026-11-06T00:00");
  });

  it("formats instants in the requested community timezone, including DST", () => {
    vi.stubGlobal("navigator", { language: "en-US", languages: ["en-US"] });
    const instant = "2026-11-01T04:30:00Z";
    expect(formatInstant(instant, "America/New_York")).toMatch(/^вс, 1 ноября 2026 г\.(?: в|,) 00:30$/);
    expect(formatInstant(instant, "Pacific/Honolulu")).toMatch(/^сб, 31 октября 2026 г\.(?: в|,) 18:30$/);
    expect(formatInstant("2026-11-01T06:30:00Z", "America/New_York")).toMatch(/^вс, 1 ноября 2026 г\.(?: в|,) 01:30$/);
  });

  it("allows an empty review while dates are being entered", () => {
    expect(formatLocalDateTime("")).toBe("Дата не указана");
    expect(formatLocalDateTime()).toBe("Дата не указана");
    expect(formatDate()).toBe("Дата не указана");
  });
});
