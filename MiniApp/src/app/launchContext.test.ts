import { describe, expect, it } from "vitest";
import { miniAppLaunchContext } from "./launchContext";

describe("Mini App launch context", () => {
  it("opens a gathering from the direct Main Mini App start parameter", () => {
    expect(miniAppLaunchContext("", "g-Hb5nG7rK0EGFXQS8Kb0ELQ-club-main")).toEqual({
      communityKey: "club-main",
      gatheringId: "1b67be1d-caba-41d0-855d-04bc29bd042d"
    });
  });

  it("continues to accept community-only start parameters", () => {
    expect(miniAppLaunchContext("", "community-club-main")).toEqual({
      communityKey: "club-main",
      gatheringId: undefined
    });
  });

  it("prefers explicit Mini App URL query parameters", () => {
    expect(miniAppLaunchContext("?community=camp&gathering=query-id", "g-Hb5nG7rK0EGFXQS8Kb0ELQ-club-main"))
      .toEqual({ communityKey: "camp", gatheringId: "query-id" });
  });

  it("ignores malformed gathering start parameters", () => {
    expect(miniAppLaunchContext("", "g-invalid-club")).toEqual({
      communityKey: undefined,
      gatheringId: undefined
    });
  });
});
