import { describe, expect, it } from "vitest";
import { buildGatheringListQuery, changeGatheringHistoryFilter, changeGatheringView, type GatheringListState } from "./gatheringListState";

describe("gathering list state", () => {
  it("keeps the selected history filter in paged requests", () => {
    const query = new URLSearchParams(buildGatheringListQuery("camp-a", {
      view: "history", historyFilter: "cancelled", page: 3
    }));

    expect(Object.fromEntries(query)).toEqual({
      community: "camp-a", view: "history", page: "3", status: "cancelled"
    });
  });

  it("resets page and stale history filter when switching views", () => {
    const state: GatheringListState = { view: "history", historyFilter: "cancelled", page: 4 };

    expect(changeGatheringView(state, "upcoming")).toEqual({
      view: "upcoming", historyFilter: "all", page: 1
    });
  });

  it("resets page when switching history filters", () => {
    const state: GatheringListState = { view: "history", historyFilter: "completed", page: 2 };

    expect(changeGatheringHistoryFilter(state, "cancelled")).toEqual({
      view: "history", historyFilter: "cancelled", page: 1
    });
  });
});
