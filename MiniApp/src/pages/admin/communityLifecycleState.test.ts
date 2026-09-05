import { describe, expect, it } from "vitest";
import { campDateValidation, cancellationConfirmation, canCancelCamp, canDeleteCommunity, deletionConfirmation } from "./communityLifecycleState";

describe("community lifecycle admin controls", () => {
  it("keeps cancellation only for a draft or active camp", () => {
    expect(canCancelCamp("Draft")).toBe(true);
    expect(canCancelCamp("Active")).toBe(true);
    expect(canCancelCamp("Closed")).toBe(false);
    expect(canCancelCamp("Cancelled")).toBe(false);
  });

  it("shows destructive deletion only to a Super Admin", () => {
    expect(canDeleteCommunity(true)).toBe(true);
    expect(canDeleteCommunity(false)).toBe(false);
  });

  it("names the target and irreversible consequence in confirmation", () => {
    expect(deletionConfirmation("clubs", "RollMove")).toContain("Удалить клуб «RollMove» из OyinQ?");
    expect(deletionConfirmation("camps", "Лето")).toContain("Отменить это действие нельзя");
    expect(cancellationConfirmation("Лето")).toContain("Возобновить кэмп после отмены нельзя");
  });

  it("points invalid camp dates to the relevant field", () => {
    expect(campDateValidation("", "")).toEqual({ start: "Укажите дату и время начала.", end: "Укажите дату и время окончания." });
    expect(campDateValidation("2026-09-10T12:00", "2026-09-10T11:00").end).toContain("позже начала");
    expect(campDateValidation("2026-09-10T12:00", "2026-09-10T13:00")).toEqual({ start: undefined, end: undefined });
  });
});
