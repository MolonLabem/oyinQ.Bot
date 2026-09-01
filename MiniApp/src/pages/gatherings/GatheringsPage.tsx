import { useEffect, useState } from "react";
import { ApiError, api, json } from "../../api/client";
import type { ClubGame, Community, GatheringDetail, GatheringListPage } from "../../api/types";
import { Badge, Card, ContactLink, Cover, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { GameMeta, GamePicker } from "../../components/GamePicker";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { buildGatheringListQuery, changeGatheringHistoryFilter, changeGatheringView, initialGatheringListState, type GatheringHistoryFilter, type GatheringListState, type GatheringListView } from "./gatheringListState";

function currentLocalMinute() {
  const now = new Date();
  now.setMinutes(now.getMinutes() + 1, 0, 0);
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}T${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`;
}

function isFutureLocal(value: string) { return Boolean(value) && new Date(value).getTime() > Date.now(); }

export function GatheringsPage({ community, bggAvailable, initialGatheringId, onInitialConsumed, editRegistration }: { community: Community; bggAvailable: boolean; initialGatheringId?: string; onInitialConsumed: () => void; editRegistration: () => void }) {
  const [screen, setScreen] = useState<"list" | "create" | "detail">(initialGatheringId ? "detail" : "list");
  const [selected, setSelected] = useState<string | undefined>(initialGatheringId);
  const [listState, setListState] = useState<GatheringListState>(initialGatheringListState);
  useEffect(() => telegram.back(screen !== "list", () => { setScreen("list"); setSelected(undefined); }), [screen]);
  useEffect(() => { if (initialGatheringId) onInitialConsumed(); }, []);
  if (screen === "create") return <CreateGathering community={community} bggAvailable={bggAvailable} onDone={() => setScreen("list")} editRegistration={editRegistration} />;
  if (screen === "detail" && selected) return <GatheringDetails community={community} id={selected} onBack={() => setScreen("list")} onCancelled={() => { setListState({ view: "history", historyFilter: "cancelled", page: 1 }); setSelected(undefined); setScreen("list"); }} editRegistration={editRegistration} />;
  return <GatheringList community={community} listState={listState} setListState={setListState} open={id => { setSelected(id); setScreen("detail"); }} create={() => setScreen("create")} />;
}

function GatheringList({ community, listState, setListState, open, create }: { community: Community; listState: GatheringListState; setListState: (state: GatheringListState) => void; open: (id: string) => void; create: () => void }) {
  const { view, historyFilter, page } = listState;
  const state = useAsync(() => api<GatheringListPage>(`/gatherings?${buildGatheringListQuery(community.key, listState)}`, { cache: "no-store" }), [community.key, view, historyFilter, page]);
  const selectView = (next: GatheringListView) => setListState(changeGatheringView(listState, next));
  const selectHistoryFilter = (next: GatheringHistoryFilter) => setListState(changeGatheringHistoryFilter(listState, next));
  return <Page title="Сборы" subtitle={community.name} actions={<button className="primary" onClick={create}>Создать сбор</button>}>
    <div className="segmented"><button className={view === "upcoming" ? "active" : ""} onClick={() => selectView("upcoming")}>Предстоящие</button><button className={view === "history" ? "active" : ""} onClick={() => selectView("history")}>История</button></div>
    {view === "history" && <div className="segmented"><button className={historyFilter === "all" ? "active" : ""} onClick={() => selectHistoryFilter("all")}>Все</button><button className={historyFilter === "completed" ? "active" : ""} onClick={() => selectHistoryFilter("completed")}>Сыграны</button><button className={historyFilter === "cancelled" ? "active" : ""} onClick={() => selectHistoryFilter("cancelled")}>Отменены</button></div>}
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.items.length ? view === "upcoming" ? <><Empty>Пока нет запланированных сборов.</Empty><button className="primary" onClick={create}>Создать сбор</button></> : <Empty>{historyFilter === "completed" ? "Сыгранных сборов пока нет." : historyFilter === "cancelled" ? "Отменённых сборов нет." : "История сборов пока пуста."}</Empty> :
      <><div className="stack">{state.data.items.map(item => <button className="card gathering-card" key={item.card.publicId} onClick={() => open(item.card.publicId)}>
        <Cover src={item.card.imageUrl} name={item.card.gameName} /><div><div className="row"><h2>{item.card.gameName}</h2>{item.isOrganizer && <Badge tone="accent">Вы организатор</Badge>}</div><div className="tag-list"><Badge tone="neutral">{item.card.typeName}</Badge></div><p>{item.card.localDateTime}</p><p>{item.card.confirmedPlayers} / {item.card.maximumPlayers} игроков</p><p className="muted">{item.card.organizerName} · организатор</p>{item.card.canTeachRules && <p>Могу объяснить правила</p>}{item.card.description && <p className="gathering-note">«{item.card.description}»</p>}<span className="muted">{item.card.statusText}</span></div>
      </button>)}</div>{(state.data.hasPrevious || state.data.hasNext) && <div className="row"><button disabled={!state.data.hasPrevious} onClick={() => setListState({ ...listState, page: page - 1 })}>Назад</button><span className="muted">Страница {page}</span><button disabled={!state.data.hasNext} onClick={() => setListState({ ...listState, page: page + 1 })}>Дальше</button></div>}</>}
  </Page>;
}

function CreateGathering({ community, bggAvailable, onDone, editRegistration }: { community: Community; bggAvailable: boolean; onDone: () => void; editRegistration: () => void }) {
  const games = useAsync(() => api<ClubGame[]>(`${community.mode === "Camp" ? "/camp/catalog" : "/games"}?community=${encodeURIComponent(community.key)}`), [community.key, community.mode]);
  const [source, setSource] = useState<"catalog" | "bgg">("catalog"); const [chosen, setChosen] = useState<ClubGame>();
  const [expansions, setExpansions] = useState<number[]>([]); const [starts, setStarts] = useState("");
  const [minimum, setMinimum] = useState(2); const [desired, setDesired] = useState(4); const [maximum, setMaximum] = useState(4);
  const [description, setDescription] = useState(""); const [teach, setTeach] = useState(true); const [reviewing, setReviewing] = useState(false); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [attendanceRequired, setAttendanceRequired] = useState(false);
  const minimumStart = currentLocalMinute();
  const gameMinimum = chosen?.minPlayers ?? 1;
  const gameMaximum = Math.max(gameMinimum, chosen?.maxPlayers ?? 12);
  const playerOptions = Array.from({ length: gameMaximum - gameMinimum + 1 }, (_, index) => gameMinimum + index);
  function chooseGame(game: ClubGame, nextSource: "catalog" | "bgg") {
    setSource(nextSource); setChosen(game); setExpansions([]); setError(undefined);
    const nextMinimum = game.minPlayers ?? 1;
    const nextMaximum = Math.max(nextMinimum, game.maxPlayers ?? 12);
    const suggested = Number.parseInt(game.bestPlayers?.match(/\d+/)?.[0] ?? "", 10);
    const nextDesired = Number.isFinite(suggested) ? Math.min(nextMaximum, Math.max(nextMinimum, suggested)) : nextMaximum;
    setMinimum(nextMinimum); setDesired(nextDesired); setMaximum(nextMaximum);
  }
  async function submit() {
    if (!chosen || !starts) { setError("Выберите игру, дату и время."); return; }
    setBusy(true); setError(undefined); setAttendanceRequired(false);
    try { await api("/gatherings", json("POST", { communityKey: community.key, gameSource: source, bggId: chosen.bggId, selectedExpansionIds: expansions, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach })); telegram.success("Сбор создан"); onDone(); }
    catch (e) { setAttendanceRequired(e instanceof ApiError && e.code === "camp_attendance_date_required"); setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); }
  }
  function review() { if (!chosen || !starts) { setError("Выберите игру, дату и время."); return; } if (!isFutureLocal(starts)) { setError("Выберите дату и время в будущем."); return; } if (minimum < 1 || minimum > desired || desired > maximum) { setError("Проверьте лимиты игроков: минимум ≤ желаемое число ≤ максимум."); return; } setError(undefined); setReviewing(true); }
  if (reviewing && chosen) {
    const selectedNames = chosen.expansions.filter(expansion => expansions.includes(expansion.bggId)).map(expansion => expansion.name);
    return <Page title="Проверьте сбор" actions={<button onClick={() => setReviewing(false)}>Изменить</button>}><Card><div className="media"><Cover src={chosen.thumbnailImageUrl} name={chosen.name} /><div><h2>{chosen.name}</h2><p>{new Date(starts).toLocaleString("ru-RU", { dateStyle: "long", timeStyle: "short" })}</p></div></div><dl className="review-list"><dt>Игроки</dt><dd>{minimum} / {desired} / {maximum}</dd><dt>Дополнения</dt><dd>{selectedNames.length ? selectedNames.join(", ") : "Без дополнений"}</dd><dt>Правила</dt><dd>{teach ? "Объясню" : "Нужно знать правила"}</dd><dt>Описание</dt><dd>{description.trim() || "Без описания"}</dd></dl></Card>{error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Редактировать регистрацию</button>}</Notice>}<button className="primary sticky-action" disabled={busy} onClick={submit}>{busy ? "Создаём…" : "Подтвердить и создать"}</button></Page>;
  }
  return <Page title="Новый сбор" actions={<button onClick={onDone}>Назад</button>}>
    {!bggAvailable && <Notice kind="warning">BGG временно недоступен. Создать сбор по игре из каталога по-прежнему можно.</Notice>}
    <Card><GamePicker catalog={games.data} catalogLoading={games.loading} catalogError={games.error}
      bggAvailable={bggAvailable} selected={chosen} onSelect={chooseGame}
      onClear={() => { setChosen(undefined); setExpansions([]); }}
      hint="Игры из коллекции найдутся сразу, а поиск в BGG может занять несколько секунд. Можно вставить ссылку или ID." /></Card>
    {chosen && <Card><div className="media"><Cover src={chosen.thumbnailImageUrl} name={chosen.name} /><div><h2>{chosen.name}</h2><GameMeta game={chosen} /></div></div>{chosen.expansions.length > 0 && <fieldset><legend>Дополнения</legend>{chosen.expansions.map(exp => <label className="check" key={exp.bggId}><input type="checkbox" checked={expansions.includes(exp.bggId)} onChange={() => setExpansions(current => current.includes(exp.bggId) ? current.filter(id => id !== exp.bggId) : [...current, exp.bggId])} />{exp.name}</label>)}</fieldset>}{(!chosen.minPlayers || !chosen.maxPlayers) && <Notice kind="warning">В BGG не указан полный диапазон игроков. Мы поставили 1–12 — проверьте значения перед созданием сбора.</Notice>}</Card>}
    <Card className="form-grid"><Field label="Дата и время" hint="Прошедшее время выбрать нельзя"><input type="datetime-local" min={minimumStart} value={starts} onChange={e => setStarts(e.target.value)} /></Field><div className="limits"><Field label="Минимум"><select value={minimum} onChange={e => { const value = +e.target.value; setMinimum(value); if (desired < value) setDesired(value); }} disabled={!chosen}>{playerOptions.filter(value => value <= desired).map(value => <option key={value}>{value}</option>)}</select></Field><Field label="Оптимально"><select value={desired} onChange={e => setDesired(+e.target.value)} disabled={!chosen}>{playerOptions.filter(value => value >= minimum && value <= maximum).map(value => <option key={value}>{value}</option>)}</select></Field><Field label="Максимум"><select value={maximum} onChange={e => { const value = +e.target.value; setMaximum(value); if (desired > value) setDesired(value); }} disabled={!chosen}>{playerOptions.filter(value => value >= desired).map(value => <option key={value}>{value}</option>)}</select></Field></div><Field label="Описание" hint="Например: играем со всеми дополнениями, новичкам помогу разобраться."><textarea value={description} maxLength={300} placeholder="Необязательно" onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label></Card>
    {error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Редактировать регистрацию</button>}</Notice>}<button className="primary sticky-action" disabled={busy || !chosen} onClick={review}>Проверить сбор</button>
  </Page>;
}

function GatheringDetails({ community, id, onBack, onCancelled, editRegistration }: { community: Community; id: string; onBack: () => void; onCancelled: () => void; editRegistration: () => void }) {
  const state = useAsync(() => api<GatheringDetail>(`/gatherings/${id}?community=${encodeURIComponent(community.key)}`), [community.key, id]);
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [attendanceRequired, setAttendanceRequired] = useState(false); const [editing, setEditing] = useState(false); const [cancelling, setCancelling] = useState(false); const [cancellationReason, setCancellationReason] = useState("");
  async function action(path: string, reason?: string) { setBusy(true); setError(undefined); setAttendanceRequired(false); try { await api(`/gatherings/${id}/${path}`, json("POST", { communityKey: community.key, reason })); telegram.success(({ join: "Вы записались на сбор", leave: "Вы вышли из сбора", close: "Запись закрыта", reopen: "Запись открыта", cancel: "Сбор отменён", "publication/retry": "Объявление опубликовано" } as Record<string, string>)[path] ?? "Изменения сохранены"); setCancelling(false); if (path === "cancel") onCancelled(); else state.reload(); } catch (e) { setAttendanceRequired(e instanceof ApiError && e.code === "camp_attendance_date_required"); setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  if (state.loading) return <Page title="Сбор"><Loading /></Page>; if (state.error || !state.data) return <Page title="Сбор" actions={<button onClick={onBack}>Назад</button>}><ErrorState message={state.error ?? "Сбор не найден"} /></Page>;
  const value = state.data;
  const canChangeLifecycle = value.canManage && !value.hasStarted && value.status !== "Completed" && value.status !== "Cancelled";
  if (editing && value.canManage) return <EditGathering community={community} id={id} value={value} done={() => { setEditing(false); state.reload(); }} cancel={() => setEditing(false)} />;
  return <Page title={value.gathering.gameName} subtitle={value.gathering.localDateTime} actions={<button onClick={onBack}>Назад</button>}>
    <Card><div className="media"><Cover src={value.gathering.imageUrl} name={value.gathering.gameName} /><div><p>{value.gathering.rulesText}</p>{value.gathering.description && <p className="gathering-note">«{value.gathering.description}»</p>}<Badge tone="accent">{value.gathering.statusText}</Badge></div></div>{value.waitlistPosition && <Notice kind="warning">Вы в листе ожидания, позиция {value.waitlistPosition}.</Notice>}</Card>
    {value.publicationStatus === "Failed" && <Notice kind="danger"><p>Не удалось опубликовать объявление в Telegram. Сам сбор сохранён.</p>{value.canRetryPublication && <button disabled={busy} onClick={() => action("publication/retry")}>Повторить публикацию</button>}</Notice>}
    <Card><h2>Организатор</h2>{value.confirmedParticipants.filter(p => p.isOrganizer).map((p, i) => <p key={`${p.name}-${i}`}><ContactLink url={p.contactUrl}>{p.name}</ContactLink></p>)}<h2>Участники</h2>{value.confirmedParticipants.some(p => !p.isOrganizer) ? <ul>{value.confirmedParticipants.filter(p => !p.isOrganizer).map((p, i) => <li key={`${p.name}-${i}`}><ContactLink url={p.contactUrl}>{p.name}</ContactLink></li>)}</ul> : <p className="muted">Пока никто не присоединился.</p>}{value.waitlistedParticipants.length > 0 && <><h2>Лист ожидания</h2><ol>{value.waitlistedParticipants.map(p => <li key={`${p.position}-${p.name}`}><ContactLink url={p.contactUrl}>{p.name}</ContactLink></li>)}</ol></>}</Card>
    {cancelling && <Card className="form-grid"><h2>Отмена сбора</h2><Field label="Причина" hint="Необязательно; участники увидят её в уведомлении"><textarea maxLength={300} value={cancellationReason} onChange={event => setCancellationReason(event.target.value)} /></Field><div className="row"><button onClick={() => setCancelling(false)}>Оставить сбор</button><button className="danger" disabled={busy} onClick={async () => { if (await telegram.confirm("Отменить сбор? Возобновить его будет нельзя.")) await action("cancel", cancellationReason.trim() || undefined); }}>{busy ? "Отменяем…" : "Подтвердить отмену"}</button></div></Card>}
    {value.hasStarted && <Notice>Время сбора наступило. Запись закрыта автоматически; изменить время или открыть запись снова нельзя.</Notice>}{error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Редактировать регистрацию</button>}</Notice>}<div className="action-bar">{value.canJoin && <button className="primary" disabled={busy} onClick={() => action("join")}>Присоединиться</button>}{value.canLeave && <button className="danger" disabled={busy} onClick={() => action("leave")}>Отказаться от места</button>}{value.canEdit && <button className="primary" onClick={() => setEditing(true)}>Изменить сбор</button>}{canChangeLifecycle && <>{value.status !== "Closed" && <button disabled={busy} onClick={() => action("close")}>Закрыть запись</button>}{value.status === "Closed" && <button onClick={() => action("reopen")}>Открыть запись</button>}<button className="danger" onClick={() => setCancelling(true)}>Отменить сбор</button></>}</div>
  </Page>;
}

function EditGathering({ community, id, value, done, cancel }: { community: Community; id: string; value: GatheringDetail; done: () => void; cancel: () => void }) {
  const [starts, setStarts] = useState(value.startsAtLocal); const [minimum, setMinimum] = useState(value.minimumPlayers); const [desired, setDesired] = useState(value.desiredPlayers); const [maximum, setMaximum] = useState(value.maximumPlayers); const [description, setDescription] = useState(value.description ?? ""); const [teach, setTeach] = useState(value.canTeachRules); const [selected, setSelected] = useState(value.selectedExpansionIds); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const gameMinimum = value.gameMinimumPlayers ?? 1;
  const gameMaximum = Math.max(gameMinimum, value.gameMaximumPlayers ?? 12);
  const playerOptions = Array.from({ length: gameMaximum - gameMinimum + 1 }, (_, index) => gameMinimum + index);
  const minimumStart = currentLocalMinute();
  async function save() { if (!isFutureLocal(starts)) { setError("Выберите дату и время в будущем."); return; } setBusy(true); setError(undefined); try { await api(`/gatherings/${id}`, json("PUT", { communityKey: community.key, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach, selectedExpansionIds: selected })); telegram.success("Сбор обновлён"); done(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  return <Page title="Изменить сбор" actions={<button onClick={cancel}>Отмена</button>}><Card className="form-grid"><Field label="Дата и время" hint="Прошедшее время выбрать нельзя"><input type="datetime-local" min={minimumStart} value={starts} onChange={e => setStarts(e.target.value)} /></Field><div className="limits"><Field label="Минимум"><select value={minimum} onChange={e => { const next = +e.target.value; setMinimum(next); if (desired < next) setDesired(next); }}>{playerOptions.filter(option => option <= desired).map(option => <option key={option}>{option}</option>)}</select></Field><Field label="Оптимально"><select value={desired} onChange={e => setDesired(+e.target.value)}>{playerOptions.filter(option => option >= minimum && option <= maximum).map(option => <option key={option}>{option}</option>)}</select></Field><Field label="Максимум"><select value={maximum} onChange={e => { const next = +e.target.value; setMaximum(next); if (desired > next) setDesired(next); }}>{playerOptions.filter(option => option >= desired).map(option => <option key={option}>{option}</option>)}</select></Field></div>{(!value.gameMinimumPlayers || !value.gameMaximumPlayers) && <Notice kind="warning">В BGG не указан полный диапазон игроков, поэтому мы поставили 1–12.</Notice>}<Field label="Описание"><textarea maxLength={300} value={description} onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label>{value.knownExpansions.length > 0 && <fieldset><legend>Дополнения</legend>{value.knownExpansions.map(exp => <label className="check" key={exp.bggId}><input type="checkbox" checked={selected.includes(exp.bggId)} onChange={() => setSelected(current => current.includes(exp.bggId) ? current.filter(x => x !== exp.bggId) : [...current, exp.bggId])} />{exp.name}</label>)}</fieldset>}{error && <Notice kind="danger">{error}</Notice>}<button className="primary" disabled={busy} onClick={save}>{busy ? "Сохраняем…" : "Сохранить"}</button></Card></Page>;
}
