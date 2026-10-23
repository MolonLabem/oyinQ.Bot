import { useState } from "react";
import { api } from "../../api/client";
import { useAsync } from "../../hooks/useAsync";
import { Empty, ErrorState, Loading } from "../../components/Ui";

type History = { items: { gatheringId: string; communityKey: string; play: { publicId: string; gameName: string; endedAtUtc: string; community: string; timeZoneId: string; players: { name: string }[] } }[]; hasNext: boolean };
type Props = { communityKey?: string; open: (key: string, id: string) => void; compact?: boolean };

export function PlayedHistoryDisclosure(props: Props) {
  const [loaded, setLoaded] = useState(false);
  return <details className="profile-history" onToggle={event => { if (event.currentTarget.open) setLoaded(true); }}>
    <summary>Сыгранные партии</summary>{loaded && <PlayedHistory {...props} compact />}
  </details>;
}
export function PlayedHistory({ communityKey, open, compact = false }: Props) {
  const [page, setPage] = useState(1);
  const state = useAsync(() => api<History>(`/profile/plays?page=${page}`), [communityKey, page]);
  return <section>{!compact && <h2>Сыгранные партии</h2>}<p className={compact ? "muted" : undefined}>Партии с вашим участием, которые подтвердил организатор или администратор.</p>
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.items.length ? <Empty>Подтверждённых партий пока нет.</Empty> : <div className="played-history-list">{state.data.items.map(x => <button className="played-history-item" key={x.play.publicId} onClick={() => open(x.communityKey, x.gatheringId)}><span><strong>{x.play.gameName}</strong><small>{new Date(x.play.endedAtUtc).toLocaleDateString("ru-RU", { timeZone: x.play.timeZoneId })} · {x.play.players.length} игроков · {x.play.community}</small></span><span aria-hidden>›</span></button>)}</div>}
    {(page > 1 || state.data?.hasNext) && <nav className="history-pagination" aria-label="Страницы сыгранных партий"><button disabled={page <= 1 || state.loading} onClick={() => setPage(page - 1)}>Назад</button><span className="muted">{page}</span><button disabled={!state.data?.hasNext || state.loading || Boolean(state.error)} onClick={() => setPage(page + 1)}>Далее</button></nav>}
  </section>;
}
