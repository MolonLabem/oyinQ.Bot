export type GatheringListView = "upcoming" | "history";
export type GatheringHistoryFilter = "all" | "completed" | "cancelled";

export type GatheringListState = {
  view: GatheringListView;
  historyFilter: GatheringHistoryFilter;
  page: number;
};

export const initialGatheringListState: GatheringListState = {
  view: "upcoming",
  historyFilter: "all",
  page: 1
};

export function changeGatheringView(
  state: GatheringListState,
  view: GatheringListView
): GatheringListState {
  if (state.view === view) return state;
  return { view, historyFilter: "all", page: 1 };
}

export function changeGatheringHistoryFilter(
  state: GatheringListState,
  historyFilter: GatheringHistoryFilter
): GatheringListState {
  if (state.view === "history" && state.historyFilter === historyFilter) return state;
  return { view: "history", historyFilter, page: 1 };
}

export function buildGatheringListQuery(communityKey: string, state: GatheringListState): string {
  const query = new URLSearchParams({
    community: communityKey,
    view: state.view,
    page: String(state.page)
  });
  if (state.view === "history" && state.historyFilter !== "all") {
    query.set("status", state.historyFilter);
  }
  return query.toString();
}
