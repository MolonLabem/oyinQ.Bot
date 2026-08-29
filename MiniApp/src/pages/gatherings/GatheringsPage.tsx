import { useEffect, useMemo, useState } from "react";
import { api, json } from "../../api/client";
import type { ClubGame, Community, GatheringDetail, GatheringListItem } from "../../api/types";
import { Badge, Card, Cover, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";

export function GatheringsPage({ community, bggAvailable, initialGatheringId, onInitialConsumed }: { community: Community; bggAvailable: boolean; initialGatheringId?: string; onInitialConsumed: () => void }) {
  const [screen, setScreen] = useState<"list" | "create" | "detail">(initialGatheringId ? "detail" : "list");
  const [selected, setSelected] = useState<string | undefined>(initialGatheringId);
  useEffect(() => telegram.back(screen !== "list", () => { setScreen("list"); setSelected(undefined); }), [screen]);
  useEffect(() => { if (initialGatheringId) onInitialConsumed(); }, []);
  if (screen === "create") return <CreateGathering community={community} bggAvailable={bggAvailable} onDone={() => setScreen("list")} />;
  if (screen === "detail" && selected) return <GatheringDetails community={community} id={selected} onBack={() => setScreen("list")} />;
  return <GatheringList community={community} open={id => { setSelected(id); setScreen("detail"); }} create={() => setScreen("create")} />;
}

function GatheringList({ community, open, create }: { community: Community; open: (id: string) => void; create: () => void }) {
  const state = useAsync(() => api<GatheringListItem[]>(`/gatherings?community=${encodeURIComponent(community.key)}`), [community.key]);
  return <Page title="Сборы" subtitle={community.name} actions={<button className="primary" onClick={create}>Создать сбор</button>}>
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.length ? <Empty>Пока нет сборов. Создайте первый.</Empty> :
      <div className="stack">{state.data.map(item => <button className="card gathering-card" key={item.card.publicId} onClick={() => open(item.card.publicId)}>
        <Cover src={item.card.imageUrl} name={item.card.gameName} /><div><div className="row"><h2>{item.card.gameName}</h2>{item.isOrganizer && <Badge tone="accent">Вы организатор</Badge>}</div><p>{item.card.localDateTime}</p><p>{item.card.confirmedPlayers} / {item.card.maximumPlayers} игроков</p><span className="muted">{item.card.statusText}</span></div>
      </button>)}</div>}
  </Page>;
}

