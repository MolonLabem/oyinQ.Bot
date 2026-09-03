import { useEffect, useState } from "react";
import { ApiError, api, json } from "../../api/client";
import type { ClubGame, Community, GatheringDetail, GatheringListPage } from "../../api/types";
import { Badge, Card, ContactLink, Cover, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { GameMeta, GamePicker } from "../../components/GamePicker";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { currentLocalMinute, formatLocalDateTimeInput, isFutureLocalDateTime } from "../../app/format";
import { buildGatheringListQuery, changeGatheringHistoryFilter, changeGatheringView, gatheringHistoryFilter, gatheringListView, initialGatheringListState, type GatheringHistoryFilter, type GatheringListState, type GatheringListView } from "./gatheringListState";
import { normalizePlayerCountRange } from "./playerCountRange";
import { gatheringDateTimeBounds, isWithinCampDateRange, revalidateGatheringStart } from "./gatheringDateRange";
import { GatheringBggLink, GatheringCollectionAction, GatheringTypeTag } from "./GatheringGameMetadata";
import { GameTaxonomy } from "../../components/GameTaxonomy";

export function GatheringsPage({ community, bggAvailable, initialGatheringId, onInitialConsumed, editRegistration, openCollection, backFromInitial }: { community: Community; bggAvailable: boolean; initialGatheringId?: string; onInitialConsumed: () => void; editRegistration: () => void; openCollection: (bggId: number, gatheringId: string) => void; backFromInitial?: () => void }) {
  const [screen, setScreen] = useState<"list" | "create" | "detail">(initialGatheringId ? "detail" : "list");
  const [selected, setSelected] = useState<string | undefined>(initialGatheringId);
  const [initialBack] = useState<(() => void) | undefined>(() => initialGatheringId ? backFromInitial : undefined);
  const [listState, setListState] = useState<GatheringListState>(initialGatheringListState);
  useEffect(() => telegram.back(screen !== "list", () => { if (screen === "detail" && initialBack) initialBack(); else { setScreen("list"); setSelected(undefined); } }), [screen, initialBack]);
  useEffect(() => { if (initialGatheringId) onInitialConsumed(); }, []);
  if (screen === "create") return <CreateGathering community={community} bggAvailable={bggAvailable} onDone={() => setScreen("list")} editRegistration={editRegistration} />;
  if (screen === "detail" && selected) return <GatheringDetails community={community} id={selected} onBack={() => { if (initialBack) initialBack(); else setScreen("list"); }} onCancelled={() => { setListState({ scope: "cancelled", page: 1 }); setSelected(undefined); setScreen("list"); }} editRegistration={editRegistration} openCollection={bggId => openCollection(bggId, selected)} />;
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
    <div className="segmented" role="tablist" aria-label="Раздел сборов"><button role="tab" aria-selected={view === "upcoming"} className={view === "upcoming" ? "active" : ""} onClick={() => selectView("upcoming")}>Предстоящие</button><button role="tab" aria-selected={view === "history"} className={view === "history" ? "active" : ""} onClick={() => selectView("history")}>История</button></div>
    {view === "history" && <div className="segmented history-filters" role="tablist" aria-label="Фильтр истории"><button role="tab" aria-selected={historyFilter === "all"} className={historyFilter === "all" ? "active" : ""} onClick={() => selectHistoryFilter("all")}>Все</button><button role="tab" aria-selected={historyFilter === "completed"} className={historyFilter === "completed" ? "active" : ""} onClick={() => selectHistoryFilter("completed")}>Сыграны</button><button role="tab" aria-selected={historyFilter === "cancelled"} className={historyFilter === "cancelled" ? "active" : ""} onClick={() => selectHistoryFilter("cancelled")}>Отменены</button></div>}
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.items.length ? view === "upcoming" ? <><Empty>Пока нет запланированных сборов.</Empty><button className="primary" onClick={create}>Создать сбор</button></> : <Empty>{historyFilter === "completed" ? "Сыгранных сборов пока нет." : historyFilter === "cancelled" ? "Отменённых сборов нет." : "История сборов пока пуста."}</Empty> :
      <><div className="stack">{state.data.items.map(item => <div className={`gathering-card-shell${item.card.bggUrl ? " has-bgg" : ""}`} key={item.card.publicId}><button className="card gathering-card" onClick={() => open(item.card.publicId)}>
        <Cover src={item.card.imageUrl} name={item.card.gameName} /><div><div className="row"><h2>{item.card.gameName}</h2>{item.isOrganizer && <Badge tone="accent">Вы организатор</Badge>}</div><GatheringTypeTag typeName={item.card.typeName} /><p><span aria-hidden>📅</span> {item.card.localDateTime}</p><p>{item.card.occupiedSeats} / {item.card.maximumPlayers} игроков</p><p className="muted">{item.card.organizerName} · организатор</p>{item.card.canTeachRules && <p>Могу объяснить правила</p>}{item.card.description && <p className="gathering-note">«{item.card.description}»</p>}<span className="muted">{item.card.statusText}</span></div>
      </button>{item.card.bggUrl && <span className="gathering-card-bgg"><GatheringBggLink bggUrl={item.card.bggUrl} compact /></span>}</div>)}</div>{(state.data.hasPrevious || state.data.hasNext) && <div className="row"><button disabled={!state.data.hasPrevious} onClick={() => setListState({ ...listState, page: page - 1 })}>Назад</button><span className="muted">Страница {page}</span><button disabled={!state.data.hasNext} onClick={() => setListState({ ...listState, page: page + 1 })}>Дальше</button></div>}</>}
  </Page>;
}

