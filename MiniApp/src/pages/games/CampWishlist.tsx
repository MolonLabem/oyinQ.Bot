import { useEffect, useRef, useState } from "react";
import { api, json } from "../../api/client";
import type { ClubGame, Community } from "../../api/types";
import { BackButton, Card, Cover, Empty, ErrorState, Field, Loading, Notice, Tabs } from "../../components/Ui";
import { WishButton } from "../../components/Wishlist";
import { GamePicker } from "../../components/GamePicker";
import { ComplexityBadge } from "../../components/ComplexityBadge";
import { useAsync, useDebouncedValue } from "../../hooks/useAsync";
import { useMobileDialog } from "../../hooks/useMobileDialog";
import { successEventName, telegram } from "../../telegram/webApp";
import { GameGatherings } from "./GameGatherings";

type Person = { id: string; name: string; dates: string[] };
type Owner = { person: Person; status: string; dates: string[]; requestState: string; isMe: boolean };
export type WishGame = { game: ClubGame; interestedParticipants: number; otherInterested: number; isWished: boolean; isOwned: boolean; confirmed: boolean; boxSummary: string; scheduledGatherings: number; myStatus?: string };
type ListResult = { items: WishGame[]; total: number; suggestions: number; hasMore: boolean; canAct: boolean; shareCollection: boolean; shareWishes: boolean; myDates: string[]; updatedAt: string };
type Outgoing = { bggId: number; name: string; owner: Person; dates: string[]; state: string; canCancel: boolean };
type Detail = { item: WishGame; interested: Person[]; anonymousCount: number; owners: Owner[]; canAct: boolean; myDates: string[]; requests: Outgoing[]; askedMe?: Person[] };
type Frame = { type: "game" | "profile" | "participants"; id?: string; scroll: number; section?: string };
type Props = { personal?: boolean; community: Community; bggAvailable: boolean; initialGameId?: number; initialPersonId?: string; back?: () => void; create?: (id: number) => void; openGathering?: (id: string) => void };
type Selection = { mode: string; search: string; needsBox: boolean; withoutGathering: boolean; owned: boolean; sort: string; page: number; showDeclined: boolean };
const defaults: Selection = { mode: "all", search: "", needsBox: false, withoutGathering: false, owned: false, sort: "demand", page: 1, showDeclined: false };
function restore(key: string): Selection { try { return { ...defaults, ...JSON.parse(sessionStorage.getItem(key) ?? "{}") }; } catch { return defaults; } }
const days = (dates: string[]) => dates.map(d => new Date(`${d}T12:00:00Z`).toLocaleDateString("ru-RU", { day: "numeric", month: "short", timeZone: "UTC" })).join(", ");
const endpoint = (key: string, params = "") => `/camp-wishlist?community=${encodeURIComponent(key)}${params}`;
export function useRefresh(reload: () => void) {
  useEffect(() => {
    const refresh = () => { if (document.visibilityState !== "hidden") reload(); };
    window.addEventListener(successEventName, refresh); window.addEventListener("focus", refresh); document.addEventListener("visibilitychange", refresh);
    return () => { window.removeEventListener(successEventName, refresh); window.removeEventListener("focus", refresh); document.removeEventListener("visibilitychange", refresh); };
  }, [reload]);
}
export function useAction(key: string) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  async function act(body: object) {
    if (busy) return false; setBusy(true); setError(undefined);
    try { await api(endpoint(key), json("POST", body)); if (alive.current) telegram.success("Сохранено"); return true; }
    catch (e) { if (alive.current) setError(e instanceof Error ? e.message : String(e)); return false; }
    finally { if (alive.current) setBusy(false); }
  }
  return { act, busy, error };
}

