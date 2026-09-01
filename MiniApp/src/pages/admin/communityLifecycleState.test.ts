import { describe, expect, it } from "vitest";
import { canCancelCamp, canDeleteCommunity, deletionConfirmation } from "./communityLifecycleState";

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
    expect(deletionConfirmation("clubs", "RollMove")).toContain("Удалить клуб «RollMove»?");
    expect(deletionConfirmation("camps", "Лето")).toContain("Действие нельзя отменить");
  });
});
