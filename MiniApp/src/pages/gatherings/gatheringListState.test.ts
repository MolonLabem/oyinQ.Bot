import { describe, expect, it } from "vitest";
import { buildGatheringListQuery, changeGatheringHistoryFilter, changeGatheringView, gatheringHistoryFilter, gatheringListView, type GatheringListState } from "./gatheringListState";

describe("gathering list state", () => {
  it("keeps the selected history filter in paged requests", () => {
    const query = new URLSearchParams(buildGatheringListQuery("camp-a", {
      scope: "cancelled", page: 3
    }));

    expect(Object.fromEntries(query)).toEqual({
      community: "camp-a", scope: "cancelled", view: "history", page: "3", status: "cancelled"
    });
  });

  it("resets page and filter when switching views", () => {
    const state: GatheringListState = { scope: "cancelled", page: 4 };

    expect(changeGatheringView(state, "upcoming")).toEqual({
      scope: "upcoming", page: 1
    });
  });

  it("resets page when switching history filters", () => {
    const state: GatheringListState = { scope: "completed", page: 2 };

    expect(changeGatheringHistoryFilter(state, "cancelled")).toEqual({
      scope: "cancelled", page: 1
    });
  });

  it("derives the visible controls from one canonical scope", () => {
    expect(gatheringListView("upcoming")).toBe("upcoming");
    expect(gatheringListView("completed")).toBe("history");
    expect(gatheringHistoryFilter("history")).toBe("all");
    expect(gatheringHistoryFilter("cancelled")).toBe("cancelled");
  });

});