export function CampWishlist(props: Props) { return <CampWishBrowser {...props} />; }
export function CampPersonalWishes(props: Props) { return <CampWishBrowser {...props} personal />; }
function CampWishBrowser(props: Props) {
  const key = `oyinq-camp-wishlist:${props.community.key}${props.personal ? ":personal" : ""}`;
  const [selection, setSelection] = useState(() => ({ ...restore(key), ...(props.personal ? { mode: "mine" } : {}) }));
  const [frames, setFrames] = useState<Frame[]>(() => props.initialPersonId !== undefined ? [{ type: "profile", id: props.initialPersonId, scroll: 0 }] : props.initialGameId ? [{ type: "game", id: String(props.initialGameId), scroll: 0 }] : []);
  const [adding, setAdding] = useState(false);
  const rootScroll = useRef(0); const restoreScroll = useRef<number | undefined>(undefined);
  const search = useDebouncedValue(selection.search, 300);
  const params = new URLSearchParams(Object.entries({ ...selection, search }).map(([k, v]) => [k, String(v)])).toString();
  const state = useAsync(async () => ({ params, communityKey: props.community.key, result: await api<ListResult>(endpoint(props.community.key, `&${params}`)) }), [props.community.key, params]);
  const result = state.data?.params === params && state.data.communityKey === props.community.key ? state.data.result : undefined;
  useRefresh(state.reload);
  useEffect(() => { try { sessionStorage.setItem(key, JSON.stringify(selection)); } catch { /* Optional storage. */ } }, [selection, key]);
  useEffect(() => { if (restoreScroll.current !== undefined) { window.scrollTo(0, restoreScroll.current); restoreScroll.current = undefined; } }, [frames]);
  function push(type: Frame["type"], id?: string, section?: string) { if (!frames.length) rootScroll.current = window.scrollY; restoreScroll.current = 0; setFrames(current => [...current.map((f, i) => i === current.length - 1 ? { ...f, scroll: window.scrollY } : f), { type, id, scroll: 0, section }]); }
  function back() { if (frames.length === 1 && (props.initialGameId || props.initialPersonId !== undefined) && props.back) { props.back(); return; } restoreScroll.current = frames.length > 1 ? frames[frames.length - 2].scroll : rootScroll.current; setFrames(frames.slice(0, -1)); state.reload(); window.dispatchEvent(new Event(successEventName)); }
  useEffect(() => telegram.back(frames.length > 0, back), [frames]);
  const change = (patch: Partial<Selection>) => setSelection(s => ({ ...s, ...patch, page: patch.page ?? 1 }));
  const openGame = (id: number, section?: string) => push("game", String(id), section);
  return <section className="camp-wishlist">
    <div hidden={frames.length > 0}>
      <p>Отмечайте, во что хотите сыграть. Владельцы смогут предложить коробку, а вы — собрать партию.</p>
      <p className="muted">Хотелки только для кэмпа «{props.community.name}».</p>
      {!props.personal && <Tabs label="Хотелки кэмпа" active={selection.mode} onChange={mode => change({ mode })} items={[{ id: "all", label: "Все" }, { id: "mine", label: "Мои" }, { id: "bring", label: "Что привезти" }]} />}
      {!props.personal && !!result?.suggestions && selection.mode !== "bring" && <Notice>У вас есть игры из хотелок участников: {result.suggestions}. <button onClick={() => change({ mode: "bring" })}>Посмотреть, что привезти</button></Notice>}
      <div className="row"><button className="primary" disabled={!result?.canAct} onClick={() => setAdding(true)}>Добавить игру</button>{!props.personal && <button onClick={() => push("participants")}>Участники кэмпа</button>}</div>
      {selection.mode === "bring" && <Incoming communityKey={props.community.key} open={openGame} openProfile={id => push("profile", id)} />}
      {selection.mode === "mine" && <OutgoingRequests communityKey={props.community.key} open={openGame} openProfile={id => push("profile", id)} canAct={result?.canAct ?? false} />}
      <Field label="Поиск игры"><input type="search" value={selection.search} onChange={e => change({ search: e.target.value })} placeholder="Название или оригинальное название" /></Field>
      <div className="wish-filters">{([["needsBox", "Нужна коробка"], ["withoutGathering", "Без сбора"], ["owned", "Есть у меня"]] as const).map(([field, label]) => <button key={field} aria-pressed={selection[field]} className={selection[field] ? "filter-chip active" : "filter-chip"} onClick={() => change({ [field]: !selection[field] })}>{label}{selection[field] ? " ✓" : ""}</button>)}</div>
      <div className="row"><Field label="Порядок"><select value={selection.sort} onChange={e => change({ sort: e.target.value })}><option value="demand">По числу желающих</option><option value="name">По названию</option></select></Field><button className="wish-refresh" disabled={state.loading} onClick={state.reload}><span aria-hidden>↻ </span>{state.loading ? "Обновляем…" : "Обновить список"}</button></div>
      {selection.mode === "bring" && <label className="check"><input type="checkbox" checked={selection.showDeclined} onChange={e => change({ showDeclined: e.target.checked })} />Показать игры, которые решил не везти</label>}
      <p className="muted" role="status">{result ? `Найдено игр: ${result.total}. Обновлено ${new Date(result.updatedAt).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" })}` : "Загружаем список…"}</p>
      {(selection.search || selection.needsBox || selection.withoutGathering || selection.owned) && <button onClick={() => change({ search: "", needsBox: false, withoutGathering: false, owned: false })}>Сбросить поиск и фильтры</button>}
      {!result && state.loading && <Loading />}
      {state.error && <ErrorState message={state.error} retry={state.reload} />}
      {result && <>{!result.items.length ? <Empty>Таких игр пока нет. Добавьте хотелку или измените фильтры.</Empty> : <div className="stack">{result.items.map(item => <WishCard key={item.game.bggId} item={item} open={section => openGame(item.game.bggId, section)} />)}</div>}
        <Paging page={selection.page} hasMore={result.hasMore} change={page => change({ page })} />
        {!props.personal && selection.mode === "mine" && <p className="muted">{result.shareWishes ? "Ваши хотелки видны с именем." : "Ваши хотелки учитываются без имени."} <a data-profile-nav href={`?community=${encodeURIComponent(props.community.key)}&tab=profile&profileTab=wishes#camp-privacy`}>Изменить</a></p>}
      </>}
    </div>
    {frames.map((frame, i) => <div key={`${i}:${frame.type}:${frame.id}`} hidden={i !== frames.length - 1}><BackButton onClick={back} />
      {frame.type === "game" ? <WishDetail {...props} id={Number(frame.id)} section={frame.section} openProfile={id => push("profile", id)} /> : frame.type === "profile" ? <><CampPerson communityKey={props.community.key} person={frame.id!} open={openGame} /></> : <CampPeople communityKey={props.community.key} open={id => push("profile", id)} />}
    </div>)}
    {adding && <AddWish {...props} close={() => { setAdding(false); state.reload(); }} />}
  </section>;
}

