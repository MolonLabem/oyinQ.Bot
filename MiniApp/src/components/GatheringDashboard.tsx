import { useState } from "react";
import { api, json } from "../api/client";
import { useAsync } from "../hooks/useAsync";
import { Card, Empty, ErrorState, Loading, Notice } from "./Ui";
import type { GameProvider, RecruitmentState } from "../api/types";

type DashboardItem = { recruitment?: RecruitmentState; publicId: string; communityKey: string; community: string; gameName: string; localDateTime: string; isOrganizer: boolean; isToday: boolean; waitlistPosition?: number; belowMinimum: boolean; fullWithWaitlist: boolean; startingSoon: boolean; recentlyCancelled: boolean; publicationFailed: boolean; deliveryProblems: number; notificationUnavailableParticipants: number; needsPlayConfirmation?: boolean; provider: GameProvider };
type Dashboard = { items: DashboardItem[]; hasMore: boolean; camp?: { registeredToday: number; gatheringsToday: number; bringingGames: number; availableGames: number } };

export function GatheringDashboard({ communityKey, open, collapsible = false }: { communityKey?: string; open: (key: string, id: string) => void; collapsible?: boolean }) {
  const personal = communityKey === undefined;
  const state = useAsync(() => api<Dashboard>(personal ? "/profile/dashboard" : `/gatherings/dashboard?community=${encodeURIComponent(communityKey!)}`), [communityKey]);
  const [busyId, setBusyId] = useState<string>(); const [error, setError] = useState<string>();
  async function bring(item: DashboardItem) { if (busyId) return; setBusyId(item.publicId); setError(undefined); try { await api(`/gatherings/${item.publicId}/bring`, json("POST", { communityKey: item.communityKey })); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusyId(undefined); } }
  if (state.loading) return <Loading />;
  if (state.error) return <ErrorState message={state.error} retry={state.reload} />;
  const rows = (state.data?.items ?? []).filter(item => personal
    ? item.isToday || Boolean(item.waitlistPosition) || item.provider.canBring && !item.provider.isConfirmed
      || item.isOrganizer && (item.recruitment?.belowDesired || item.publicationFailed || item.needsPlayConfirmation)
    : item.recentlyCancelled || item.recruitment?.belowDesired
    || item.fullWithWaitlist || !item.provider.isConfirmed || item.startingSoon || item.publicationFailed
    || item.deliveryProblems > 0 || item.notificationUnavailableParticipants > 0 || item.needsPlayConfirmation);
  if (collapsible && !rows.length && !state.data?.hasMore) return null;
  const content = <>
    {state.data?.camp && <Notice>Сегодня зарегистрированы: {state.data.camp.registeredToday}. Игр точно привезут: {state.data.camp.bringingGames}, могут привезти: {state.data.camp.availableGames}.</Notice>}
    {error && <Notice kind="danger">{error}</Notice>}
    {!rows.length && <Empty>{personal ? "Здесь появятся ваши сборы, ожидание места и игры, для которых вы можете привезти коробку." : "Сборов, требующих внимания, нет."}</Empty>}
    {rows.map(item => <Card key={item.publicId}>
      <button className="dashboard-gathering-link" onClick={() => open(item.communityKey, item.publicId)}><strong>{item.gameName}</strong><span>{item.localDateTime}{personal && ` · ${item.community}`}</span></button>
      {(item.isToday || item.isOrganizer || item.waitlistPosition || item.startingSoon) && <p>{[item.isToday && "Сегодня", item.isOrganizer ? "Вы организатор" : item.waitlistPosition ? `Лист ожидания: ${item.waitlistPosition}` : undefined, item.startingSoon && "В ближайшие два часа"].filter(Boolean).join(" · ")}</p>}
      <ul className="dashboard-statuses">
        {item.needsPlayConfirmation && <li className="attention">Подтвердите, состоялась ли игра, и укажите фактический состав.</li>}
        {item.recentlyCancelled ? <li className="danger">Недавно отменён</li> : !item.needsPlayConfirmation && <>
          {item.recruitment && <li className={item.belowMinimum ? "attention" : undefined}>{item.recruitment.text}</li>}
          {item.fullWithWaitlist && <li className="attention">Места заняты, есть лист ожидания</li>}
          {!item.provider.isConfirmed && <li className="attention">{item.provider.summary}</li>}
        </>}
        {item.publicationFailed && <li className="danger">Проверьте публикацию объявления в сборе.</li>}
        {!item.recentlyCancelled && item.notificationUnavailableParticipants > 0 && <li className="attention">Участников без доступных уведомлений: {item.notificationUnavailableParticipants}. Попросите их запустить бота.</li>}
        {item.deliveryProblems > 0 && <li className="attention">Не подтверждена доставка важных уведомлений: {item.deliveryProblems}. Свяжитесь с участниками через сбор.</li>}
      </ul>
      {!item.recentlyCancelled && !item.needsPlayConfirmation && item.provider.canBring && <button disabled={Boolean(busyId)} onClick={() => bring(item)}>{busyId === item.publicId ? "Сохраняем…" : "Я привезу"}</button>}
    </Card>)}
    {state.data?.hasMore && <Notice>Показаны не все сборы. Полное расписание — в календаре и списке сборов.</Notice>}
  </>;
  return <section className="planning-dashboard" aria-label="Сборы, требующие внимания">
    {collapsible ? <details className="calendar-attention"><summary>Требуют внимания · {rows.length}{state.data?.hasMore ? "+" : ""}</summary><div className="stack">{content}</div></details>
      : <><h2>{personal ? "Сегодня и требует внимания" : "Требуют внимания"}</h2>{content}</>}
  </section>;
}
