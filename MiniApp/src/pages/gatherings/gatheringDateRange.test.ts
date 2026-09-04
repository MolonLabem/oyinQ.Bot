import { describe, expect, it } from "vitest";
import type { Community } from "../../api/types";
import {
  gatheringDateTimeBounds,
  isWithinCampDateRange,
  revalidateGatheringStart,
} from "./gatheringDateRange";

const camp: Community = {
  key: "camp",
  name: "Кэмп",
  mode: "Camp",
  timeZoneId: "Asia/Qyzylorda",
  startsAtUtc: "2026-09-09T19:00:00Z",
  endsAtUtc: "2026-09-13T19:00:00Z",
  startDate: "2026-09-10",
  endDate: "2026-09-13",
};

describe("camp gathering date range", () => {
  it("сохраняет последнее утро и исключает точный момент закрытия", () => {
    const timed = { ...camp, startsAtUtc: "2026-09-10T04:00:00Z", endsAtUtc: "2026-09-13T04:00:00Z" };
    expect(gatheringDateTimeBounds(timed, "2026-09-03T12:00")).toEqual({ min: "2026-09-10T09:00", max: "2026-09-13T08:59" });
    expect(isWithinCampDateRange("2026-09-10T08:59", timed)).toBe(false);
    expect(isWithinCampDateRange("2026-09-13T08:59", timed)).toBe(true);
    expect(isWithinCampDateRange("2026-09-13T09:00", timed)).toBe(false);
  });
  it("uses inclusive datetime-local bounds for a multi-day camp", () => {
    expect(gatheringDateTimeBounds(camp, "2026-09-03T12:00")).toEqual({
      min: "2026-09-10T00:00",
      max: "2026-09-13T23:59",
    });
    expect(isWithinCampDateRange("2026-09-09T23:59", camp)).toBe(false);
    expect(isWithinCampDateRange("2026-09-10T00:00", camp)).toBe(true);
    expect(isWithinCampDateRange("2026-09-13T23:59", camp)).toBe(true);
    expect(isWithinCampDateRange("2026-09-14T00:00", camp)).toBe(false);
  });

  it("allows only the configured date for a single-day camp", () => {
    const singleDay = { ...camp, startsAtUtc: "2026-09-19T19:00:00Z", endsAtUtc: "2026-09-20T19:00:00Z" };
    expect(gatheringDateTimeBounds(singleDay, "2026-09-03T12:00")).toEqual({
      min: "2026-09-20T00:00",
      max: "2026-09-20T23:59",
    });
    expect(isWithinCampDateRange("2026-09-20T18:00", singleDay)).toBe(true);
    expect(isWithinCampDateRange("2026-09-21T00:00", singleDay)).toBe(false);
  });

  it("replaces an invalid restored value when the selected camp changes", () => {
    const bounds = gatheringDateTimeBounds(camp, "2026-09-03T12:00");
    expect(revalidateGatheringStart("2026-10-01T18:00", camp, bounds))
      .toBe("2026-09-10T00:00");
    expect(revalidateGatheringStart("2026-09-12T18:00", camp, bounds))
      .toBe("2026-09-12T18:00");
  });

  it("preserves normal club gathering behavior", () => {
    const club: Community = {
      key: "club",
      name: "Клуб",
      mode: "Club",
      timeZoneId: "Asia/Qyzylorda",
    };
    const bounds = gatheringDateTimeBounds(club, "2026-09-03T12:00");
    expect(bounds).toEqual({ min: "2026-09-03T12:00" });
    expect(isWithinCampDateRange("2030-01-01T18:00", club)).toBe(true);
    expect(revalidateGatheringStart("2030-01-01T18:00", club, bounds))
      .toBe("2030-01-01T18:00");
  });
});