function Paging({ page, hasMore, change }: { page: number; hasMore: boolean; change: (page: number) => void }) { return page > 1 || hasMore ? <div className="row"><button disabled={page <= 1} onClick={() => change(page - 1)}>Назад</button><span>Страница {page}</span><button disabled={!hasMore} onClick={() => change(page + 1)}>Дальше</button></div> : null; }
function WishCard({ item, open }: { item: WishGame; open: (section?: string) => void }) {
  return <Card className="wish-card"><button className="wish-game-heading" onClick={() => open()}><Cover name={item.game.name} src={item.game.thumbnailImageUrl} /><span><strong>{item.game.name}</strong><ComplexityBadge info={item.game.complexityInfo} />{item.isOwned && <small>Есть у вас</small>}</span></button>
    <p>Хотят сыграть: {item.interestedParticipants}{item.isWished ? " · Вы тоже" : ""}</p><p>{item.boxSummary}</p><p>{item.scheduledGatherings ? `Предстоящих сборов: ${item.scheduledGatherings}` : "Сборов пока нет"}</p>
    {item.myStatus === "declined" && <p>Вы решили не везти эту игру. Решение можно изменить.</p>}
    <button onClick={() => open(item.isOwned ? "box" : item.scheduledGatherings ? "gatherings" : item.confirmed ? "create" : "owners")}>{item.isOwned ? "Предложить или подтвердить коробку" : item.scheduledGatherings ? "Посмотреть сборы" : item.confirmed ? "Организовать сбор" : "Найти коробку"}</button>
    <button className="ghost" onClick={() => open("interested")}>Кто хочет сыграть · Все {item.interestedParticipants}</button>
  </Card>;
}

