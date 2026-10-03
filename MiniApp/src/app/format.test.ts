import { describe, expect, it } from "vitest";
import { formatDate, formatLocalDateTime } from "./format";

describe("local camp date presentation", () => {
  it("keeps the supplied calendar day and time without a timezone conversion", () => {
    expect(formatLocalDateTime("2026-10-10T00:15")).toBe("10 октября 2026 г., 00:15");
    expect(formatLocalDateTime("2026-10-12T23:45")).toBe("12 октября 2026 г., 23:45");
  });

  it("allows an empty review while dates are being entered", () => {
    expect(formatLocalDateTime("")).toBe("Дата не указана");
    expect(formatLocalDateTime()).toBe("Дата не указана");
    expect(formatDate("2026-10-10")).toBe("10 октября 2026 г.");
  });
});
