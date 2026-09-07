import { WishButton } from "../../components/Wishlist";
import { GuestRow } from "./GuestRow";
import { PlayPanel } from "./PlayPanel";
import { GameProviderNotice } from "../../components/GameProviderNotice";
import { useEffect, useState } from "react";
import { ApiError, api, gatheringMutation, json } from "../../api/client";
import type { ClubGame, Community, GatheringDetail, GatheringListPage } from "../../api/types";
import { Badge, Card, ContactLink, Cover, Empty, ErrorState, Field, Loading, Notice, Page, SegmentedControl, Tabs } from "../../components/Ui";
import { GameMeta, GamePicker } from "../../components/GamePicker";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { currentLocalMinute, isFutureLocalDateTime } from "../../app/format";
import { buildGatheringListQuery, changeGatheringHistoryFilter, changeGatheringView, gatheringHistoryFilter, gatheringListView, initialGatheringListState, type GatheringHistoryFilter, type GatheringListState, type GatheringListView } from "./gatheringListState";
import { normalizePlayerCountRange } from "./playerCountRange";
import { gatheringDateTimeBounds, isWithinCampDateRange, revalidateGatheringStart } from "./gatheringDateRange";
import { GatheringBggLink, GatheringCollectionAction, GatheringTypeTag } from "./GatheringGameMetadata";
import { BotStartNotice } from "../../components/BotStartNotice";
import { GameTaxonomy } from "../../components/GameTaxonomy";
import { gatheringStatusTone, participationStatusTone } from "../../app/semanticTones";

export function GatheringsPage({ community, bggAvailable, initialGatheringId, onInitialConsumed, editRegistration, openCollection, backFromInitial }: { community: Community; bggAvailable: boolean; initialGatheringId?: string; onInitialConsumed: () => void; editRegistration: () => void; openCollection: (bggId: number, gatheringId: string) => void; backFromInitial?: () => void }) {
  const [screen, setScreen] = useState<"list" | "create" | "detail">(initialGatheringId ? "detail" : "list");
  const [selected, setSelected] = useState<string | undefined>(initialGatheringId);
  const [initialBack] = useState<(() => void) | undefined>(() => initialGatheringId ? backFromInitial : undefined);
  const [listState, setListState] = useState<GatheringListState>(initialGatheringListState);
  useEffect(() => telegram.back(screen !== "list", () => { if (screen === "detail" && initialBack) initialBack(); else { setScreen("list"); setSelected(undefined); } }), [screen, initialBack]);
  useEffect(() => { if (initialGatheringId) onInitialConsumed(); }, []);
  if (screen === "create") return <CreateGathering community={community} bggAvailable={bggAvailable} onDone={() => setScreen("list")} editRegistration={editRegistration} />;
  if (screen === "detail" && selected) return <GatheringDetails key={`${community.key}-${selected}`} community={community} id={selected} onBack={() => { if (initialBack) initialBack(); else setScreen("list"); }} onCancelled={() => { setListState({ scope: "cancelled", page: 1 }); setSelected(undefined); setScreen("list"); }} editRegistration={editRegistration} openCollection={bggId => openCollection(bggId, selected)} />;
  return <GatheringList community={community} listState={listState} setListState={setListState} open={id => { setSelected(id); setScreen("detail"); }} create={() => setScreen("create")} />;
}