function AddWish({ community, bggAvailable, close }: Props & { close: () => void }) {
  const dialog = useMobileDialog(close); const games = useAsync(() => api<ClubGame[]>(`/games?community=${encodeURIComponent(community.key)}`), [community.key]); const [selected, setSelected] = useState<ClubGame>();
  return <dialog ref={dialog} className="catalog-filter-dialog" onCancel={e => { e.preventDefault(); close(); }}><div className="stack"><h2>Добавить игру в хотелки</h2><p>Для кэмпа «{community.name}»</p><GamePicker selectionMode="wish" catalog={games.data} catalogLoading={games.loading} catalogError={games.error} bggAvailable={bggAvailable} selected={selected} onSelect={setSelected} onClear={() => setSelected(undefined)} label="Выберите игру" />{selected && <WishButton communityKey={community.key} bggId={selected.bggId} changed={close} />}<button onClick={close}>Закрыть</button></div></dialog>;
}

function WishDetail({ community, id, create, openGathering, openProfile, section }: Props & { id: number; openProfile: (id: string) => void; section?: string }) {
  const state = useAsync(() => api<Detail>(endpoint(community.key, `&game=${id}`)), [community.key, id]); useRefresh(state.reload);
  const [limit, setLimit] = useState(12); const [ownerLimit, setOwnerLimit] = useState(12);
  const container = useRef<HTMLDivElement>(null); const jumped = useRef(false);
  useEffect(() => {
    if (!state.data || jumped.current || !section) return;
    jumped.current = true;
    container.current?.querySelector(`[data-wish-section="${section}"]`)?.scrollIntoView({ block: "start" });
  }, [state.data, section]);
  if (!state.data) return state.error ? <ErrorState message={state.error} retry={state.reload} /> : <Loading />;
  const data = state.data; const item = data.item;
  return <div className="stack" ref={container}><h2>{item.game.name}</h2><div className="game-detail-hero"><Cover name={item.game.name} src={item.game.imageUrl ?? item.game.thumbnailImageUrl} /><div><p>{item.boxSummary}</p><p>{community.name}</p></div></div>
    {state.error && <ErrorState message={state.error} retry={state.reload} />}
    {data.canAct && <WishButton communityKey={community.key} bggId={id} initial={item.isWished} changed={state.reload} />}
    {!!data.askedMe?.length && <section><h3>Вас попросили привезти · {data.askedMe.length}</h3><PeopleRequests people={data.askedMe} open={openProfile} /></section>}
    {!item.isOwned && !!data.askedMe?.length && <Notice>Игры больше нет в вашей коллекции. Отказ доступен во входящих просьбах профиля; для подтверждения сначала добавьте игру.</Notice>}
    {(item.isOwned || !!data.askedMe?.length) && <section data-wish-section="box"><a data-profile-nav className="button primary-link" href={`?community=${encodeURIComponent(community.key)}&tab=profile&profileTab=collection&box=${id}`}>Управлять коробкой в профиле</a></section>}
    {!data.canAct && <Notice>Кэмп завершён или закрыт для изменений.</Notice>}
    {data.requests?.length > 0 && <section><h3>Ваши просьбы</h3>{data.requests.map(r => <RequestResult key={r.owner.id} communityKey={community.key} request={r} canAct={data.canAct} changed={state.reload} openProfile={openProfile} />)}</section>}
    <section data-wish-section="interested"><h3>Кто хочет сыграть · {item.interestedParticipants}</h3>{data.interested.slice(0, limit).map(p => <button className="person-link" key={p.id} onClick={() => openProfile(p.id)}>{p.name}</button>)}{data.anonymousCount > 0 && <p className="muted">Ещё {data.anonymousCount} без показа имени.</p>}{data.interested.length > limit && <button onClick={() => setLimit(limit + 12)}>Показать ещё</button>}</section>
    <section data-wish-section="owners"><h3>У кого есть коробка / кто привезёт</h3>{!data.owners.length && <p>Коробку пока не нашли.</p>}{data.owners.slice(0, ownerLimit).map(owner => <OwnerRow key={owner.person.id} community={community} id={id} gameName={item.game.name} owner={owner} canAct={data.canAct} myDates={data.myDates} openProfile={openProfile} changed={state.reload} />)}{data.owners.length > ownerLimit && <button onClick={() => setOwnerLimit(ownerLimit + 12)}>Показать ещё</button>}</section>
    <div data-wish-section="gatherings">{openGathering && <GameGatherings preserveScroll communityKey={community.key} bggId={id} open={openGathering} />}</div>
    <div data-wish-section="create">{data.canAct && (create ? <button className={item.scheduledGatherings ? "" : "primary"} onClick={() => create(id)}>Организовать сбор</button> : <a className="button" href={`?community=${encodeURIComponent(community.key)}&tab=gatherings&createGame=${id}`}>Организовать сбор</a>)}</div>
    <p className="muted">Хотелка и просьба не записывают вас на сбор. Привоз коробки не обязывает организовывать партию или объяснять правила.</p>
    {item.game.description && <details><summary>Об игре</summary><p>{item.game.description}</p></details>}
  </div>;
}

