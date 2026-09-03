import { describe, expect, it } from "vitest";
import { profileGatheringVisit } from "./profileNavigation";

describe("profile schedule navigation", () => {
  it("opens the exact gathering and retains the profile community for Back", () => {
    expect(profileGatheringVisit("rollmove", "camp-2026", "gathering-42")).toEqual({
      returnCommunityKey: "rollmove",
      targetCommunityKey: "camp-2026",
      gatheringId: "gathering-42"
    });
  });
});
