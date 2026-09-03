import { describe, expect, it } from "vitest";
import { collectionMissingMessage, collectionVisitFromGathering, positiveGameId } from "./collectionNavigation";

describe("collection navigation", () => {
  it("keeps the canonical BGG identity and gathering return target", () => {
    expect(collectionVisitFromGathering(167791, "gathering-42")).toEqual({
      bggId: 167791,
      returnGatheringId: "gathering-42"
    });
  });

  it("accepts only positive safe game IDs from direct links", () => {
    expect(positiveGameId("167791")).toBe(167791);
    expect(positiveGameId("0")).toBeUndefined();
    expect(positiveGameId("Terraforming Mars")).toBeUndefined();
    expect(positiveGameId("9007199254740992")).toBeUndefined();
  });

  it("uses a collection absence notice rather than an application error", () => {
    expect(collectionMissingMessage({ key: "rollmove", name: "RollMove", mode: "Club", timeZoneId: "UTC" }))
      .toBe("Этой игры нет в коллекции «RollMove».");
    expect(collectionMissingMessage({ key: "camp", name: "Camp", mode: "Camp", timeZoneId: "UTC" }))
      .toBe("Этой игры нет в коллекции этого кэмпа.");
  });
});