function CreateGathering({ community, bggAvailable, onDone, editRegistration }: { community: Community; bggAvailable: boolean; onDone: () => void; editRegistration: () => void }) {
  const games = useAsync(() => api<ClubGame[]>(`${community.mode === "Camp" ? "/camp/catalog" : "/games"}?community=${encodeURIComponent(community.key)}`), [community.key, community.mode]);
  const [source, setSource] = useState<"catalog" | "bgg">("catalog"); const [chosen, setChosen] = useState<ClubGame>();
  const [expansions, setExpansions] = useState<number[]>([]); const [starts, setStarts] = useState("");
  const [minimum, setMinimum] = useState(2); const [desired, setDesired] = useState(4); const [maximum, setMaximum] = useState(4);
  const [description, setDescription] = useState(""); const [teach, setTeach] = useState(true); const [reviewing, setReviewing] = useState(false); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [attendanceRequired, setAttendanceRequired] = useState(false);
  const dateBounds = gatheringDateTimeBounds(community, currentLocalMinute(community.timeZoneId));
  useEffect(() => {
    setStarts(current => revalidateGatheringStart(current, community, dateBounds));
    setReviewing(false);
  }, [community.key, community.startDate, community.endDate, dateBounds.min, dateBounds.max]);
  const chosenPlayers = normalizePlayerCountRange(chosen?.minPlayers, chosen?.maxPlayers);
  const gameMinimum = chosenPlayers.minimum;
  const gameMaximum = chosenPlayers.maximum;
  const playerOptions = Array.from({ length: gameMaximum - gameMinimum + 1 }, (_, index) => gameMinimum + index);
  function chooseGame(game: ClubGame, nextSource: "catalog" | "bgg") {
    setSource(nextSource); setChosen(game); setExpansions([]); setError(undefined);
    const players = normalizePlayerCountRange(game.minPlayers, game.maxPlayers);
    const nextMinimum = players.minimum;
    const nextMaximum = players.maximum;
    const suggested = Number.parseInt(game.bestPlayers?.match(/\d+/)?.[0] ?? "", 10);
    const nextDesired = Number.isFinite(suggested) ? Math.min(nextMaximum, Math.max(nextMinimum, suggested)) : nextMaximum;
    setMinimum(nextMinimum); setDesired(nextDesired); setMaximum(nextMaximum);
  }
  async function submit() {
    if (!chosen || !starts) { setError("Выберите игру, дату и время."); return; }
    if (!isWithinCampDateRange(starts, community)) { setError("Дата сбора должна быть в пределах дат кэмпа."); return; }
    setBusy(true); setError(undefined); setAttendanceRequired(false);
    try { await api("/gatherings", json("POST", { communityKey: community.key, gameSource: source, bggId: chosen.bggId, selectedExpansionIds: expansions, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach })); telegram.success("Сбор создан"); onDone(); }
    catch (e) { setAttendanceRequired(e instanceof ApiError && e.code === "camp_attendance_date_required"); setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); }
  }
  function review() { if (!chosen || !starts) { setError("Выберите игру, дату и время."); return; } if (!isWithinCampDateRange(starts, community)) { setError("Дата сбора должна быть в пределах дат кэмпа."); return; } if (!isFutureLocalDateTime(starts, community.timeZoneId)) { setError("Выберите дату и время в будущем."); return; } if (minimum < 1 || minimum > desired || desired > maximum) { setError("Проверьте лимиты игроков: минимум ≤ желаемое число ≤ максимум."); return; } setError(undefined); setReviewing(true); }
  if (reviewing && chosen) {
    const selectedNames = chosen.expansions.filter(expansion => expansions.includes(expansion.bggId)).map(expansion => expansion.name);
    return <Page title="Проверьте сбор" actions={<button onClick={() => setReviewing(false)}>Изменить</button>}><Card><div className="media"><Cover src={chosen.thumbnailImageUrl} name={chosen.name} /><div><h2>{chosen.name}</h2><p>{formatLocalDateTimeInput(starts)}</p></div></div><dl className="review-list"><dt>Игроки</dt><dd>{minimum} / {desired} / {maximum}</dd><dt>Дополнения</dt><dd>{selectedNames.length ? selectedNames.join(", ") : "Без дополнений"}</dd><dt>Правила</dt><dd>{teach ? "Объясню" : "Нужно знать правила"}</dd><dt>Описание</dt><dd>{description.trim() || "Без описания"}</dd></dl></Card>{error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Редактировать регистрацию</button>}</Notice>}<button className="primary sticky-action" disabled={busy} onClick={submit}>{busy ? "Создаём…" : "Подтвердить и создать"}</button></Page>;
  }
  return <Page title="Новый сбор" actions={<button onClick={onDone}>Назад</button>}>
    {!bggAvailable && <Notice kind="warning">BGG временно недоступен. Создать сбор по игре из каталога по-прежнему можно.</Notice>}
    <Card><GamePicker catalog={games.data} catalogLoading={games.loading} catalogError={games.error}
      bggAvailable={bggAvailable} selected={chosen} onSelect={chooseGame}
      onClear={() => { setChosen(undefined); setExpansions([]); }}
      hint="Игры из коллекции найдутся сразу, а поиск в BGG может занять несколько секунд. Можно вставить ссылку или ID." /></Card>
    {chosen && <Card><div className="media"><Cover src={chosen.thumbnailImageUrl} name={chosen.name} /><div><h2>{chosen.name}</h2><GameMeta game={chosen} /></div></div>{chosen.expansions.length > 0 && <fieldset><legend>Дополнения</legend>{chosen.expansions.map(exp => <label className="check" key={exp.bggId}><input type="checkbox" checked={expansions.includes(exp.bggId)} onChange={() => setExpansions(current => current.includes(exp.bggId) ? current.filter(id => id !== exp.bggId) : [...current, exp.bggId])} />{exp.name}</label>)}</fieldset>}{(chosen.playerRangeDefaulted || chosenPlayers.wasDefaulted) && <Notice kind="warning">В BGG не указан полный диапазон игроков. Мы поставили 1–12 — проверьте значения перед созданием сбора.</Notice>}</Card>}
    <Card className="form-grid"><Field label="Дата и время" hint={community.mode === "Camp" ? "Можно выбрать только дату кэмпа" : "Прошедшее время выбрать нельзя"}><input type="datetime-local" min={dateBounds.min} max={dateBounds.max} value={starts} onChange={e => setStarts(e.target.value)} /></Field><div className="limits"><Field label="Минимум"><select value={minimum} onChange={e => { const value = +e.target.value; setMinimum(value); if (desired < value) setDesired(value); }} disabled={!chosen}>{playerOptions.filter(value => value <= desired).map(value => <option key={value}>{value}</option>)}</select></Field><Field label="Оптимально"><select value={desired} onChange={e => setDesired(+e.target.value)} disabled={!chosen}>{playerOptions.filter(value => value >= minimum && value <= maximum).map(value => <option key={value}>{value}</option>)}</select></Field><Field label="Максимум"><select value={maximum} onChange={e => { const value = +e.target.value; setMaximum(value); if (desired > value) setDesired(value); }} disabled={!chosen}>{playerOptions.filter(value => value >= desired).map(value => <option key={value}>{value}</option>)}</select></Field></div><Field label="Описание" hint="Например: играем со всеми дополнениями, новичкам помогу разобраться."><textarea value={description} maxLength={300} placeholder="Необязательно" onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label></Card>
    {error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Редактировать регистрацию</button>}</Notice>}<button className="primary sticky-action" disabled={busy || !chosen} onClick={review}>Проверить сбор</button>
  </Page>;
}