function DateChoices({ dates, chosen, set }: { dates: string[]; chosen: string[]; set: (dates: string[]) => void }) { return <fieldset className="wish-dates"><legend>Дни</legend>{dates.map(date => <label key={date}><input type="checkbox" checked={chosen.includes(date)} onChange={e => set(e.target.checked ? [...chosen, date].sort() : chosen.filter(d => d !== date))} />{days([date])}</label>)}</fieldset>; }
function OwnerRow({ community, id, gameName, owner, canAct, myDates, openProfile, changed }: { community: Community; id: number; gameName: string; owner: Owner; canAct: boolean; myDates: string[]; openProfile: (id: string) => void; changed: () => void }) {
  const action = useAction(community.key); const [confirm, setConfirm] = useState(false); const common = myDates.filter(d => owner.person.dates.includes(d)); const [chosen, setChosen] = useState(common); const [sent, setSent] = useState(false);
  const already = owner.status === "Bringing" && chosen.length > 0 && chosen.every(d => owner.dates.includes(d));
  const text = owner.status === "Bringing" ? "Точно привезёт" : owner.status === "Available" ? "Может привезти — пока без подтверждения" : "Есть в коллекции — можно попросить";
  return <Card><button className="person-link" onClick={() => openProfile(owner.person.id)}>{owner.person.name}</button><p>{text} · {days(owner.dates)}</p>
    {!owner.isMe && <>{owner.requestState === "pending" ? <><p role="status">{sent ? "Просьба отправлена" : "Ожидаем ответа"}</p><button disabled={action.busy || !canAct} onClick={async () => { if (await action.act({ action: "cancel", bggId: id, owner: owner.person.id })) changed(); }}>Отменить свою просьбу</button></> : owner.requestState === "declined" ? <p>Владелец не сможет привезти эту игру.</p> : owner.requestState === "found" ? <p>{already ? "Уже привезёт" : "Коробка уже найдена"} · Посмотрите сборы по игре.</p> : !common.length ? <p>У вас нет общих дней участия.</p> : <button disabled={!canAct || action.busy} onClick={() => setConfirm(true)}>Попросить привезти</button>}
      {confirm && <div className="stack"><p>Попросить {owner.person.name} привезти «{gameName}» на «{community.name}»?</p><DateChoices dates={common} chosen={chosen} set={setChosen} /><p>{days(chosen)}</p>{already ? <Notice>Уже привезёт на выбранные дни.</Notice> : <button className="primary" disabled={action.busy || !chosen.length || !canAct} onClick={async () => { if (await action.act({ action: "request", bggId: id, owner: owner.person.id, dates: chosen })) { setSent(true); setConfirm(false); changed(); } }}>Отправить просьбу</button>}<button onClick={() => setConfirm(false)}>Отмена</button></div>}</>}
    {action.error && <Notice kind="danger">{action.error}</Notice>}
  </Card>;
}

