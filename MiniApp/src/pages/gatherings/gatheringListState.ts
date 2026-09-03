export type GatheringListView = "upcoming" | "history";
export type GatheringHistoryFilter = "all" | "completed" | "cancelled";
export type GatheringListScope = "upcoming" | "history" | "completed" | "cancelled";

export type GatheringListState = {
  scope: GatheringListScope;
  page: number;
};

export const initialGatheringListState: GatheringListState = {
  scope: "upcoming",
  page: 1
};

export function changeGatheringView(
  state: GatheringListState,
  view: GatheringListView
): GatheringListState {
  const scope: GatheringListScope = view === "upcoming" ? "upcoming" : "history";
  if (state.scope === scope) return state;
  return { scope, page: 1 };
}

export function changeGatheringHistoryFilter(
  state: GatheringListState,
  historyFilter: GatheringHistoryFilter
): GatheringListState {
  const scope: GatheringListScope = historyFilter === "all" ? "history" : historyFilter;
  if (state.scope === scope) return state;
  return { scope, page: 1 };
}

export function gatheringListView(scope: GatheringListScope): GatheringListView {
  return scope === "upcoming" ? "upcoming" : "history";
}

export function gatheringHistoryFilter(scope: GatheringListScope): GatheringHistoryFilter {
  return scope === "completed" || scope === "cancelled" ? scope : "all";
}

export function buildGatheringListQuery(communityKey: string, state: GatheringListState): string {
  const view = gatheringListView(state.scope);
  const historyFilter = gatheringHistoryFilter(state.scope);
  const query = new URLSearchParams({
    community: communityKey,
    scope: state.scope,
    view,
    page: String(state.page)
  });
  // Keep equivalent legacy parameters while cached Mini Apps and rolling backend instances coexist.
  if (view === "history" && historyFilter !== "all") query.set("status", historyFilter);
  return query.toString();
}
