import { registrationSubmitEnabled, toggleRegistrationDate } from "../camp/registrationLogic";
import { CampBoxControl, attendanceLabel } from "./CampBoxControl";
import { useCampProfile } from "./CampProfileContext";
import { CampWishlist, Incoming, useRefresh } from "../games/CampWishlist";
import { ExpansionPicker } from "../../components/ExpansionPicker";
import { groupCollectionItems } from "../../app/collectionGroups";
import { type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { ApiError, api, json } from "../../api/client";
import type { CampImport, ClubGame, Community, Contribution, PersonalCollectionItem, ImportDraftItem } from "../../api/types";
import { Badge, Card, Cover, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { GameMeta, GamePicker, searchGames } from "../../components/GamePicker";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { formatDate, formatInstant, plural } from "../../app/format";
import { bggImportProgressText } from "../../app/bggImportProgress";
import { defaultImportSelection, expansionBelongsToBase, importItemKey, importParentIds, isImportItemSelectable } from "../camp/importSelection";
import { importStatusTone } from "../../app/semanticTones";

type RegistrationState = { campStatus: string; startsAtUtc?: string; endsAtUtc?: string; startDate?: string; endDate?: string; dateLabels?: Record<string, string>; availableDates: string[]; baseGameIds: number[]; displayName?: string; registration?: { registered: boolean; daysStaying: number; selectedDates: string[]; suggestedDates: string[]; needsAccommodation: boolean; city?: string; displayName?: string } };

export function CampRegistrationSettings({ community }: { community: Community }) {
  const registration = useAsync(() => api<RegistrationState>(`/camp/registration?community=${encodeURIComponent(community.key)}`), [community.key]);
  if (registration.loading) return <Loading />;
  if (registration.error || !registration.data) return <ErrorState message={registration.error ?? "Регистрация недоступна"} retry={registration.reload} />;
  return <Registration community={community} state={registration.data} done={registration.reload} />;
}

export function CampRegistrationGate({ community, canOpenAdminPanel, children }: { community: Community; canOpenAdminPanel: boolean; children: ReactNode }) {
  const registration = useAsync(() => api<RegistrationState>(`/camp/registration?community=${encodeURIComponent(community.key)}`), [community.key]);
  if (registration.loading) return <Page as="section" title="Кэмп"><Loading /></Page>;
  if (registration.error) return <Page as="section" title="Кэмп"><ErrorState message={registration.error} retry={registration.reload} /></Page>;
  if (!registration.data?.startDate || !registration.data.endDate) return <Page as="section" title="Кэмп ещё настраивается" subtitle={community.name}><Notice kind="warning">Организатор ещё не указал даты кэмпа. Регистрация и создание сборов станут доступны после настройки.</Notice>{canOpenAdminPanel && <a className="button primary-link" href="?admin=1">Указать даты в админ-панели</a>}</Page>;
  if (!registration.data.registration?.registered) return <Registration community={community} state={registration.data} done={registration.reload} />;
  return <>{children}</>;
}

function Registration({ community, state, done, cancel }: { community: Community; state: RegistrationState; done: () => void; cancel?: () => void }) {
  const initialDates = state.registration?.selectedDates?.length ? state.registration.selectedDates : state.registration?.suggestedDates ?? [];
  const [selectedDates, setSelectedDates] = useState<string[]>(initialDates); const [housing, setHousing] = useState(state.registration?.needsAccommodation ?? false); const [name, setName] = useState(state.registration?.displayName ?? state.displayName ?? ""); const [city, setCity] = useState(state.registration?.city ?? ""); const [error, setError] = useState<string>(); const [busy, setBusy] = useState(false); const [validated, setValidated] = useState(false);
  async function save(confirmAttendanceChanges = false) { await api("/camp/registration", json("PUT", { communityKey: community.key, selectedDates, needsAccommodation: housing, displayName: name, city, confirmAttendanceChanges })); }
  async function submit() { if (busy) return; setValidated(true); if (!registrationSubmitEnabled(city, selectedDates, state.campStatus)) return; setBusy(true); setError(undefined); try { await save(); telegram.success("Регистрация сохранена"); done(); } catch (e) { if (e instanceof ApiError && e.code === "registration_dates_affect_gatherings" && e.affectedGatherings?.length) { const summary = e.affectedGatherings.map(x => `• ${x.gameName}, ${formatInstant(x.startsAtUtc, community.timeZoneId)}`).join("\n"); if (await telegram.confirm(`Вы больше не сможете участвовать в этих сборах:\n${summary}\n\nПродолжить?`)) { try { await save(true); telegram.success("Регистрация обновлена"); done(); return; } catch (retry) { setError(retry instanceof Error ? retry.message : String(retry)); } } } else setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function unregister() { if (busy || !await telegram.confirm("Отменить регистрацию?\n\nОтметки доступности в этом кэмпе будут удалены. Личная коллекция сохранится. Вы выйдете из будущих сборов, а история прошедших сборов сохранится.")) return; setBusy(true); setError(undefined); try { await api("/camp/registration/unregister", json("POST", { communityKey: community.key })); telegram.success("Регистрация отменена"); done(); } catch (e) { if (e instanceof ApiError && e.code === "registration_organizer_conflict" && e.affectedGatherings?.length) setError(`Сначала отмените свои будущие сборы:\n${e.affectedGatherings.map(item => `• ${item.gameName} · ${formatInstant(item.startsAtUtc, community.timeZoneId)}`).join("\n")}`); else setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  return <Page as="section" title={state.registration?.registered ? "Редактировать регистрацию" : "Регистрация на кэмп"} subtitle={`${formatDate(state.startDate)} — ${formatDate(state.endDate)}`} actions={cancel && <button onClick={cancel}>Назад</button>}><Card className="form-grid"><Field label="Имя" hint="Так вас увидят в списке участников и среди владельцев игр этого кэмпа."><input maxLength={128} value={name} onChange={e => setName(e.target.value)} /></Field><Field label="Город" hint="Его увидят только участники этого кэмпа" error={validated && !city.trim() ? "Укажите город." : undefined}><input required maxLength={100} value={city} onChange={e => setCity(e.target.value)} placeholder="Например, Астана" /></Field><fieldset><legend>Когда вы будете на кэмпе?</legend><p className="muted">Отметьте дни, когда будете на кэмпе. Создавать сборы и записываться можно только на выбранные дни. У игр с отметкой «Все дни моего участия» дни привоза изменятся вместе с регистрацией. Отдельно выбранные даты коробок останутся ограничением.</p>{state.availableDates.map(date => <label className="check" key={date}><input type="checkbox" checked={selectedDates.includes(date)} onChange={() => setSelectedDates(current => toggleRegistrationDate(current, date))} />{new Date(`${date}T00:00:00`).toLocaleDateString("ru-RU", { weekday: "short", day: "numeric", month: "long" })}{state.dateLabels?.[date] ? ` · ${state.dateLabels[date]}` : ""}</label>)}{validated && selectedDates.length === 0 && <small className="field-error" role="alert">Выберите хотя бы один день.</small>}</fieldset><label className="check"><input type="checkbox" checked={housing} onChange={e => setHousing(e.target.checked)} />Нужно жильё</label>{error && <Notice kind="danger"><span className="pre-line">{error}</span></Notice>}<button className="primary" disabled={busy || state.campStatus !== "Active"} aria-busy={busy} onClick={submit}>{busy ? "Сохраняем…" : state.registration?.registered ? "Сохранить изменения" : "Зарегистрироваться"}</button></Card>{state.registration?.registered && <Card className="danger-zone"><h2>Отменить регистрацию</h2><p>Отметки доступности в этом кэмпе будут удалены. Личная коллекция сохранится, а места в будущих сборах освободятся. История сохранится.</p><button className="danger ghost" disabled={busy} onClick={unregister}>Отменить регистрацию</button></Card>}</Page>;
}

export function ProfileCollectionPage({ community, bggAvailable, editRegistration, initialGameId }: { initialGameId?: number; community?: Community; bggAvailable: boolean; editRegistration?: () => void }) {
  const state = useAsync(() => api<PersonalCollectionItem[]>("/profile/collection/"), [community?.key]);
  const importSource = useAsync(() => api<{ source: { bggUsername: string; lastImportedAt: string } | null }>("/profile/collection/imports/source"), []);
  const campState = useAsync(() => community?.mode === "Camp" ? api<Contribution[]>(`/camp/contributions?community=${encodeURIComponent(community?.key ?? "")}`) : Promise.resolve([]), [community?.key]);
  const legacyKey = `oyinq-camp-import-${community?.key}`;
  const [legacyImport, setLegacyImport] = useState(() => !new URLSearchParams(location.search).get("profileImport") && Boolean(community && (new URLSearchParams(location.search).get("import") ?? localStorage.getItem(legacyKey))));
  const storageKey = legacyImport ? legacyKey : "oyinq-profile-import";
  const importBase = legacyImport ? "/camp" : "/profile/collection";
  const [importId, setImportId] = useState<string | undefined>(() => new URLSearchParams(location.search).get("profileImport") ?? (community ? new URLSearchParams(location.search).get("import") : null) ?? localStorage.getItem(storageKey) ?? undefined); const [chosen, setChosen] = useState<ClubGame>(); const [selectedExpansions, setSelectedExpansions] = useState<number[]>([]); const [listQuery, setListQuery] = useState(() => (initialGameId || new URLSearchParams(location.search).has("box")) ? "" : sessionStorage.getItem(`oyinq-collection-search:${community?.key ?? "global"}`) ?? ""); const [adding, setAdding] = useState(false); const [error, setError] = useState<string>();
  const profile = useCampProfile();
  const [filter, setFilter] = useState(() => (initialGameId || new URLSearchParams(location.search).has("box")) ? "all" : sessionStorage.getItem(`oyinq-collection-filter:${community?.key ?? "global"}`) ?? "all");
  const [addOpen, setAddOpen] = useState(false); const [importOpen, setImportOpen] = useState(true);
  const [importStatus, setImportStatus] = useState("Загрузка состояния");
  const detailScroll = useRef(0);
  const restoreDetailScroll = useRef(false);
  const [detail, setDetail] = useState<{ game?: number; person?: string }>();
  const [focusGame, setFocusGame] = useState<number | undefined>(() => initialGameId ?? (Number(new URLSearchParams(location.search).get("box")) || undefined));
  function openDetail(value: { game?: number; person?: string }) { detailScroll.current = window.scrollY; setDetail(value); window.scrollTo(0, 0); }
  useEffect(() => { if (!detail && restoreDetailScroll.current) { restoreDetailScroll.current = false; window.scrollTo(0, detailScroll.current); } }, [detail]);
  useRefresh(state.reload); useRefresh(campState.reload);
  useEffect(() => { sessionStorage.setItem(`oyinq-collection-search:${community?.key ?? "global"}`, listQuery); }, [listQuery, community?.key]);
  useEffect(() => { sessionStorage.setItem(`oyinq-collection-filter:${community?.key ?? "global"}`, filter); }, [filter, community?.key]);
  useEffect(() => { if (focusGame && state.data) { document.getElementById(`owned-game-${focusGame}`)?.scrollIntoView({ block: "center" }); setFocusGame(undefined); } }, [focusGame, state.data]);
  const collectionGroups = useMemo(() => {
    const values = state.data ?? [];
    const matches = new Set(searchGames(values.map(item => ({ bggId: item.bggId, ...item.snapshot, expansions: [] })), listQuery).map(game => game.bggId));
    return groupCollectionItems(values, item => (!listQuery.trim() || matches.has(item.bggId)) &&
      (community?.mode !== "Camp" || filter === "all" || filter === "demand" && Boolean(profile?.data?.suggestedGameIds.includes(item.bggId)) || Boolean(campState.data?.some(c => c.bggId === item.bggId && c.itemType === item.itemType && c.commitment === filter))));
  }, [state.data, listQuery, filter, campState.data, profile?.data, community?.mode]);
  useEffect(() => telegram.back(Boolean(importId && importOpen), () => setImportOpen(false)), [importId, importOpen]);
  async function startImport(input?: string) { setError(undefined); try { const result = await api<{ publicId: string }>("/profile/collection/imports", json("POST", { bggInput: input })); setLegacyImport(false); localStorage.setItem("oyinq-profile-import", result.publicId); setImportId(result.publicId); setImportOpen(true); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } }
  async function addManual() { if (adding || !chosen) return; setAdding(true); setError(undefined); try { await api("/profile/collection/manual", json("POST", { communityKey: community?.key, bggInput: String(chosen.bggId), expansionBggIds: selectedExpansions })); setChosen(undefined); setSelectedExpansions([]); telegram.success("Игра и данные BGG добавлены"); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setAdding(false); } }
  async function remove(item: PersonalCollectionItem) { if (!await telegram.confirm(`Удалить «${item.snapshot.name}» из личной коллекции во всех сообществах? Это не снятие отметки привоза: активное обещание сначала нужно снять отдельно.`)) return; setError(undefined); try { await api(`/profile/collection/${item.itemType}/${item.bggId}`, { method: "DELETE" }); telegram.success("Игра удалена"); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } }
  const renderItem = (item: PersonalCollectionItem) => {
    const contribution = campState.data?.find(x => x.bggId === item.bggId && x.itemType === item.itemType);
    return <div className="collection-item" id={`owned-game-${item.bggId}`}>
      <div className="media"><Cover src={item.snapshot.thumbnailImageUrl} name={item.snapshot.name} /><div><h3>{item.snapshot.name}</h3><GameMeta game={{ bggId: item.bggId, ...item.snapshot, expansions: [] }} compact /></div></div>
      {community?.mode === "Camp" && <CampBoxControl communityKey={community.key} item={item} contribution={contribution} disabled={campState.loading || Boolean(campState.error)} changed={campState.reload} />}
      <details className="collection-menu"><summary aria-label={`Действия с игрой ${item.snapshot.name}`}>Ещё</summary><button className="ghost" onClick={() => void remove(item)}>Удалить из коллекции</button></details>
    </div>;
  };
  return <>
    {importId && <div hidden={!importOpen}><ImportProgress active={importOpen} base={importBase} community={community} id={importId} onStatus={setImportStatus} close={(preserve = false) => {
      if (preserve) { setImportOpen(false); return; }
      localStorage.removeItem(storageKey); setImportId(undefined); setLegacyImport(false); state.reload(); importSource.reload();
    }} /></div>}
    {detail && community && <CampWishlist community={community} bggAvailable={bggAvailable} initialGameId={detail.game} initialPersonId={detail.person} back={() => { restoreDetailScroll.current = true; setDetail(undefined); }} />}
    <section className="profile-collection stack" hidden={Boolean(importId && importOpen) || Boolean(detail)}>
      <div className="row collection-heading"><h2>Игры · {state.data?.length ?? 0}</h2><button className="primary" aria-expanded={addOpen} aria-controls="collection-add" onClick={() => setAddOpen(!addOpen)}>{addOpen ? "Свернуть добавление ▴" : "Добавить ▾"}</button></div>
      <p className="muted">Личная коллекция общая для всех сообществ.</p>
      {!bggAvailable && <Notice kind="warning">BGG временно недоступен. Сохранённые игры можно просматривать, отмечать и удалять.</Notice>}
      {campState.error && <Notice kind="warning">{campState.error}</Notice>}
      {error && <Notice kind="danger">{error}</Notice>}
      {chosen && !addOpen && <p role="status">Выбрана игра «{chosen.name}». <button onClick={() => setAddOpen(true)}>Продолжить добавление</button></p>}
      {importId && !importOpen && <div className="notice" role="status">Импорт BGG: {importStatus}. <button onClick={() => setImportOpen(true)}>Открыть</button></div>}
      <div id="collection-add" hidden={!addOpen} className="content-section collection-tool">
        <h3>Добавить игру или дополнение</h3>
        <GamePicker bggAvailable={bggAvailable} selectionMode="item" selected={chosen} label="Найдите игру или дополнение по названию или BGG-ссылке" onSelect={game => { setChosen(game); setSelectedExpansions([]); }} onClear={() => { setChosen(undefined); setSelectedExpansions([]); }} />
        {chosen && <div className="selected-game"><strong>{chosen.name}</strong><ExpansionPicker expansions={chosen.expansions} selected={selectedExpansions} onChange={setSelectedExpansions} label="Дополнения в вашей коллекции" /><button className="primary" disabled={adding} onClick={addManual}>{adding ? "Добавляем…" : "Добавить в коллекцию"}</button></div>}
        <details className="import-options"><summary>Импорт из BGG</summary>{importSource.error && <ErrorState message={importSource.error} retry={importSource.reload} />}<BggImportButton available={bggAvailable} username={importSource.data?.source?.bggUsername} lastImportedAt={importSource.data?.source?.lastImportedAt} start={startImport} /></details>
      </div>
      {community?.mode === "Camp" && <>
        <p className="muted">Привоз на «{community.name}». Отметки привоза действуют на все дни вашего участия{profile?.data?.myDates.length ? `: ${attendanceLabel(profile.data.myDates)}` : ""}. Для отдельных коробок можно выбрать другие дни. <button className="person-link" onClick={editRegistration}>Изменить регистрацию</button></p>
        <Incoming allowDecline communityKey={community.key} open={id => { setFocusGame(id); requestAnimationFrame(() => document.getElementById(`owned-game-${id}`)?.scrollIntoView({ block: "center" })); setFilter("all"); setListQuery(""); if (!state.data?.some(x => x.bggId === id && x.itemType === "BaseGame")) openDetail({ game: id }); }} openProfile={person => openDetail({ person })} />
        {!!profile?.data?.suggestedGameIds.length && <button className="ghost" onClick={() => setFilter(filter === "demand" ? "all" : "demand")}>У участников в хотелках: {profile.data.suggestedGameIds.length} ваших игр{filter === "demand" ? " · Показать все" : ""}</button>}
      </>}
      <Field label="Поиск среди добавленных игр"><input type="search" value={listQuery} onChange={event => setListQuery(event.target.value)} placeholder="Название игры" /></Field>
      {community?.mode === "Camp" && <div className="wish-filters">{[["all", "Все"], ["Bringing", "Точно привезу"], ["Available", "Могу привезти"]].map(([value, label]) => <button key={value} className={filter === value ? "filter-chip active" : "filter-chip"} aria-pressed={filter === value} onClick={() => setFilter(value)}>{label}</button>)}</div>}
      {state.loading && !state.data && <Loading />}{state.error && <ErrorState message={state.error} retry={state.reload} />}
      {state.data && (!state.data.length ? <Empty>В коллекции пока нет игр. <button onClick={() => setAddOpen(true)}>Добавить игры</button></Empty> : !collectionGroups.length ? <Empty>Ничего не найдено. Измените поиск или фильтр.</Empty> : <div className="stack">{collectionGroups.map(group => <Card key={`${group.item.itemType}-${group.item.bggId}`}>
        {renderItem(group.item)}
        {group.expansions.length > 0 && <details className="collection-expansions" open={listQuery.trim() || filter !== "all" ? true : undefined}><summary>Дополнения ({group.expansions.length})</summary>{group.expansions.map(item => <div className="collection-expansion" key={item.bggId}>{renderItem(item)}</div>)}</details>}
      </Card>)}</div>)}
    </section>
  </>;
}

function BggImportButton({ available, username, lastImportedAt, start }: { available: boolean; username?: string; lastImportedAt?: string; start: (input?: string) => Promise<void> }) {
  const [open, setOpen] = useState(false); const [input, setInput] = useState(username ?? ""); const [busy, setBusy] = useState(false);
  async function submit(saved = false) { if (busy || !available || (!saved && !input.trim())) return; setBusy(true); try { await start(saved ? undefined : input); } finally { setBusy(false); } }
  return <>{username && <><p>Аккаунт BGG: <strong>{username}</strong></p>{lastImportedAt && <p className="muted">Последний импорт: {new Date(lastImportedAt).toLocaleString("ru-RU")}</p>}</>}{open ? <><Field label="Профиль BoardGameGeek" hint="Имя пользователя или ссылка на профиль"><input disabled={busy} value={input} onChange={e => setInput(e.target.value)} placeholder="Например, John90" /></Field><div className="row"><button className="primary" disabled={busy || !available || !input.trim()} aria-busy={busy} onClick={() => submit()}>{busy ? "Запускаем импорт…" : "Импортировать коллекцию"}</button><button disabled={busy} onClick={() => setOpen(false)}>Отмена</button></div></> : <div className="row">{username && <button className="primary" disabled={busy || !available} aria-busy={busy} onClick={() => submit(true)}>{busy ? "Запускаем импорт…" : "Обновить из BGG"}</button>}<button disabled={busy || !available} onClick={() => { setInput(username ?? ""); setOpen(true); }}>{username ? "Сменить аккаунт" : "Импортировать коллекцию"}</button></div>}</>;
}

function ImportProgress({ base, community, id, close, onStatus, active }: { active: boolean; onStatus?: (status: string) => void; base: string; community?: Community; id: string; close: (preserve?: boolean) => void }) {
  const state = useAsync(() => api<CampImport>(`${base}/imports/${id}?community=${encodeURIComponent(community?.key ?? "")}`), [community?.key, id]);
  useEffect(() => { if (state.error) onStatus?.("не удалось обновить состояние"); else if (state.data) onStatus?.(({ Queued: "в очереди", Running: "выполняется", Completed: "выберите игры для сохранения", Failed: "ошибка — требуется повтор", Confirmed: "игры сохранены", Cancelled: "отменён" } as Record<string, string>)[state.data.status] ?? state.data.status); }, [state.data?.status, state.error, onStatus]);
  const [actionError, setActionError] = useState<string>();
  const [actionBusy, setActionBusy] = useState(false);
  useEffect(() => { if (active) state.reload(); }, [active]);
  useEffect(() => { if (active && (!state.data || ["Queued", "Running"].includes(state.data.status))) { const timer = window.setInterval(state.reload, 3000); return () => clearInterval(timer); } }, [active, state.data?.status, state.reload]);
  async function cancel() { if (actionBusy || !await telegram.confirm("Отменить импорт? Выбранные игры не будут сохранены.")) return; setActionBusy(true); setActionError(undefined); try { await api(`${base}/imports/${id}/cancel`, json("POST", { communityKey: community?.key })); telegram.success("Импорт отменён"); close(); } catch (e) { setActionError(e instanceof Error ? e.message : String(e)); } finally { setActionBusy(false); } }
  async function retry() { if (actionBusy) return; setActionBusy(true); setActionError(undefined); try { await api(`${base}/imports/${id}/retry`, json("POST", { communityKey: community?.key })); state.reload(); } catch (e) { setActionError(e instanceof Error ? e.message : String(e)); } finally { setActionBusy(false); } }
  if (state.loading && !state.data) return <Page preserveScroll as="section" title="Импорт BGG"><Loading label="BGG готовит коллекцию. Можно закрыть и вернуться позже." /></Page>;
  if (state.error) return <Page preserveScroll as="section" title="Импорт BGG" actions={<button onClick={() => close(true)}>Закрыть</button>}><ErrorState message={state.error} retry={state.reload} /></Page>;
  if (!state.data) return null;
  if (state.data.status === "Failed") return <Page preserveScroll as="section" title="Импорт BGG"><Badge tone={importStatusTone(state.data.status, state.data.stage)}>Ошибка</Badge><Notice kind="danger">Не удалось импортировать коллекцию BGG. Сохранённые игры не изменены; попробуйте ещё раз позже.</Notice>{actionError && <Notice kind="danger">{actionError}</Notice>}<button className="primary" disabled={actionBusy} onClick={retry}>{actionBusy ? "Запускаем повтор…" : "Повторить"}</button><button disabled={actionBusy} onClick={() => close(true)}>К моей коллекции</button></Page>;
  if (state.data.status === "Cancelled") return <Page preserveScroll as="section" title="Импорт отменён"><Badge tone={importStatusTone(state.data.status, state.data.stage)}>Отменён</Badge><Notice>Игры из этой загрузки не добавлены.</Notice><button onClick={() => close()}>К моей коллекции</button></Page>;
  if (state.data.status !== "Completed" && state.data.status !== "Confirmed") return <Page preserveScroll as="section" title="Импорт BGG" subtitle={bggImportProgressText(state.data)}><Badge tone={importStatusTone(state.data.status, state.data.stage)}>{state.data.status === "Queued" ? "В очереди" : "Выполняется"}</Badge><Loading label="Импорт выполняется в фоне. Эту страницу можно закрыть." />{actionError && <Notice kind="danger">{actionError}</Notice>}<div className="row"><button disabled={actionBusy} onClick={() => close(true)}>Продолжить позже</button><button disabled={actionBusy} className="danger ghost" onClick={cancel}>{actionBusy ? "Отменяем…" : "Отменить импорт"}</button></div></Page>;
  if (state.data.status === "Confirmed") { const overridable = state.data.draft?.items.filter(item => item.skipReason === "AlreadyInBaseCollection" && item.isOverridable) ?? []; return <Page preserveScroll as="section" title="Импорт завершён"><Badge tone={importStatusTone(state.data.status, state.data.stage)}>Подтверждён</Badge><Notice kind="success">Выбранные игры сохранены.</Notice>{state.data.hasSelectedOverridableItems && !state.data.overrideResolution && <BaseDuplicateResolution community={community} id={id} count={overridable.length} done={state.reload} />}{state.data.overrideResolution && <Notice>{state.data.overrideResolution === "AddPersonalCopies" ? "Ваши личные копии добавлены." : "Игры остаются только в общей коллекции кэмпа."}</Notice>}<button onClick={() => close()}>К моей коллекции</button></Page>; }
  return <><button onClick={() => close(true)}>Продолжить позже</button><ImportSelection base={base} community={community} id={id} items={state.data.draft?.items ?? []} foundGames={state.data.foundGames} foundExpansions={state.data.foundExpansions} done={() => close()} cancel={cancel} cancellationError={actionError} /></>;
}

function ImportSelection({ base, community, id, items, foundGames, foundExpansions, done, cancel, cancellationError }: { base: string; community?: Community; id: string; items: ImportDraftItem[]; foundGames: number; foundExpansions: number; done: () => void; cancel: () => void; cancellationError?: string }) {
  const [selected, setSelected] = useState(() => defaultImportSelection(items));
  const [query, setQuery] = useState(""); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const isExpansion = (item: ImportDraftItem) => item.itemType === "Expansion";
  const normalizedQuery = query.trim().toLowerCase();
  const expansions = useMemo(() => items.filter(isExpansion), [items]);
  const matchesName = (snapshot: ImportDraftItem["snapshot"]) => !normalizedQuery
    || snapshot.name.toLowerCase().includes(normalizedQuery)
    || snapshot.originalName?.toLowerCase().includes(normalizedQuery);
  const bases = useMemo(() => items.filter(x => !isExpansion(x) && (matchesName(x.snapshot) || expansions.some(exp => expansionBelongsToBase(exp, x.bggId) && matchesName(exp.snapshot)))), [items, expansions, normalizedQuery]);
  const orphans = useMemo(() => expansions.filter(exp => !items.some(base => !isExpansion(base) && expansionBelongsToBase(exp, base.bggId)) && matchesName(exp.snapshot)), [items, expansions, normalizedQuery]);
  const toggle = (item: ImportDraftItem) => setSelected(current => { const next = new Set(current); const key = importItemKey(item); next.has(key) ? next.delete(key) : next.add(key); return next; });
  const hasBase = (item: ImportDraftItem) => importParentIds(item).length === 0 || items.some(base => !isExpansion(base) && expansionBelongsToBase(item, base.bggId) && selected.has(`${base.itemType}-${base.bggId}`));
  const selectable = isImportItemSelectable;
  const displayedGames = foundGames || items.filter(item => !isExpansion(item)).length;
  const displayedExpansions = foundExpansions || items.filter(isExpansion).length;
  async function confirm() { if (busy) return; setBusy(true); try { await api(`${base}/imports/${id}/confirm`, json("POST", { communityKey: community?.key, selectedBaseGameIds: items.filter(x => !isExpansion(x) && selected.has(`${x.itemType}-${x.bggId}`)).map(x => x.bggId), selectedExpansionIds: items.filter(x => isExpansion(x) && selected.has(`${x.itemType}-${x.bggId}`)).map(x => x.bggId) })); telegram.success("Игры добавлены"); done(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  const hasMatches = bases.length > 0 || orphans.length > 0;
  return <Page preserveScroll as="section" title="Выберите игры" subtitle={`Выбрано ${selected.size} из ${items.length}`} actions={<button className="danger ghost" onClick={cancel}>Отменить импорт</button>}><Notice kind="success">Получили коллекцию BGG — {plural(displayedGames, "игра", "игры", "игр")} · {plural(displayedExpansions, "дополнение", "дополнения", "дополнений")}.</Notice><div className="row"><button onClick={() => setSelected(new Set(items.filter(selectable).map(x => `${x.itemType}-${x.bggId}`)))}>Выбрать всё доступное</button><button onClick={() => setSelected(new Set())}>Очистить</button></div><Field label="Поиск"><input type="search" value={query} onChange={e => setQuery(e.target.value)} placeholder="Игра или дополнение" /></Field>{!hasMatches ? <Empty>Ничего не найдено.</Empty> : <div className="import-groups">{bases.map(base => { const children = expansions.filter(x => expansionBelongsToBase(x, base.bggId) && (matchesName(base.snapshot) || matchesName(x.snapshot))); return <details open={normalizedQuery ? true : undefined} key={base.bggId}><summary><label className="check"><input type="checkbox" disabled={!selectable(base)} checked={selected.has(`${base.itemType}-${base.bggId}`)} onChange={() => toggle(base)} /><ImportItemSummary item={base} /></label></summary>{children.map(exp => <label className={`check nested ${selected.has(`${exp.itemType}-${exp.bggId}`) && !hasBase(exp) ? "missing-base" : ""}`} key={exp.bggId}><input type="checkbox" disabled={!selectable(exp)} checked={selected.has(`${exp.itemType}-${exp.bggId}`)} onChange={() => toggle(exp)} /><ImportItemSummary item={exp} />{selected.has(`${exp.itemType}-${exp.bggId}`) && !hasBase(exp) && <span className="missing-base-note">Нет базовой игры</span>}</label>)}</details>; })}{orphans.length > 0 && <section className="notice warning"><strong>Дополнения без базовой игры</strong><p className="muted">Это допустимо: их можно привезти отдельно.</p>{orphans.map(exp => <label className={`check nested ${selected.has(`${exp.itemType}-${exp.bggId}`) ? "missing-base" : ""}`} key={exp.bggId}><input type="checkbox" disabled={!selectable(exp)} checked={selected.has(`${exp.itemType}-${exp.bggId}`)} onChange={() => toggle(exp)} /><ImportItemSummary item={exp} />{selected.has(`${exp.itemType}-${exp.bggId}`) && <span className="missing-base-note">Базовой игры нет в вашей коллекции</span>}</label>)}</section>}</div>}{(error || cancellationError) && <Notice kind="danger">{error ?? cancellationError}</Notice>}<button className="primary sticky-action" disabled={busy} onClick={confirm}>{busy ? "Сохраняем…" : `Сохранить (${selected.size})`}</button></Page>;
}

function ImportItemSummary({ item }: { item: ImportDraftItem }) {
  const reason = item.skipReason === "AlreadyInBaseCollection" ? "Уже есть в базовой коллекции" : item.skipReason === "AlreadyAddedManually" ? "Уже добавлено вручную" : item.skipReason ? "Не удалось добавить" : undefined;
  return <span className="import-item-summary"><Cover src={item.snapshot.thumbnailImageUrl} name={item.snapshot.name} /><span><strong>{item.snapshot.name}</strong>{reason && <small>{reason}</small>}<GameMeta game={{ bggId: item.bggId, ...item.snapshot, expansions: [] }} compact /></span></span>;
}

function BaseDuplicateResolution({ community, id, count, done }: { community?: Community; id: string; count: number; done: () => void }) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  async function resolve(resolution: "KeepBaseCollection" | "AddPersonalCopies") { if (busy) return; setBusy(true); setError(undefined); try { await api(`/camp/imports/${id}/resolve-base-duplicates`, json("POST", { communityKey: community?.key, resolution })); telegram.success(resolution === "AddPersonalCopies" ? "Личные копии добавлены" : "Оставлено без изменений"); done(); } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); } finally { setBusy(false); } }
  return <Card className="form-grid"><h2>Игры уже есть в общей коллекции</h2><p>{plural(count, "игра", "игры", "игр")} уже есть в общей коллекции кэмпа. Если у вас есть своя коробка, добавьте её отдельно.</p><div className="row"><button disabled={busy} onClick={() => resolve("KeepBaseCollection")}>Оставить как есть</button><button className="primary" disabled={busy} onClick={() => resolve("AddPersonalCopies")}>Добавить мои копии</button></div>{error && <Notice kind="danger">{error}</Notice>}</Card>;
}