export function Incoming({ communityKey, open, openProfile, allowDecline = false }: { allowDecline?: boolean; communityKey: string; open: (id: number) => void; openProfile: (id: string) => void }) {
  const state = useAsync(() => api<{ bggId: number; name: string; requesters: Person[]; state: string }[]>(endpoint(communityKey, "&view=incoming")), [communityKey]); useRefresh(state.reload);
  const [limit, setLimit] = useState(10);
  return <details className="content-section"><summary>Вас попросили привезти · {state.data?.length ?? 0}</summary>{state.error && <ErrorState message={state.error} retry={state.reload} />}{state.data?.slice(0, limit).map(r => <Card key={r.bggId}><strong>{r.name}</strong><p>Попросили: {r.requesters.length}</p><p>{r.state === "declined" ? "Вы ответили, что не сможете" : r.state === "found" ? "Коробка уже найдена" : r.state === "unavailable" ? "Нет общих дней участия" : "Ожидают вашего ответа"}</p><PeopleRequests people={r.requesters} open={openProfile} /><button onClick={() => open(r.bggId)}>Посмотреть и ответить</button>{allowDecline && r.state === "pending" && <DeclineRequest communityKey={communityKey} id={r.bggId} changed={state.reload} />}</Card>)}{(state.data?.length ?? 0) > limit && <button onClick={() => setLimit(limit + 10)}>Показать ещё</button>}</details>;
}

function PeopleRequests({ people, open }: { people: Person[]; open: (id: string) => void }) {
  const [limit, setLimit] = useState(10);
  return <details><summary>Кто попросил и на какие дни</summary>{people.slice(0, limit).map(p => <p key={p.id}><button className="person-link" onClick={() => open(p.id)}>{p.name}</button> · {days(p.dates)}</p>)}{people.length > limit && <button onClick={() => setLimit(limit + 10)}>Показать ещё</button>}</details>;
}
function RequestResult({ communityKey, request: r, canAct, changed, openProfile }: { communityKey: string; request: Outgoing; canAct: boolean; changed: () => void; openProfile: (id: string) => void }) {
  const action = useAction(communityKey);
  const labels: Record<string, string> = { pending: "Ожидаем ответа", declined: "Владелец не сможет привезти", found: "Коробка уже найдена", cancelled: "Вы отменили просьбу", unavailable: "Нет общих дней участия" };
  return <Card><button className="person-link" onClick={() => openProfile(r.owner.id)}>{r.owner.name}</button><p>{labels[r.state]} · {days(r.dates)}</p>{r.canCancel && <button disabled={!canAct || action.busy} onClick={async () => { if (await action.act({ action: "cancel", bggId: r.bggId, owner: r.owner.id })) changed(); }}>Отменить свою просьбу</button>}{action.error && <Notice kind="danger">{action.error}</Notice>}</Card>;
}
function OutgoingRequests({ communityKey, open, openProfile, canAct }: { communityKey: string; open: (id: number) => void; openProfile: (id: string) => void; canAct: boolean }) {
  const state = useAsync(() => api<Outgoing[]>(endpoint(communityKey, "&view=outgoing")), [communityKey]); useRefresh(state.reload); const [limit, setLimit] = useState(10);
  return <details className="content-section"><summary>Мои просьбы привезти · {state.data?.length ?? 0}</summary>{state.error && <ErrorState message={state.error} retry={state.reload} />}{state.data?.slice(0, limit).map(r => <div key={`${r.owner.id}:${r.bggId}`}><button onClick={() => open(r.bggId)}>{r.name}</button><RequestResult communityKey={communityKey} request={r} canAct={canAct} changed={state.reload} openProfile={openProfile} /></div>)}{(state.data?.length ?? 0) > limit && <button onClick={() => setLimit(limit + 10)}>Показать ещё</button>}</details>;
}

