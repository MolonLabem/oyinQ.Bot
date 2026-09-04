import { useState } from "react";
import { api } from "../../api/client";
import { useAsync } from "../../hooks/useAsync";
import { Card, Empty, ErrorState, Loading } from "../../components/Ui";

type History = { items: { gatheringId: string; communityKey: string; play: { publicId: string; gameName: string; endedAtUtc: string; community: string; timeZoneId: string; players: { name: string }[] } }[]; hasNext: boolean };
export function PlayedHistory({ communityKey, open }: { communityKey?: string; open: (key: string, id: string) => void }) {
  const [page, setPage] = useState(1);
  const state = useAsync(() => api<History>(`/profile/plays?page=${page}`), [communityKey, page]);
  return <section><h2>История игр</h2><p>Только явно подтверждённые партии с вашим участием.</p>
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.items.length ? <Empty>Подтверждённых партий пока нет.</Empty> : state.data.items.map(x => <Card key={x.play.publicId}><button className="ghost" onClick={() => open(x.communityKey, x.gatheringId)}><strong>{x.play.gameName}</strong><br />{new Date(x.play.endedAtUtc).toLocaleDateString("ru-RU", { timeZone: x.play.timeZoneId })} · {x.play.players.length} игроков · {x.play.community}</button></Card>)}
    <div className="choice-row">{page > 1 && <button onClick={() => setPage(page - 1)}>Назад</button>}{state.data?.hasNext && <button onClick={() => setPage(page + 1)}>Далее</button>}</div>
  </section>;
}
