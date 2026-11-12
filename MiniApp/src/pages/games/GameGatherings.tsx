import { GatheringCard } from "../../components/GatheringCard";
import { useState } from "react";
import { api } from "../../api/client";
import type { GatheringListPage } from "../../api/types";
import { useAsync } from "../../hooks/useAsync";
import { Pagination, Empty, ErrorState, Loading, Tabs } from "../../components/Ui";

export function GameGatherings({ communityKey, bggId, open, preserveScroll = false }: { communityKey: string; bggId: number; open: (id: string) => void; preserveScroll?: boolean }) {
  const [selection, setSelection] = useState({ scope: "upcoming", page: 1 });
  const state = useAsync(() => api<GatheringListPage>(`/gatherings?community=${encodeURIComponent(communityKey)}&bggId=${bggId}&scope=${selection.scope}&page=${selection.page}`), [communityKey, bggId, selection]);
  return <section className="content-section"><h2>Сборы по этой игре</h2>
    <Tabs scrollActiveIntoView={!preserveScroll} label="Сборы по игре" active={selection.scope} onChange={scope => setSelection({ scope, page: 1 })} items={[{ id: "upcoming", label: "Предстоящие" }, { id: "history", label: "История" }]} />
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : <>
      {!state.data?.items.length && <Empty>Сборов пока нет.</Empty>}
      <div className="stack">{state.data?.items.map(item => <GatheringCard key={item.card.publicId} item={item} open={open} />)}</div>
      <Pagination page={selection.page} hasPrevious={Boolean(state.data?.hasPrevious)} hasNext={Boolean(state.data?.hasNext)} onChange={page => setSelection({ ...selection, page })} label="Страницы сборов по игре" />
    </>}
  </section>;
}