function CampPerson({ communityKey, person, open }: { communityKey: string; person: string; open: (id: number, section?: string) => void }) {
  const [tab, setTab] = useState("games"); const [search, setSearch] = useState(""); const [page, setPage] = useState(1); const query = useDebouncedValue(search, 300);
  const params = `&view=profile${person ? `&person=${encodeURIComponent(person)}` : ""}&mode=${tab}&search=${encodeURIComponent(query)}&page=${page}`;
  const state = useAsync(async () => ({ params, data: await api<{ person: Person; isMe: boolean; wishesVisible: boolean; items: WishGame[]; total: number; hasMore: boolean }>(endpoint(communityKey, params)) }), [communityKey, params]); useRefresh(state.reload); const data = state.data?.params === params ? state.data.data : undefined;
  return <div className="stack"><h2>{data?.person.name ?? "Участник кэмпа"}</h2>{data?.isMe && <a data-profile-nav href={`?community=${encodeURIComponent(communityKey)}&tab=profile&profileTab=collection`}>Управлять моими играми в профиле</a>}{data && <p>Дни участия: {days(data.person.dates)}</p>}<Tabs label="Профиль участника" active={tab} onChange={v => { setTab(v); setPage(1); }} items={[{ id: "games", label: "Игры" }, { id: "wishes", label: "Хотелки" }]} /><Field label="Поиск игры"><input type="search" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} /></Field>{state.error && <ErrorState message={state.error} retry={state.reload} />}{!data && state.loading && <Loading />}{data && <>{tab === "wishes" && !data.wishesVisible && <Notice>Участник пока не открыл свои хотелки для просмотра.</Notice>}<p>Найдено игр: {data.total}</p>{data.items.map(item => <WishCard key={item.game.bggId} item={item} open={section => open(item.game.bggId, section)} />)}{!data.items.length && <Empty>Нет игр для показа.</Empty>}<Paging page={page} hasMore={data.hasMore} change={setPage} /></>}</div>;
}
function CampPeople({ communityKey, open }: { communityKey: string; open: (id: string) => void }) {
  const [search, setSearch] = useState(""); const [page, setPage] = useState(1); const query = useDebouncedValue(search, 300);
  const params = `&view=participants&search=${encodeURIComponent(query)}&page=${page}`;
  const state = useAsync(async () => ({ params, data: await api<{ items: Person[]; total: number; hasMore: boolean }>(endpoint(communityKey, params)) }), [communityKey, params]); const data = state.data?.params === params ? state.data.data : undefined;
  return <div className="stack"><h2>Участники кэмпа</h2><Field label="Поиск участника"><input type="search" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} /></Field>{state.error && <ErrorState message={state.error} retry={state.reload} />}{!data && state.loading && <Loading />}{data && <><p>Участников: {data.total}</p>{data.items.map(p => <button className="card" key={p.id} onClick={() => open(p.id)}>{p.name}<small>{days(p.dates)}</small></button>)}<Paging page={page} hasMore={data.hasMore} change={setPage} /></>}</div>;
}

function DeclineRequest({ communityKey, id, changed }: { communityKey: string; id: number; changed: () => void }) {
  const action = useAction(communityKey);
  return <><button disabled={action.busy} onClick={async () => { if (await action.act({ action: "decline", bggId: id })) changed(); }}>Не смогу</button>{action.error && <Notice kind="danger">{action.error}</Notice>}</>;
}
