import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { dateTimeLocale } from "./dateTimeLocale";
import { currentLocalMinute, formatDate, formatInstant, formatLocalDateTime, formatLocalTime, formatShortDate, formatTime, formatUpdatedAt } from "./format";

function locale(language: string, languages = [language]) { vi.stubGlobal("navigator", { language, languages }); }
beforeEach(() => locale("ru-RU"));
afterEach(() => vi.unstubAllGlobals());

describe("regional date and time presentation", () => {
  it("keeps community wall dates and times without a device-timezone conversion", () => {
    expect(formatLocalDateTime("2026-10-10T00:15")).toBe("10.10.2026, 00:15");
    expect(formatLocalDateTime("2026-10-12T23:45")).toBe("12.10.2026, 23:45");
    expect(formatDate("2026-11-06")).toBe("06.11.2026");
  });

  it("follows British day-first dates and the 24-hour clock", () => {
    locale("en-GB");
    expect(formatDate("2026-11-06")).toBe("06/11/2026");
    expect(formatShortDate("2026-11-06")).toBe("06/11/2026");
    expect(formatLocalDateTime("2026-11-06T13:05")).toBe("06/11/2026, 13:05");
    expect(formatLocalTime("00:00")).toBe("00:00");
  });

  it("follows American month-first dates and the 12-hour clock when requested by the device", () => {
    locale("en-US");
    expect(formatLocalDateTime("2026-11-06T13:05")).toBe("11/06/2026, 01:05 PM");
    expect(formatLocalTime("00:00")).toBe("12:00 AM");
    expect(formatTime("2026-11-06T13:05:00Z")).toMatch(/AM|PM/);
    expect(formatUpdatedAt("2026-11-06T13:05:00Z")).toContain("2026");
  });

  it("honors a browser hour-cycle preference without changing canonical API values", () => {
    locale("en-US-u-hc-h23");
    expect(formatLocalTime("00:00")).toBe("00:00");
    expect(currentLocalMinute("Asia/Qyzylorda", new Date("2026-11-05T19:00:00Z"))).toBe("2026-11-06T00:00");
  });

  it("keeps the saved Gregorian calendar year when regional settings use a different calendar", () => {
    locale("th-TH");
    expect(formatDate("2026-11-06")).toContain("2026");
  });

  it("formats instants in the requested community timezone, including DST", () => {
    locale("en-US");
    const instant = "2026-11-01T04:30:00Z";
    expect(formatInstant(instant, "America/New_York")).toContain("12:30 AM");
    expect(formatInstant(instant, "Pacific/Honolulu")).toContain("October 31");
    expect(formatInstant("2026-11-01T06:30:00Z", "America/New_York")).toContain("01:30 AM");
  });

  it("tries the preferred supported locale and skips invalid or unsupported entries", () => {
    locale("en-US", ["bad_locale", "zz-ZZ", "en-GB"]);
    expect(dateTimeLocale()).toBe("en-GB");
    expect(formatDate("2026-11-06")).toBe("06/11/2026");
    locale("en-GB", []); expect(dateTimeLocale()).toBe("en-GB");
  });

  it("falls back to DD.MM.YYYY and 24-hour time without browser regional information", () => {
    vi.stubGlobal("navigator", undefined);
    expect(dateTimeLocale()).toBe("ru-RU");
    expect(formatLocalDateTime("2026-11-06T13:05")).toBe("06.11.2026, 13:05");
    locale("bad_locale", ["zz-ZZ"]);
    expect(formatDate("2026-11-06")).toBe("06.11.2026");
  });

  it("allows an empty review while dates are being entered", () => {
    expect(formatLocalDateTime("")).toBe("Дата не указана");
    expect(formatLocalDateTime()).toBe("Дата не указана");
    expect(formatDate()).toBe("Дата не указана");
  });
});
