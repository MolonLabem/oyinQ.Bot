import { api } from "../../api/client";
import type { ClubGame } from "../../api/types";
import { useAsync } from "../../hooks/useAsync";
import { Card, Empty, ErrorState, Loading } from "../../components/Ui";
import { ComplexityBadge } from "../../components/ComplexityBadge";

export function CommunityDemand({ communityKey, create }: { communityKey: string; create?: (id: number) => void }) {
  const state = useAsync(() => api<{ game: ClubGame; interestedParticipants: number; scheduledGatherings: number; availabilitySummary: string }[]>(`/catalog/demand?community=${encodeURIComponent(communityKey)}`), [communityKey]);
  return <section className="content-section"><h2>Во что хотят сыграть в сообществе</h2>
    <p className="muted">Сначала игры, по которым ещё нет сборов. Интерес не означает запись: участники присоединятся сами.</p>
    <button className="wish-refresh" disabled={state.loading} onClick={state.reload}>↻ {state.loading ? "Обновляем…" : "Обновить список"}</button>
    {state.error && <ErrorState message={state.error} retry={state.reload} />}
    {state.loading && !state.data ? <Loading /> : !state.data?.length ? <Empty>Пока никто не отметил игры.</Empty> : <div className="stack">{state.data.map(item => <Card key={item.game.bggId}>
      <strong>{item.game.name}</strong> <ComplexityBadge info={item.game.complexityInfo} />
      <p>Хотят сыграть: {item.interestedParticipants} · Сборов: {item.scheduledGatherings}</p><p>{item.availabilitySummary}</p>
      {create && <button onClick={() => create(item.game.bggId)}>Организовать сбор</button>}
    </Card>)}</div>}
  </section>;
}