function CreateGathering({ community, bggAvailable, onDone }: { community: Community; bggAvailable: boolean; onDone: () => void }) {
  const games = useAsync(() => api<ClubGame[]>(`/games?community=${encodeURIComponent(community.key)}`), [community.key]);
  const [source, setSource] = useState<"catalog" | "bgg">("catalog"); const [query, setQuery] = useState(""); const [chosen, setChosen] = useState<ClubGame>();
  const [bggInput, setBggInput] = useState(""); const [expansions, setExpansions] = useState<number[]>([]); const [starts, setStarts] = useState("");
  const [minimum, setMinimum] = useState(2); const [desired, setDesired] = useState(4); const [maximum, setMaximum] = useState(4);
  const [description, setDescription] = useState(""); const [teach, setTeach] = useState(true); const [reviewing, setReviewing] = useState(false); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const filtered = useMemo(() => games.data?.filter(g => g.name.toLowerCase().includes(query.toLowerCase())) ?? [], [games.data, query]);
  async function previewBgg() { setError(undefined); try { const value = await api<{ game: ClubGame; expansions: { bggId: number; name: string }[] }>(`/bgg/game?input=${encodeURIComponent(bggInput)}`); setChosen({ ...value.game, expansions: value.expansions }); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } }
  async function submit() {
    if (!chosen || !starts) { setError("Выберите игру, дату и время."); return; }
    setBusy(true); setError(undefined);
    try { await api("/gatherings", json("POST", { communityKey: community.key, gameSource: source, bggId: chosen.bggId, selectedExpansionIds: expansions, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach })); telegram.success("Сбор создан"); onDone(); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); }
  }
  function review() { if (!chosen || !starts) { setError("Выберите игру, дату и время."); return; } if (minimum < 1 || minimum > desired || desired > maximum) { setError("Проверьте лимиты игроков: минимум ≤ желаемое число ≤ максимум."); return; } setError(undefined); setReviewing(true); }
  if (reviewing && chosen) {
    const selectedNames = chosen.expansions.filter(expansion => expansions.includes(expansion.bggId)).map(expansion => expansion.name);
    return <Page title="Проверьте сбор" actions={<button onClick={() => setReviewing(false)}>Изменить</button>}><Card><div className="media"><Cover src={chosen.thumbnailImageUrl} name={chosen.name} /><div><h2>{chosen.name}</h2><p>{new Date(starts).toLocaleString("ru-RU", { dateStyle: "long", timeStyle: "short" })}</p></div></div><dl className="review-list"><dt>Игроки</dt><dd>{minimum} / {desired} / {maximum}</dd><dt>Дополнения</dt><dd>{selectedNames.length ? selectedNames.join(", ") : "Без дополнений"}</dd><dt>Правила</dt><dd>{teach ? "Объясню" : "Нужен опыт"}</dd><dt>Описание</dt><dd>{description.trim() || "Без описания"}</dd></dl></Card>{error && <Notice kind="danger">{error}</Notice>}<button className="primary sticky-action" disabled={busy} onClick={submit}>{busy ? "Создаём…" : "Подтвердить и создать"}</button></Page>;
  }
  return <Page title="Новый сбор" actions={<button onClick={onDone}>Назад</button>}>
    <div className="segmented"><button className={source === "catalog" ? "active" : ""} onClick={() => { setSource("catalog"); setChosen(undefined); }}>Из каталога</button><button disabled={!bggAvailable} className={source === "bgg" ? "active" : ""} onClick={() => { setSource("bgg"); setChosen(undefined); }}>По ссылке BGG</button></div>
    {!bggAvailable && <Notice kind="warning">BGG временно недоступен. Создать сбор по игре из каталога по-прежнему можно.</Notice>}
    {source === "catalog" ? <><Field label="Найти игру"><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Название игры" /></Field>{games.loading ? <Loading /> : <div className="game-grid">{filtered.map(game => <button className={`game-option ${chosen?.bggId === game.bggId ? "selected" : ""}`} onClick={() => { setChosen(game); setExpansions([]); }} key={game.bggId}><Cover src={game.thumbnailImageUrl} name={game.name} /><span>{game.name}</span></button>)}</div>}</> :
      <Card><Field label="Ссылка на BoardGameGeek"><input value={bggInput} onChange={e => setBggInput(e.target.value)} placeholder="https://boardgamegeek.com/boardgame/…" /></Field><button onClick={previewBgg}>Показать игру</button></Card>}
    {chosen && <Card><div className="media"><Cover src={chosen.thumbnailImageUrl} name={chosen.name} /><div><h2>{chosen.name}</h2><p>{chosen.minPlayers ?? "?"}–{chosen.maxPlayers ?? "?"} игроков</p></div></div>{chosen.expansions.length > 0 && <fieldset><legend>Дополнения</legend>{chosen.expansions.map(exp => <label className="check" key={exp.bggId}><input type="checkbox" checked={expansions.includes(exp.bggId)} onChange={() => setExpansions(current => current.includes(exp.bggId) ? current.filter(id => id !== exp.bggId) : [...current, exp.bggId])} />{exp.name}</label>)}</fieldset>}</Card>}
    <Card className="form-grid"><Field label="Дата и время"><input type="datetime-local" value={starts} onChange={e => setStarts(e.target.value)} /></Field><div className="limits"><Field label="Минимум"><input type="number" min="1" value={minimum} onChange={e => setMinimum(+e.target.value)} /></Field><Field label="Желаемо"><input type="number" min="1" value={desired} onChange={e => setDesired(+e.target.value)} /></Field><Field label="Максимум"><input type="number" min="1" value={maximum} onChange={e => setMaximum(+e.target.value)} /></Field></div><Field label="Описание"><textarea value={description} maxLength={300} onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label></Card>
    {error && <Notice kind="danger">{error}</Notice>}<button className="primary sticky-action" disabled={busy || !chosen} onClick={review}>Проверить сбор</button>
  </Page>;
}