function GatheringList({ community, listState, setListState, open, create }: { community: Community; listState: GatheringListState; setListState: (state: GatheringListState) => void; open: (id: string) => void; create: () => void }) {
  const { scope, page } = listState;
  const view = gatheringListView(scope);
  const historyFilter = gatheringHistoryFilter(scope);
  const state = useAsync(() => api<GatheringListPage>(
    `/gatherings?${buildGatheringListQuery(community.key, listState)}`,
    { cache: "no-store" }
  ), [community.key, scope, page]);
  const selectView = (next: GatheringListView) => setListState(changeGatheringView(listState, next));
  const selectHistoryFilter = (next: GatheringHistoryFilter) => setListState(changeGatheringHistoryFilter(listState, next));
  return <Page title="Сборы" subtitle={community.name} actions={<button className="primary" onClick={create}>Создать сбор</button>}>
    <Tabs label="Раздел сборов" active={view} onChange={id => selectView(id as GatheringListView)} items={[{ id: "upcoming", label: "Предстоящие" }, { id: "history", label: "История" }]} />
    {view === "history" && <SegmentedControl className="history-filters" label="Фильтр истории" active={historyFilter} onChange={id => selectHistoryFilter(id as GatheringHistoryFilter)} items={[{ id: "all", label: "Все" }, { id: "completed", label: "Завершены" }, { id: "cancelled", label: "Отменены" }]} />}
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.items.length ? view === "upcoming" ? <><Empty>Пока нет запланированных сборов.</Empty><button className="primary" onClick={create}>Создать сбор</button></> : <Empty>{historyFilter === "completed" ? "Завершённых сборов пока нет." : historyFilter === "cancelled" ? "Отменённых сборов нет." : "История сборов пока пуста."}</Empty> :
      <><div className="stack">{state.data.items.map(item => { const statusTone = gatheringStatusTone(item.status); return <div className={`gathering-card-shell${item.card.bggUrl ? " has-bgg" : ""}`} key={item.card.publicId}><button className="card gathering-card" onClick={() => open(item.card.publicId)}>
        <Cover src={item.card.imageUrl} name={item.card.gameName} /><div className="gathering-card-body"><div className="row gathering-card-title"><h2>{item.card.gameName}</h2>{item.isOrganizer && <Badge tone="accent">Вы организатор</Badge>}</div><div className="gathering-card-facts"><span><span aria-hidden>📅</span> {item.card.localDateTime}</span><span><span aria-hidden>👥</span> {item.card.occupiedSeats} / {item.card.maximumPlayers}</span></div>{item.card.recruitment?.text && <p className="gathering-card-activity attention">{item.card.recruitment.text}</p>}<Badge tone={statusTone}>{item.card.statusText}</Badge>{item.card.cancellationReason && <p className="muted">Причина: {item.card.cancellationReason}</p>}</div>
      </button>{item.card.bggUrl && <span className="gathering-card-bgg"><GatheringBggLink bggUrl={item.card.bggUrl} compact /></span>}</div>; })}</div>{(state.data.hasPrevious || state.data.hasNext) && <div className="row"><button disabled={!state.data.hasPrevious} onClick={() => setListState({ ...listState, page: page - 1 })}>Назад</button><span className="muted">Страница {page}</span><button disabled={!state.data.hasNext} onClick={() => setListState({ ...listState, page: page + 1 })}>Дальше</button></div>}</>}
  </Page>;
}

