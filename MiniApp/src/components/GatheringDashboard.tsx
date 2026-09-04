import { useState } from "react";
import { api, json } from "../api/client";
import { useAsync } from "../hooks/useAsync";
import { Card, Empty, ErrorState, Loading, Notice } from "./Ui";
import type { GameProvider } from "../api/types";

type DashboardItem = { publicId: string; communityKey: string; community: string; gameName: string; localDateTime: string; isOrganizer: boolean; isToday: boolean; waitlistPosition?: number; belowMinimum: boolean; fullWithWaitlist: boolean; startingSoon: boolean; recentlyCancelled: boolean; publicationFailed: boolean; deliveryProblems: number; notificationUnavailableParticipants: number; provider: GameProvider };
type Dashboard = { items: DashboardItem[]; hasMore: boolean; camp?: { registeredToday: number; gatheringsToday: number; bringingGames: number; availableGames: number } };

export function GatheringDashboard({ communityKey, organizer = false, open }: { communityKey: string; organizer?: boolean; open: (key: string, id: string) => void }) {
  const state = useAsync(() => api<Dashboard>(`${organizer ? "/gatherings" : "/profile"}/dashboard?community=${encodeURIComponent(communityKey)}`), [communityKey, organizer]);
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [all, setAll] = useState(false);
  async function bring(item: DashboardItem) { setBusy(true); setError(undefined); try { await api(`/gatherings/${item.publicId}/bring`, json("POST", { communityKey: item.communityKey })); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  if (state.loading) return <Loading />;
  if (state.error) return <ErrorState message={state.error} retry={state.reload} />;
  const rows = state.data?.items ?? [];
  return <section className="planning-dashboard" aria-label={organizer ? "Обзор организатора" : "Что дальше"}>
    <h2>{organizer ? "Обзор организатора" : "Что дальше"}</h2>
    {organizer && <p>Будущие сборы: {rows.filter(x => !x.recentlyCancelled).length} · Сегодня: {rows.filter(x => x.isToday && !x.recentlyCancelled).length}</p>}
    {state.data?.camp && <Notice>Сегодня зарегистрированы: {state.data.camp.registeredToday}. Игр точно привезут: {state.data.camp.bringingGames}, могут привезти: {state.data.camp.availableGames}.</Notice>}
    {error && <Notice kind="danger">{error}</Notice>}
    {!rows.length && <Empty>{organizer ? "Пока нет сборов, которыми вы управляете." : "Ближайших сборов и действий пока нет."}</Empty>}
    {(all ? rows : rows.slice(0, 8)).map(item => <Card key={item.publicId}>
      <button className="ghost" onClick={() => open(item.communityKey, item.publicId)}><strong>{item.gameName}</strong><br />{item.localDateTime} · {item.community}</button>
      <p>{item.isToday ? "Сегодня · " : ""}{item.isOrganizer ? "Вы организатор" : item.waitlistPosition ? `Лист ожидания: ${item.waitlistPosition}` : ""}{item.startingSoon ? " · В ближайшие два часа" : ""}</p>
      {item.recentlyCancelled ? <Notice>Недавно отменён</Notice> : <>
        {organizer && item.belowMinimum && <Notice kind="warning">Игроков меньше минимума</Notice>}
        {organizer && item.fullWithWaitlist && <Notice>Места заняты, есть лист ожидания</Notice>}
        {!item.provider.isConfirmed && <Notice kind="warning">{item.provider.summary}</Notice>}
        {item.provider.canBring && <button disabled={busy} onClick={() => bring(item)}>Я привезу</button>}
      </>}
      {organizer && item.publicationFailed && <Notice kind="danger">Не удалось опубликовать объявление — откройте сбор для повтора.</Notice>}
      {organizer && !item.recentlyCancelled && item.notificationUnavailableParticipants > 0 && <Notice kind="warning">Участников без доступных уведомлений: {item.notificationUnavailableParticipants}. Попросите их запустить бота.</Notice>}
      {organizer && item.deliveryProblems > 0 && <Notice kind="warning">Не подтверждена доставка важных уведомлений: {item.deliveryProblems}. Свяжитесь с участниками через сбор.</Notice>}
    </Card>)}
    {!all && rows.length > 8 && <button onClick={() => setAll(true)}>Показать все</button>}
    {state.data?.hasMore && <Notice>Обзор ограничен первыми 200 сборами каждой выборки. Полное расписание доступно в календаре и списке сборов.</Notice>}
  </section>;
}