function GatheringDetails({ community, id, onBack }: { community: Community; id: string; onBack: () => void }) {
  const state = useAsync(() => api<GatheringDetail>(`/gatherings/${id}?community=${encodeURIComponent(community.key)}`), [community.key, id]);
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [editing, setEditing] = useState(false); const [cancelling, setCancelling] = useState(false); const [cancellationReason, setCancellationReason] = useState("");
  async function action(path: string, reason?: string) { setBusy(true); setError(undefined); try { await api(`/gatherings/${id}/${path}`, json("POST", { communityKey: community.key, reason })); telegram.success(({ join: "Вы присоединились", leave: "Вы покинули сбор", close: "Запись закрыта", reopen: "Запись открыта", cancel: "Сбор отменён", "publication/retry": "Объявление опубликовано" } as Record<string, string>)[path] ?? "Изменения сохранены"); setCancelling(false); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  if (state.loading) return <Page title="Сбор"><Loading /></Page>; if (state.error || !state.data) return <Page title="Сбор" actions={<button onClick={onBack}>Назад</button>}><ErrorState message={state.error ?? "Сбор не найден"} /></Page>;
  const value = state.data;
  if (editing && value.canManage) return <EditGathering community={community} id={id} value={value} done={() => { setEditing(false); state.reload(); }} cancel={() => setEditing(false)} />;
  return <Page title={value.gathering.gameName} subtitle={value.gathering.localDateTime} actions={<button onClick={onBack}>Назад</button>}>
    <Card><Cover src={value.gathering.imageUrl} name={value.gathering.gameName} /><p>{value.gathering.description}</p><p>{value.gathering.rulesText}</p><Badge tone="accent">{value.gathering.statusText}</Badge>{value.waitlistPosition && <Notice kind="warning">Вы в листе ожидания, позиция {value.waitlistPosition}.</Notice>}</Card>
    {value.publicationStatus === "Failed" && <Notice kind="danger">Объявление не опубликовано: {value.publicationError}{value.canRetryPublication && <button disabled={busy} onClick={() => action("publication/retry")}>Повторить</button>}</Notice>}
    <Card><h2>Участники</h2><ul>{value.confirmedParticipants.map((p, i) => <li key={`${p.name}-${i}`}>{p.name}{p.isOrganizer ? " — организатор" : ""}</li>)}</ul>{value.waitlistedParticipants.length > 0 && <><h3>Лист ожидания</h3><ol>{value.waitlistedParticipants.map(p => <li key={`${p.position}-${p.name}`}>{p.name}</li>)}</ol></>}</Card>
    {cancelling && <Card className="form-grid"><h2>Отмена сбора</h2><Field label="Причина" hint="Необязательно; участники увидят её в уведомлении"><textarea maxLength={300} value={cancellationReason} onChange={event => setCancellationReason(event.target.value)} /></Field><div className="row"><button onClick={() => setCancelling(false)}>Оставить сбор</button><button className="danger" disabled={busy} onClick={async () => { if (await telegram.confirm("Отменить сбор? Возобновить его будет нельзя.")) await action("cancel", cancellationReason.trim() || undefined); }}>{busy ? "Отменяем…" : "Подтвердить отмену"}</button></div></Card>}
    {error && <Notice kind="danger">{error}</Notice>}<div className="action-bar">{value.canJoin && <button className="primary" disabled={busy} onClick={() => action("join")}>Присоединиться</button>}{value.canLeave && <button className="danger" disabled={busy} onClick={() => action("leave")}>Отказаться от места</button>}{value.canManage && <><button className="primary" onClick={() => setEditing(true)}>Изменить сбор</button>{value.status !== "Closed" && <button disabled={busy} onClick={() => action("close")}>Закрыть запись</button>}{value.status === "Closed" && <button onClick={() => action("reopen")}>Открыть запись</button>}<button className="danger" onClick={() => setCancelling(true)}>Отменить сбор</button></>}</div>
  </Page>;
}

function EditGathering({ community, id, value, done, cancel }: { community: Community; id: string; value: GatheringDetail; done: () => void; cancel: () => void }) {
  const [starts, setStarts] = useState(value.startsAtLocal); const [minimum, setMinimum] = useState(value.minimumPlayers); const [desired, setDesired] = useState(value.desiredPlayers); const [maximum, setMaximum] = useState(value.maximumPlayers); const [description, setDescription] = useState(value.description ?? ""); const [teach, setTeach] = useState(value.canTeachRules); const [selected, setSelected] = useState(value.selectedExpansionIds); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  async function save() { setBusy(true); setError(undefined); try { await api(`/gatherings/${id}`, json("PUT", { communityKey: community.key, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach, selectedExpansionIds: selected })); telegram.success("Сбор обновлён"); done(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  return <Page title="Изменить сбор" actions={<button onClick={cancel}>Отмена</button>}><Card className="form-grid"><Field label="Дата и время"><input type="datetime-local" value={starts} onChange={e => setStarts(e.target.value)} /></Field><div className="limits"><Field label="Минимум"><input type="number" min="1" value={minimum} onChange={e => setMinimum(+e.target.value)} /></Field><Field label="Желаемо"><input type="number" min="1" value={desired} onChange={e => setDesired(+e.target.value)} /></Field><Field label="Максимум"><input type="number" min="1" value={maximum} onChange={e => setMaximum(+e.target.value)} /></Field></div><Field label="Описание"><textarea maxLength={300} value={description} onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label>{value.knownExpansions.length > 0 && <fieldset><legend>Дополнения</legend>{value.knownExpansions.map(exp => <label className="check" key={exp.bggId}><input type="checkbox" checked={selected.includes(exp.bggId)} onChange={() => setSelected(current => current.includes(exp.bggId) ? current.filter(x => x !== exp.bggId) : [...current, exp.bggId])} />{exp.name}</label>)}</fieldset>}{error && <Notice kind="danger">{error}</Notice>}<button className="primary" disabled={busy} onClick={save}>{busy ? "Сохраняем…" : "Сохранить"}</button></Card></Page>;
}