export function CreateGathering({ community, bggAvailable, onDone, editRegistration }: { community: Community; bggAvailable: boolean; onDone: () => void; editRegistration: () => void }) {
  const games = useAsync(() => api<ClubGame[]>(`/games?community=${encodeURIComponent(community.key)}`), [community.key, community.mode]);
  const [addToCollection, setAddToCollection] = useState(false); const [bringToCamp, setBringToCamp] = useState(false);
  const [source, setSource] = useState<"catalog" | "bgg">("catalog"); const [chosen, setChosen] = useState<ClubGame>();
  const [expansions, setExpansions] = useState<number[]>([]); const [starts, setStarts] = useState("");
  const [minimum, setMinimum] = useState(2); const [desired, setDesired] = useState(4); const [maximum, setMaximum] = useState(4);
  const [description, setDescription] = useState(""); const [teach, setTeach] = useState(true); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [attendanceRequired, setAttendanceRequired] = useState(false);
  const dateBounds = gatheringDateTimeBounds(community, currentLocalMinute(community.timeZoneId));
  useEffect(() => {
    setStarts(current => revalidateGatheringStart(current, community, dateBounds));
  }, [community.key, community.startDate, community.endDate, dateBounds.min, dateBounds.max]);
  const chosenPlayers = normalizePlayerCountRange(chosen?.minPlayers, chosen?.maxPlayers);
  const gameMinimum = chosenPlayers.minimum;
  const gameMaximum = chosenPlayers.maximum;
  const playerOptions = Array.from({ length: gameMaximum - gameMinimum + 1 }, (_, index) => gameMinimum + index);
  function chooseGame(game: ClubGame, nextSource: "catalog" | "bgg") {
    setAddToCollection(false); setBringToCamp(false); setSource(nextSource); setChosen(game); setExpansions([]); setError(undefined);
    const players = normalizePlayerCountRange(game.minPlayers, game.maxPlayers);
    const nextMinimum = players.minimum;
    const nextMaximum = players.maximum;
    const suggested = Number.parseInt(game.bestPlayers?.match(/\d+/)?.[0] ?? "", 10);
    const nextDesired = Number.isFinite(suggested) ? Math.min(nextMaximum, Math.max(nextMinimum, suggested)) : nextMaximum;
    setMinimum(nextMinimum); setDesired(nextDesired); setMaximum(nextMaximum);
  }
  async function submit() {
    if (busy) return;
    if (!chosen || !starts) { setError("Выберите игру, дату и время."); return; }
    if (!isWithinCampDateRange(starts, community)) { setError("Дата сбора должна быть в пределах дат кэмпа."); return; }
    if (!isFutureLocalDateTime(starts, community.timeZoneId)) { setError("Выберите дату и время в будущем."); return; }
    if (minimum < 1 || minimum > desired || desired > maximum) { setError("Проверьте лимиты игроков: минимум ≤ желаемое число ≤ максимум."); return; }
    setBusy(true); setError(undefined); setAttendanceRequired(false);
    try { await gatheringMutation("/gatherings", json("POST", { communityKey: community.key, gameSource: source, bggId: chosen.bggId, selectedExpansionIds: expansions, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach, addToCollection, bringToCamp })); telegram.success("Сбор создан"); onDone(); }
    catch (e) { setAttendanceRequired(e instanceof ApiError && e.code === "camp_attendance_date_required"); setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); }
  }
  return <Page title="Новый сбор" actions={<button className="ghost page-back" onClick={onDone}><span aria-hidden>← </span>Назад</button>}>
    {!bggAvailable && <Notice kind="warning">BGG временно недоступен. Создать сбор по игре из каталога по-прежнему можно.</Notice>}
    <section className="content-section gathering-create-section"><GamePicker catalog={games.data} catalogLoading={games.loading} catalogError={games.error}
      bggAvailable={bggAvailable} selected={chosen} onSelect={chooseGame}
      onClear={() => { setChosen(undefined); setExpansions([]); }}
      hint="Игры из коллекции найдутся сразу, а поиск в BGG может занять несколько секунд. Можно вставить ссылку или ID." /></section>
    {chosen && <section className="content-section gathering-create-section"><div className="media"><Cover src={chosen.thumbnailImageUrl} name={chosen.name} /><div><h2>{chosen.name}</h2><GameProviderNotice mode={community.mode} communityKey={community.key} bggId={chosen.bggId} startsAtLocal={starts} ownership={source === "bgg" ? { gameName: chosen.name, add: addToCollection, bring: bringToCamp, camp: community.mode === "Camp", setAdd: value => { setAddToCollection(value); if (!value) setBringToCamp(false); }, setBring: setBringToCamp } : undefined} /><WishButton key={chosen.bggId} communityKey={community.key} bggId={chosen.bggId} /><GameMeta game={chosen} /></div></div>{chosen.expansions.length > 0 && <fieldset><legend>Дополнения</legend>{chosen.expansions.map(exp => <label className="check" key={exp.bggId}><input type="checkbox" checked={expansions.includes(exp.bggId)} onChange={() => setExpansions(current => current.includes(exp.bggId) ? current.filter(id => id !== exp.bggId) : [...current, exp.bggId])} />{exp.name}</label>)}</fieldset>}{(chosen.playerRangeDefaulted || chosenPlayers.wasDefaulted) && <Notice kind="warning">В BGG не указан полный диапазон игроков. Мы поставили 1–12 — проверьте значения перед созданием сбора.</Notice>}</section>}
    <section className="content-section gathering-create-section form-grid"><h2>Параметры сбора</h2><Field label="Дата и время" hint={community.mode === "Camp" ? "Можно выбрать только дату кэмпа" : "Прошедшее время выбрать нельзя"}><input type="datetime-local" min={dateBounds.min} max={dateBounds.max} value={starts} onChange={e => setStarts(e.target.value)} /></Field><div className="limits"><Field label="Минимум"><select value={minimum} onChange={e => { const value = +e.target.value; setMinimum(value); if (desired < value) setDesired(value); }} disabled={!chosen}>{playerOptions.filter(value => value <= desired).map(value => <option key={value}>{value}</option>)}</select></Field><Field label="Оптимально"><select value={desired} onChange={e => setDesired(+e.target.value)} disabled={!chosen}>{playerOptions.filter(value => value >= minimum && value <= maximum).map(value => <option key={value}>{value}</option>)}</select></Field><Field label="Максимум"><select value={maximum} onChange={e => { const value = +e.target.value; setMaximum(value); if (desired > value) setDesired(value); }} disabled={!chosen}>{playerOptions.filter(value => value >= desired).map(value => <option key={value}>{value}</option>)}</select></Field></div><Field label="Описание" hint="Например: играем со всеми дополнениями, новичкам помогу разобраться."><textarea value={description} maxLength={300} placeholder="Необязательно" onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label></section>
    {error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Редактировать регистрацию</button>}</Notice>}<button className="primary sticky-action" disabled={busy || !chosen} onClick={submit}>{busy ? "Создаём…" : "Создать сбор"}</button>
  </Page>;
}