function GatheringDetails({ community, id, onBack, onCancelled, editRegistration, openCollection }: { community: Community; id: string; onBack: () => void; onCancelled: () => void; editRegistration: () => void; openCollection: (bggId: number) => void }) {
  const state = useAsync(() => api<GatheringDetail>(`/gatherings/${id}?community=${encodeURIComponent(community.key)}`), [community.key, id]);
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [attendanceRequired, setAttendanceRequired] = useState(false); const [editing, setEditing] = useState(false); const [cancelling, setCancelling] = useState(false); const [cancellationReason, setCancellationReason] = useState("");
  const [guestName, setGuestName] = useState(""); const [editingGuestId, setEditingGuestId] = useState<number>(); const [editingGuestName, setEditingGuestName] = useState("");
  async function action(path: string, reason?: string) { setBusy(true); setError(undefined); setAttendanceRequired(false); try { await api(`/gatherings/${id}/${path}`, json("POST", { communityKey: community.key, reason })); telegram.success(({ join: "Вы записались на сбор", leave: "Вы вышли из сбора", close: "Запись закрыта", reopen: "Запись открыта", cancel: "Сбор отменён", "publication/retry": "Объявление опубликовано" } as Record<string, string>)[path] ?? "Изменения сохранены"); setCancelling(false); if (path === "cancel") onCancelled(); else state.reload(); } catch (e) { setAttendanceRequired(e instanceof ApiError && e.code === "camp_attendance_date_required"); setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function guestAction(method: "POST" | "PUT" | "DELETE", guestId?: number, displayName?: string) { setBusy(true); setError(undefined); try { await api(`/gatherings/${id}/guests${guestId ? `/${guestId}` : ""}`, json(method, { communityKey: community.key, displayName })); telegram.success(method === "DELETE" ? "Гость удалён" : guestId ? "Имя гостя изменено" : "Гость добавлен"); setGuestName(""); setEditingGuestId(undefined); setEditingGuestName(""); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  if (state.loading) return <Page title="Сбор"><Loading /></Page>; if (state.error || !state.data) return <Page title="Сбор" actions={<button onClick={onBack}>Назад</button>}><ErrorState message={state.error ?? "Сбор не найден"} /></Page>;
  const value = state.data;
  if (editing && value.canEdit) return <EditGathering community={community} id={id} value={value} done={() => { setEditing(false); state.reload(); }} cancel={() => setEditing(false)} />;
  const organizer = value.confirmedParticipants.find(participant => participant.isOrganizer);
  const freeSeats = Math.max(0, value.maximumPlayers - value.gathering.occupiedSeats);
  const occupiedPercent = Math.min(100, Math.round(value.gathering.occupiedSeats / value.maximumPlayers * 100));
  const typeNames = value.gathering.typeNames?.length
    ? value.gathering.typeNames
    : value.gathering.typeName ? [value.gathering.typeName] : [];
  return <Page actions={<button onClick={onBack}>Назад</button>}>
    <Card className="gathering-overview">
      <header className="gathering-overview-header">
        <h1>{value.gathering.bggUrl ? <a className="page-title-link" href={value.gathering.bggUrl} target="_blank" rel="noreferrer">{value.gathering.gameName}</a> : value.gathering.gameName}</h1>
        <div className="gathering-status-row"><Badge tone="accent">{value.gathering.statusText}</Badge><GatheringTypeTag typeName={value.gathering.typeName} /></div>
      </header>
      <div className="gathering-detail-hero">
        <Cover src={value.gathering.imageUrl} name={value.gathering.gameName} />
        <div className="gathering-summary">
          <div className="gathering-key-facts">
            <div><span aria-hidden>📅</span><span><small>Когда</small><strong>{value.gathering.localDateTime}</strong></span></div>
            <div><span aria-hidden>👥</span><span><small>Игроки</small><strong>{value.gathering.occupiedSeats} из {value.maximumPlayers}</strong></span></div>
            <div><span aria-hidden>{freeSeats > 0 ? "✨" : "⏳"}</span><span><small>Запись</small><strong>{freeSeats > 0 ? `Свободно мест: ${freeSeats}` : value.canJoin ? "Лист ожидания" : "Мест нет"}</strong></span></div>
          </div>
          <div className="seat-meter" role="progressbar" aria-label="Занятые места" aria-valuemin={0} aria-valuemax={value.maximumPlayers} aria-valuenow={value.gathering.occupiedSeats}><span style={{ width: `${occupiedPercent}%` }} /></div>
          <p className="capacity-caption">Минимум {value.minimumPlayers} · оптимально {value.desiredPlayers} · максимум {value.maximumPlayers}</p>
          {organizer && <p className="gathering-organizer"><span className="muted">Организатор</span> <ContactLink url={organizer.contactUrl}>{organizer.name}</ContactLink></p>}
          <p className="gathering-rules">{value.canTeachRules ? "📖 " : "🎯 "}{value.gathering.rulesText}</p>
        </div>
      </div>
      {value.gathering.expansions.length > 0 && <div className="gathering-expansions"><strong>Дополнения</strong><div className="tag-list">{value.gathering.expansions.map(name => <span className="tag" key={name}>{name}</span>)}</div></div>}
      {value.gathering.description && <section className="gathering-description"><h2>От организатора</h2><div className="gathering-description-scroll">{value.gathering.description}</div></section>}
      {value.waitlistPosition && <Notice kind="warning">Вы в листе ожидания, позиция {value.waitlistPosition}.</Notice>}
    </Card>
    {value.publicationStatus === "Failed" && <Notice kind="danger"><p>Не удалось опубликовать объявление в Telegram. Сам сбор сохранён.</p>{value.canRetryPublication && <button disabled={busy} onClick={() => action("publication/retry")}>Повторить публикацию</button>}</Notice>}
    {value.hasStarted && <Notice>Время сбора наступило. Запись закрыта автоматически; изменить время или открыть запись снова нельзя.</Notice>}
    {error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Редактировать регистрацию</button>}</Notice>}
    <div className="action-bar gathering-detail-actions">{value.canJoin && <button className="primary" disabled={busy} onClick={() => action("join")}>{freeSeats > 0 ? "Занять место" : "Встать в лист ожидания"}</button>}{value.canLeave && <button className="danger" disabled={busy} onClick={() => action("leave")}>{value.currentUserStatus === "Waitlisted" ? "Выйти из листа ожидания" : "Отказаться от места"}</button>}{value.canEdit && <button onClick={() => setEditing(true)}>Изменить сбор</button>}{value.canClose && <button disabled={busy} onClick={() => action("close")}>Закрыть запись</button>}{value.canReopen && <button disabled={busy} onClick={() => action("reopen")}>Открыть запись</button>}{value.canCancel && <button className="danger ghost" onClick={() => setCancelling(true)}>Отменить сбор</button>}</div>
    {cancelling && <Card className="form-grid"><h2>Отмена сбора</h2><Field label="Причина" hint="Необязательно; участники увидят её в уведомлении"><textarea maxLength={500} value={cancellationReason} onChange={event => setCancellationReason(event.target.value)} /></Field><div className="row"><button onClick={() => setCancelling(false)}>Оставить сбор</button><button className="danger" disabled={busy} onClick={async () => { if (await telegram.confirm("Отменить сбор? Возобновить его будет нельзя.")) await action("cancel", cancellationReason.trim() || undefined); }}>{busy ? "Отменяем…" : "Подтвердить отмену"}</button></div></Card>}
    <Card className="gathering-players">
      <div className="row gathering-section-heading"><h2>Кто играет</h2><Badge tone="neutral">{value.gathering.occupiedSeats} / {value.maximumPlayers}</Badge></div>
      <ul className="participant-roster gathering-roster">
        {value.confirmedParticipants.map((participant, index) => <li key={`${participant.name}-${index}`}><span className="participant-marker" aria-hidden>{participant.isOrganizer ? "★" : index + 1}</span><span><ContactLink url={participant.contactUrl}>{participant.name}</ContactLink>{participant.isOrganizer && <small>Организатор</small>}</span></li>)}
        {value.guestParticipants.map(guest => <li key={guest.id}>{editingGuestId === guest.id ? <div className="inline-form"><input value={editingGuestName} maxLength={80} onChange={event => setEditingGuestName(event.target.value)} /><button disabled={busy || !editingGuestName.trim()} onClick={() => guestAction("PUT", guest.id, editingGuestName)}>Сохранить</button><button onClick={() => setEditingGuestId(undefined)}>Отмена</button></div> : <div className="row guest-row"><span className="participant-marker" aria-hidden>●</span><span className="guest-name">{guest.displayName} <Badge tone="neutral">Гость</Badge></span>{value.canManageGuests && <span className="guest-actions"><button onClick={() => { setEditingGuestId(guest.id); setEditingGuestName(guest.displayName); }}>Изменить</button><button className="danger ghost" disabled={busy} onClick={async () => { if (await telegram.confirm(`Удалить гостя «${guest.displayName}»?`)) await guestAction("DELETE", guest.id); }}>Удалить</button></span>}</div>}</li>)}
      </ul>
      {value.canManageGuests && <div className="inline-form guest-form"><input value={guestName} maxLength={80} placeholder="Имя или описание гостя" onChange={event => setGuestName(event.target.value)} /><button disabled={busy || !guestName.trim()} onClick={() => guestAction("POST", undefined, guestName)}>Добавить гостя</button></div>}
      {value.waitlistedParticipants.length > 0 && <section className="gathering-waitlist"><h3>Лист ожидания <span>{value.waitlistedParticipants.length}</span></h3><ol>{value.waitlistedParticipants.map(participant => <li key={`${participant.position}-${participant.name}`}><span>{participant.position}</span><ContactLink url={participant.contactUrl}>{participant.name}</ContactLink></li>)}</ol></section>}
    </Card>
    <GameTaxonomy className="gathering-taxonomy" typeNames={typeNames} categoryNames={value.gathering.categoryNames} mechanicNames={value.gathering.mechanicNames} />
    {value.gathering.bggId && <div className="gathering-secondary-actions"><GatheringCollectionAction bggId={value.gathering.bggId} open={openCollection} /></div>}
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
  async function save() { if (invalidCampDate) { setError("Дата сбора должна быть в пределах дат кэмпа."); return; } if (!isFutureLocalDateTime(starts, community.timeZoneId)) { setError("Выберите дату и время в будущем."); return; } setBusy(true); setError(undefined); try { await api(`/gatherings/${id}`, json("PUT", { communityKey: community.key, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach, selectedExpansionIds: selected })); telegram.success("Сбор обновлён"); done(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  return <Page title="Изменить сбор" actions={<button onClick={cancel}>Отмена</button>}><Card className="form-grid"><Field label="Дата и время" hint={community.mode === "Camp" ? "Можно выбрать только дату кэмпа" : "Прошедшее время выбрать нельзя"}><input type="datetime-local" min={dateBounds.min} max={dateBounds.max} value={starts} onChange={e => setStarts(e.target.value)} /></Field>{invalidCampDate && <Notice kind="warning">Сохранённая дата находится вне текущих дат кэмпа. Выберите допустимую дату перед сохранением.</Notice>}<div className="limits"><Field label="Минимум"><select value={minimum} onChange={e => { const next = +e.target.value; setMinimum(next); if (desired < next) setDesired(next); }}>{playerOptions.filter(option => option <= desired).map(option => <option key={option}>{option}</option>)}</select></Field><Field label="Оптимально"><select value={desired} onChange={e => setDesired(+e.target.value)}>{playerOptions.filter(option => option >= minimum && option <= maximum).map(option => <option key={option}>{option}</option>)}</select></Field><Field label="Максимум"><select value={maximum} onChange={e => { const next = +e.target.value; setMaximum(next); if (desired > next) setDesired(next); }}>{playerOptions.filter(option => option >= desired).map(option => <option key={option}>{option}</option>)}</select></Field></div>{(value.gamePlayerRangeDefaulted || gamePlayers.wasDefaulted) && <Notice kind="warning">В BGG не указан полный диапазон игроков, поэтому мы поставили 1–12.</Notice>}<Field label="Описание"><textarea maxLength={300} value={description} onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label>{value.knownExpansions.length > 0 && <fieldset><legend>Дополнения</legend>{value.knownExpansions.map(exp => <label className="check" key={exp.bggId}><input type="checkbox" checked={selected.includes(exp.bggId)} onChange={() => setSelected(current => current.includes(exp.bggId) ? current.filter(x => x !== exp.bggId) : [...current, exp.bggId])} />{exp.name}</label>)}</fieldset>}{error && <Notice kind="danger">{error}</Notice>}<button className="primary" disabled={busy || invalidCampDate} onClick={save}>{busy ? "Сохраняем…" : "Сохранить"}</button></Card></Page>;
}
