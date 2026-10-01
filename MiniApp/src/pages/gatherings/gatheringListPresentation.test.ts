import { expect, it } from "vitest";
import type { GatheringListItem } from "../../api/types";
import { gatheringDays } from "./gatheringListPresentation";

const item = (id: string, startsAtUtc?: string): GatheringListItem => ({
  startsAtUtc, isOrganizer: false,
  card: { publicId: id, gameName: "Игра", organizerName: "Организатор", canTeachRules: true,
    rulesText: "Могу объяснить правила", localDateTime: "1 октября, 01:00", occupiedSeats: 2, maximumPlayers: 4, statusText: "Есть места" }
});

it("groups by community date when the device and UTC dates differ", () => {
  const days = gatheringDays([item("a", "2026-09-30T20:00:00Z"), item("b", "2026-10-01T19:00:00Z")],
    "Asia/Qyzylorda", new Date("2026-09-30T19:30:00Z"));
  expect(days.map(day => [day.date, day.label, day.items[0].time])).toEqual([
    ["2026-10-01", "Сегодня", "01:00"], ["2026-10-02", "Завтра", "00:00"]
  ]);
});

it("uses the next calendar date across a DST transition", () => {
  const days = gatheringDays([item("a", "2026-03-29T22:30:00Z")], "Europe/Berlin", new Date("2026-03-29T00:30:00Z"));
  expect(days[0].label).toBe("Завтра");
  expect(days[0].date).toBe("2026-03-30");
  expect(days[0].items[0].time).toBe("00:30");
});

it("preserves descending history order, page contents, and the year of older events", () => {
  const days = gatheringDays([item("new", "2026-09-20T09:00:00Z"), item("old", "2025-09-20T09:00:00Z")],
    "UTC", new Date("2026-09-30T09:00:00Z"));
  expect(days.flatMap(day => day.items.map(row => row.item.card.publicId))).toEqual(["new", "old"]);
  expect(days[0].label).not.toContain("2026");
  expect(days[1].label).toContain("2025");
});

it("keeps cards from older responses readable without guessing from localized date text", () => {
  const days = gatheringDays([item("legacy"), item("invalid", "bad-date"), item("current", "2026-09-30T09:00:00Z")],
    "UTC", new Date("2026-09-30T00:00:00Z"));
  expect(days[0].label).toBeUndefined();
  expect(days[0].items.map(row => row.item.card.publicId)).toEqual(["legacy", "invalid"]);
  expect(days[0].items[0].time).toBeUndefined();
  expect(days[1].label).toBe("Сегодня");
});