export function GatheringDetails({ community, id, onBack, onCancelled, editRegistration, openCollection, readOnly = false }: { community: Community; id: string; onBack: () => void; onCancelled: () => void; editRegistration: () => void; openCollection: (bggId: number) => void; readOnly?: boolean }) {
  const state = useAsync(() => api<GatheringDetail>(`/gatherings/${id}?community=${encodeURIComponent(community.key)}`), [community.key, id]);
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [attendanceRequired, setAttendanceRequired] = useState(false); const [editing, setEditing] = useState(false); const [cancelling, setCancelling] = useState(false); const [cancellationReason, setCancellationReason] = useState("");
  const [recruitmentMessage, setRecruitmentMessage] = useState<string>();
  async function requestRecruitment() { if (busy) return; setBusy(true); setError(undefined); try { const result = await api<{ message: string }>(`/gatherings/${id}/recruitment`, json("POST", { communityKey: community.key })); setRecruitmentMessage(result.message); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  const [guestName, setGuestName] = useState("");
  async function action(path: string, reason?: string) { if (busy) return; setBusy(true); setError(undefined); setAttendanceRequired(false); try { await gatheringMutation(`/gatherings/${id}/${path}`, json("POST", { communityKey: community.key, reason })); telegram.success(({ join: "Вы записались на сбор", leave: "Вы вышли из сбора", close: "Запись закрыта", reopen: "Запись открыта", cancel: "Сбор отменён", "publication/retry": "Объявление опубликовано" } as Record<string, string>)[path] ?? "Изменения сохранены"); setCancelling(false); if (path === "cancel") onCancelled(); else state.reload(); } catch (e) { setAttendanceRequired(e instanceof ApiError && e.code === "camp_attendance_date_required"); setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function guestAction(method: "POST" | "PUT" | "DELETE", guestId?: number, displayName?: string) { if (busy) return false; setBusy(true); setError(undefined); try { await api(`/gatherings/${id}/guests${guestId ? `/${guestId}` : ""}`, json(method, { communityKey: community.key, displayName })); telegram.success(method === "DELETE" ? "Гость удалён" : guestId ? "Имя гостя изменено" : "Гость добавлен"); if (method === "POST") setGuestName(""); state.reload(); return true; } catch (e) { setError(e instanceof Error ? e.message : String(e)); return false; } finally { setBusy(false); } }
  if (state.loading && !state.data) return <Page title="Сбор"><Loading /></Page>; if (state.error || !state.data) return <Page title="Сбор" actions={<button onClick={onBack}>Назад</button>}><ErrorState message={state.error ?? "Сбор не найден"} /></Page>;
  const value = state.data;
  const working = busy || state.loading;
  if (editing && value.canEdit) return <EditGathering community={community} id={id} value={value} done={() => { setEditing(false); state.reload(); }} cancel={() => setEditing(false)} />;
  const organizer = value.confirmedParticipants.find(participant => participant.isOrganizer);
  const freeSeats = Math.max(0, value.maximumPlayers - value.gathering.occupiedSeats);
  const occupiedPercent = Math.min(100, Math.round(value.gathering.occupiedSeats / value.maximumPlayers * 100));
  const statusTone = gatheringStatusTone(value.status);
  const typeNames = value.gathering.typeNames?.length
    ? value.gathering.typeNames
    : value.gathering.typeName ? [value.gathering.typeName] : [];
  const canManage = (!readOnly && (value.canEdit || value.canClose || value.canReopen || value.canCancel || value.canRequestRecruitment)) || value.canRetryPublication;
  return <Page>
    <div className="gathering-detail-nav"><button className="ghost" onClick={onBack}><span aria-hidden>← </span>Назад</button></div>
    <Card className="gathering-overview">
      <header className="gathering-overview-header">
        <h1>{value.gathering.bggUrl ? <a className="page-title-link" href={value.gathering.bggUrl} target="_blank" rel="noreferrer">{value.gathering.gameName}</a> : value.gathering.gameName}</h1>
        <div className="gathering-status-row"><Badge tone={statusTone}>{value.gathering.statusText}</Badge><GatheringTypeTag typeName={value.gathering.typeName} /></div>
      </header>
      <div className="gathering-detail-hero">
        <Cover src={value.gathering.imageUrl} name={value.gathering.gameName} />
        <div className="gathering-summary">
          <div className="gathering-key-facts">
            <div><span aria-hidden>📅</span><span><small>Когда</small><strong>{value.gathering.localDateTime}</strong></span></div>
            <div><span aria-hidden>👥</span><span><small>Игроки</small><strong>{value.gathering.occupiedSeats} из {value.maximumPlayers}</strong></span></div>
            <div><span aria-hidden>{freeSeats > 0 ? "✨" : "⏳"}</span><span><small>Запись</small><strong>{freeSeats > 0 ? `Свободно мест: ${freeSeats}` : value.canJoin ? "Лист ожидания" : "Мест нет"}</strong></span></div>
          </div>
          <div className={`seat-meter ${statusTone}`} role="progressbar" aria-label="Занятые места" aria-valuemin={0} aria-valuemax={value.maximumPlayers} aria-valuenow={value.gathering.occupiedSeats}><span style={{ width: `${occupiedPercent}%` }} /></div>
          {value.gathering.recruitment && <p>{value.gathering.recruitment.text}</p>}
          <p className="capacity-caption">Минимум {value.minimumPlayers} · оптимально {value.desiredPlayers} · максимум {value.maximumPlayers}</p>
          {organizer && <p className="gathering-organizer"><span className="muted">Организатор</span> <ContactLink url={organizer.contactUrl}>{organizer.name}</ContactLink></p>}
          <p className="gathering-rules">{value.canTeachRules ? "📖 " : "🎯 "}{value.gathering.rulesText}</p>
        </div>
      </div>
      {!readOnly && (value.canJoin || value.canLeave) && <div className="gathering-participation" aria-label="Участие в сборе">
        {value.canJoin && <button className="primary" disabled={working} onClick={() => action("join")}>{busy ? "Сохраняем…" : freeSeats > 0 ? "Занять место" : "Встать в лист ожидания"}</button>}
        {value.canLeave && <><Badge tone={participationStatusTone(value.currentUserStatus)}>{value.currentUserStatus === "Waitlisted" ? `Ваша позиция в очереди: ${value.waitlistPosition ?? "—"}` : "Вы записаны на сбор"}</Badge><button className="ghost" disabled={working} onClick={() => action("leave")}>{value.currentUserStatus === "Waitlisted" ? "Выйти из листа ожидания" : "Отказаться от места"}</button></>}
      </div>}
      {value.gathering.cancellationReason && <Notice kind="danger">Причина отмены: {value.gathering.cancellationReason}</Notice>}
      {community.mode === "Club" && value.provider && <p className={`gathering-box-status availability${value.provider.isConfirmed || value.provider.isOwned ? " success" : value.provider.providers.length > 1 ? " warning" : ""}`}>Коробка · {value.provider.summary}</p>}
      {value.gathering.expansions.length > 0 && <div className="gathering-expansions"><strong>Дополнения</strong><div className="tag-list">{value.gathering.expansions.map(name => <span className="tag" key={name}>{name}</span>)}</div></div>}
      {value.gathering.description && <section className="gathering-description"><h2>От организатора</h2><div className="gathering-description-scroll">{value.gathering.description}</div></section>}
    </Card>
    <div className="gathering-feedback">
    {!readOnly && <BotStartNotice required={value.botStartRequired} startUrl={value.startUrl} refresh={state.reload} />}
    {error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Редактировать регистрацию</button>}</Notice>}
    </div>
    {value.hasStarted && <div className="gathering-context-note"><Notice>Время сбора наступило. Запись закрыта автоматически; изменить время или открыть запись снова нельзя.</Notice></div>}
    {canManage && <Card className="gathering-management">
      <details open={value.publicationStatus === "Failed" ? true : undefined}>
        <summary><span><strong>Управление сбором</strong><small>{value.publicationStatus === "Failed" ? "Объявление требует внимания" : "Настройки, запись и приглашение игроков"}</small></span></summary>
        <div className="gathering-management-content">
          {!readOnly && (value.canEdit || value.canClose || value.canReopen) && <div className="gathering-management-buttons">
            {value.canEdit && <button disabled={working} onClick={() => setEditing(true)}>Изменить сбор</button>}
            {value.canClose && <button disabled={working} onClick={() => action("close")}>Закрыть запись</button>}
            {value.canReopen && <button disabled={working} onClick={() => action("reopen")}>Открыть запись</button>}
          </div>}
          {value.publicationStatus === "Failed" && <Notice kind="danger"><p>Объявление в Telegram не удалось обновить. Оно может показывать прежние данные.</p>{value.canRetryPublication && <button disabled={working} onClick={() => action("publication/retry")}>Повторить обновление</button>}</Notice>}
          {!readOnly && (value.canRequestRecruitment || recruitmentMessage || value.recruitmentDelivery) && <section className="gathering-recruitment-action">
            <h3>Пригласить игроков</h3>
            {value.canRequestRecruitment && <><p className="muted">В группу придёт одно напоминание обо всех ближайших сборах, которым нужны игроки.</p><button disabled={working} onClick={() => void requestRecruitment()}>Напомнить о сборе</button></>}
            {recruitmentMessage && <Notice>{recruitmentMessage}</Notice>}
            {value.recruitmentDelivery && <div className="gathering-delivery-status"><p className="muted" role="status">{value.recruitmentDelivery}</p><button className="ghost" disabled={working} onClick={state.reload}>Обновить статус</button></div>}
          </section>}
          {!readOnly && value.canCancel && <div className="gathering-cancel-action">
            {!cancelling && <button className="danger ghost" disabled={working} onClick={() => setCancelling(true)}>Отменить сбор</button>}
    {cancelling && <div className="form-grid gathering-cancel-form"><h3>Отмена сбора</h3><Field label="Причина" hint="Необязательно; участники увидят её в уведомлении"><textarea maxLength={500} value={cancellationReason} onChange={event => setCancellationReason(event.target.value)} /></Field><div className="row"><button onClick={() => setCancelling(false)}>Оставить сбор</button><button className="danger" disabled={working} onClick={async () => { if (await telegram.confirm("Отменить сбор? Возобновить его будет нельзя.")) await action("cancel", cancellationReason.trim() || undefined); }}>{busy ? "Отменяем…" : "Подтвердить отмену"}</button></div></div>}
          </div>}
        </div>
      </details>
    </Card>}
    <section className="content-section gathering-players">
      <div className="row gathering-section-heading"><h2>Кто играет</h2><Badge tone="neutral">{value.gathering.occupiedSeats} / {value.maximumPlayers}</Badge></div>
      <ul className="participant-roster gathering-roster">
        {value.confirmedParticipants.map((participant, index) => <li key={`${participant.name}-${index}`}><span className={`participant-marker ${participant.isOrganizer ? "organizer" : "confirmed"}`} aria-hidden>{participant.isOrganizer ? "★" : index + 1}</span><span><ContactLink url={participant.contactUrl}>{participant.name}</ContactLink>{participant.isOrganizer && <small>Организатор</small>}</span></li>)}
        {value.guestParticipants.map(guest => <GuestRow key={guest.id} name={guest.displayName} editable={!readOnly && value.canManageGuests} busy={working} rename={name => guestAction("PUT", guest.id, name)} remove={() => guestAction("DELETE", guest.id)} />)}
      </ul>
      {!readOnly && value.canManageGuests && <div className="inline-form guest-form"><input value={guestName} maxLength={80} placeholder="Имя или описание гостя" onChange={event => setGuestName(event.target.value)} /><button disabled={working || !guestName.trim()} onClick={() => guestAction("POST", undefined, guestName)}>Добавить гостя</button></div>}
      {value.waitlistedParticipants.length > 0 && <section className="gathering-waitlist"><h3>Лист ожидания <span>{value.waitlistedParticipants.length}</span></h3><ol>{value.waitlistedParticipants.map(participant => <li key={`${participant.position}-${participant.name}`}><span>{participant.position}</span><ContactLink url={participant.contactUrl}>{participant.name}</ContactLink></li>)}</ol></section>}
    </section>
    {community.mode === "Camp" && value.provider && <section className="content-section gathering-provider"><h2>Коробка</h2><Notice kind={value.provider.isConfirmed ? "success" : "warning"}>{value.provider.summary}</Notice>{value.provider.providers.map(p => <p key={p.participantId}>{p.displayName} — <span className={`provider-status${p.commitment === "Bringing" ? " success" : ""}`}>{p.commitment === "Bringing" ? "привезёт" : "может привезти"}</span></p>)}{!readOnly && value.provider.canBring && !value.hasStarted && <button className="primary" disabled={working} onClick={() => action("bring")}>Я привезу</button>}</section>}
    {value.canRecordPlay && <div className="gathering-play-section"><PlayPanel community={community} id={id} /></div>}
    <Card className="gathering-game-info">
      <details>
        <summary>Об игре</summary>
        <GameTaxonomy className="gathering-taxonomy" typeNames={typeNames} categoryNames={value.gathering.categoryNames} mechanicNames={value.gathering.mechanicNames} />
      </details>
      {!readOnly && value.gathering.bggId && <div className="gathering-game-actions">
        <WishButton key={value.gathering.bggId} communityKey={community.key} bggId={value.gathering.bggId} />
        <GatheringCollectionAction bggId={value.gathering.bggId} open={openCollection} />
      </div>}
    </Card>
  </Page>;
}

function EditGathering({ community, id, value, done, cancel }: { community: Community; id: string; value: GatheringDetail; done: () => void; cancel: () => void }) {
  const [starts, setStarts] = useState(value.startsAtLocal); const [minimum, setMinimum] = useState(value.minimumPlayers); const [desired, setDesired] = useState(value.desiredPlayers); const [maximum, setMaximum] = useState(value.maximumPlayers); const [description, setDescription] = useState(value.description ?? ""); const [teach, setTeach] = useState(value.canTeachRules); const [selected, setSelected] = useState(value.selectedExpansionIds); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const gamePlayers = normalizePlayerCountRange(value.gameMinimumPlayers, value.gameMaximumPlayers);
  const gameMinimum = gamePlayers.minimum;
  const gameMaximum = gamePlayers.maximum;
  const playerOptions = Array.from({ length: gameMaximum - gameMinimum + 1 }, (_, index) => gameMinimum + index);
  const dateBounds = gatheringDateTimeBounds(community, currentLocalMinute(community.timeZoneId));
  const invalidCampDate = !isWithinCampDateRange(starts, community);
  async function save() { if (busy) return; if (invalidCampDate) { setError("Дата сбора должна быть в пределах дат кэмпа."); return; } if (!isFutureLocalDateTime(starts, community.timeZoneId)) { setError("Выберите дату и время в будущем."); return; } setBusy(true); setError(undefined); try { await gatheringMutation(`/gatherings/${id}`, json("PUT", { communityKey: community.key, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach, selectedExpansionIds: selected })); telegram.success("Сбор обновлён"); done(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  return <Page title="Изменить сбор" actions={<button className="ghost" onClick={cancel}>Отмена</button>}><section className="content-section form-grid"><Field label="Дата и время" hint={community.mode === "Camp" ? "Можно выбрать только дату кэмпа" : "Прошедшее время выбрать нельзя"}><input type="datetime-local" min={dateBounds.min} max={dateBounds.max} value={starts} onChange={e => setStarts(e.target.value)} /></Field>{invalidCampDate && <Notice kind="warning">Сохранённая дата находится вне текущих дат кэмпа. Выберите допустимую дату перед сохранением.</Notice>}<div className="limits"><Field label="Минимум"><select value={minimum} onChange={e => { const next = +e.target.value; setMinimum(next); if (desired < next) setDesired(next); }}>{playerOptions.filter(option => option <= desired).map(option => <option key={option}>{option}</option>)}</select></Field><Field label="Оптимально"><select value={desired} onChange={e => setDesired(+e.target.value)}>{playerOptions.filter(option => option >= minimum && option <= maximum).map(option => <option key={option}>{option}</option>)}</select></Field><Field label="Максимум"><select value={maximum} onChange={e => { const next = +e.target.value; setMaximum(next); if (desired > next) setDesired(next); }}>{playerOptions.filter(option => option >= desired).map(option => <option key={option}>{option}</option>)}</select></Field></div>{(value.gamePlayerRangeDefaulted || gamePlayers.wasDefaulted) && <Notice kind="warning">В BGG не указан полный диапазон игроков, поэтому мы поставили 1–12.</Notice>}<Field label="Описание"><textarea maxLength={300} value={description} onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label>{value.knownExpansions.length > 0 && <fieldset><legend>Дополнения</legend>{value.knownExpansions.map(exp => <label className="check" key={exp.bggId}><input type="checkbox" checked={selected.includes(exp.bggId)} onChange={() => setSelected(current => current.includes(exp.bggId) ? current.filter(x => x !== exp.bggId) : [...current, exp.bggId])} />{exp.name}</label>)}</fieldset>}{error && <Notice kind="danger">{error}</Notice>}<button className="primary" disabled={busy || invalidCampDate} onClick={save}>{busy ? "Сохраняем…" : "Сохранить"}</button></section></Page>;
}
