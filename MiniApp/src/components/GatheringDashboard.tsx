import { useState } from "react";
import { api, json } from "../api/client";
import { useAsync } from "../hooks/useAsync";
import { Card, Empty, ErrorState, Loading, Notice } from "./Ui";
import type { GameProvider, RecruitmentState } from "../api/types";

type DashboardItem = { recruitment?: RecruitmentState; publicId: string; communityKey: string; community: string; gameName: string; localDateTime: string; isOrganizer: boolean; isToday: boolean; waitlistPosition?: number; belowMinimum: boolean; fullWithWaitlist: boolean; startingSoon: boolean; recentlyCancelled: boolean; publicationFailed: boolean; deliveryProblems: number; notificationUnavailableParticipants: number; provider: GameProvider };
type Dashboard = { items: DashboardItem[]; hasMore: boolean; camp?: { registeredToday: number; gatheringsToday: number; bringingGames: number; availableGames: number } };

export function GatheringDashboard({ communityKey, open }: { communityKey: string; open: (key: string, id: string) => void }) {
  const state = useAsync(() => api<Dashboard>(`/gatherings/dashboard?community=${encodeURIComponent(communityKey)}`), [communityKey]);
  const [busyId, setBusyId] = useState<string>(); const [error, setError] = useState<string>();
  async function bring(item: DashboardItem) { if (busyId) return; setBusyId(item.publicId); setError(undefined); try { await api(`/gatherings/${item.publicId}/bring`, json("POST", { communityKey: item.communityKey })); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusyId(undefined); } }
  if (state.loading) return <Loading />;
  if (state.error) return <ErrorState message={state.error} retry={state.reload} />;
  const rows = (state.data?.items ?? []).filter(item => item.recentlyCancelled || item.recruitment?.belowDesired
    || item.fullWithWaitlist || !item.provider.isConfirmed || item.startingSoon || item.publicationFailed
    || item.deliveryProblems > 0 || item.notificationUnavailableParticipants > 0);
  return <section className="planning-dashboard" aria-label="Сборы, требующие внимания">
    <h2>Требуют внимания</h2>
    {state.data?.camp && <Notice>Сегодня зарегистрированы: {state.data.camp.registeredToday}. Игр точно привезут: {state.data.camp.bringingGames}, могут привезти: {state.data.camp.availableGames}.</Notice>}
    {error && <Notice kind="danger">{error}</Notice>}
    {!rows.length && <Empty>Нет сборов, требующих действий администратора.</Empty>}
    {rows.map(item => <Card key={item.publicId}>
      <button className="ghost" onClick={() => open(item.communityKey, item.publicId)}><strong>{item.gameName}</strong><br />{item.localDateTime} · {item.community}</button>
      <p>{item.isToday ? "Сегодня · " : ""}{item.isOrganizer ? "Вы организатор" : item.waitlistPosition ? `Лист ожидания: ${item.waitlistPosition}` : ""}{item.startingSoon ? " · В ближайшие два часа" : ""}</p>
      {item.recentlyCancelled ? <Notice>Недавно отменён</Notice> : <>
        {item.recruitment && <Notice kind={item.belowMinimum ? "warning" : "info"}>{item.recruitment.text}</Notice>}
        {item.fullWithWaitlist && <Notice>Места заняты, есть лист ожидания</Notice>}
        {!item.provider.isConfirmed && <Notice kind="warning">{item.provider.summary}</Notice>}
        {item.provider.canBring && <button disabled={Boolean(busyId)} onClick={() => bring(item)}>{busyId === item.publicId ? "Сохраняем…" : "Я привезу"}</button>}
      </>}
      {item.publicationFailed && <Notice kind="danger">Не удалось опубликовать объявление — откройте сбор для повтора.</Notice>}
      {!item.recentlyCancelled && item.notificationUnavailableParticipants > 0 && <Notice kind="warning">Участников без доступных уведомлений: {item.notificationUnavailableParticipants}. Попросите их запустить бота.</Notice>}
      {item.deliveryProblems > 0 && <Notice kind="warning">Не подтверждена доставка важных уведомлений: {item.deliveryProblems}. Свяжитесь с участниками через сбор.</Notice>}
    </Card>)}
    {state.data?.hasMore && <Notice>Обзор ограничен первыми 200 сборами каждой выборки. Полное расписание доступно в календаре и списке сборов.</Notice>}
  </section>;
}
