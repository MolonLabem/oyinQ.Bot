import { useState } from "react";
import { api } from "../../api/client";
import type { GatheringListPage } from "../../api/types";
import { useAsync } from "../../hooks/useAsync";
import { Empty, ErrorState, Loading, Tabs } from "../../components/Ui";

export function GameGatherings({ communityKey, bggId, open }: { communityKey: string; bggId: number; open: (id: string) => void }) {
  const [selection, setSelection] = useState({ scope: "upcoming", page: 1 });
  const state = useAsync(() => api<GatheringListPage>(`/gatherings?community=${encodeURIComponent(communityKey)}&bggId=${bggId}&scope=${selection.scope}&page=${selection.page}`), [communityKey, bggId, selection]);
  return <section className="content-section"><h2>Сборы по этой игре</h2>
    <Tabs label="Сборы по игре" active={selection.scope} onChange={scope => setSelection({ scope, page: 1 })} items={[{ id: "upcoming", label: "Предстоящие" }, { id: "history", label: "История" }]} />
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : <>
      {!state.data?.items.length && <Empty>В этом разделе сборов по игре пока нет.</Empty>}
      <div className="stack">{state.data?.items.map(item => <button className="card" key={item.card.publicId} onClick={() => open(item.card.publicId)}><strong>{item.card.localDateTime}</strong><p>{item.card.statusText} · {item.card.occupiedSeats}/{item.card.maximumPlayers}</p>{item.card.recruitment?.text && <p>{item.card.recruitment.text}</p>}</button>)}</div>
      {(state.data?.hasPrevious || state.data?.hasNext) && <div className="row"><button disabled={!state.data?.hasPrevious} onClick={() => setSelection({ ...selection, page: selection.page - 1 })}>Назад</button><span>{selection.page}</span><button disabled={!state.data?.hasNext} onClick={() => setSelection({ ...selection, page: selection.page + 1 })}>Дальше</button></div>}
    </>}
  </section>;
}
