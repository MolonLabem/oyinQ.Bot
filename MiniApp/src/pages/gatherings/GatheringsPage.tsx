import { plural, formatDuration } from "../../app/format";
import { GameFact, GameFacts } from "../../components/GameFact";
import { DateTimeField } from "../../components/DateTimeField";
import { useBackButton } from "../../hooks/useBackButton";
import { useRefresh } from "../../hooks/useRefreshOnActivity";
import type { ExpansionLookup } from "../../components/gamePickerModel";
import { useGatheringExpansions } from "./useGatheringExpansions";
import { creationOperation, completeCreation } from "./gatheringCreationOperation";
import { ComplexityBadge, ComplexityDetails } from "../../components/ComplexityBadge";
import { WishButton } from "../../components/Wishlist";
import { GuestRow } from "./GuestRow";
import { ParticipantNameEditor } from "./ParticipantNameEditor";
import { PlayPanel } from "./PlayPanel";
import { GameProviderNotice } from "../../components/GameProviderNotice";
import { useEffect, useMemo, useRef, useState } from "react";
import { profileGeneration } from "../../app/profileLifecycle";
import { readGatheringDraft, saveGatheringDraft, clearGatheringDraft, type GatheringDraft } from "./gatheringDraft";
import { ApiError, api, gatheringMutation, json } from "../../api/client";
import type { ClubGame, Community, GatheringDetail, GatheringListPage } from "../../api/types";
import { Pagination, BackButton, Badge, Card, ContactLink, Cover, Empty, ErrorState, Field, Loading, Notice, Page, SegmentedControl, Tabs } from "../../components/Ui";
import { GameMeta, GamePicker } from "../../components/GamePicker";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { currentLocalMinute, formatLocalDateTime, isFutureLocalDateTime } from "../../app/format";
import { changeGatheringSeats, type GatheringSeatFilter, buildGatheringListQuery, changeGatheringHistoryFilter, changeGatheringView, gatheringHistoryFilter, gatheringListView, initialGatheringListState, type GatheringHistoryFilter, type GatheringListState, type GatheringListView } from "./gatheringListState";
import { resolvePlayerCountRange, fitPlayerLimits, type PlayerLimits } from "./playerCountRange";
import { GatheringPlayerLimits } from "./GatheringPlayerLimits";
import { GatheringExpansionPicker, emptyExpansionOwnership } from "./GatheringExpansionPicker";
import { gatheringDateTimeBounds, isWithinCampDateRange, revalidateGatheringStart } from "./gatheringDateRange";
import { GatheringCollectionAction } from "./GatheringGameMetadata";
import { BotStartNotice } from "../../components/BotStartNotice";
import { GameTaxonomy } from "../../components/GameTaxonomy";
import { gatheringStatusTone, participationStatusTone } from "../../app/semanticTones";
import { GatheringCard } from "../../components/GatheringCard";
import { gatheringDays } from "./gatheringListPresentation";

export function GatheringsPage({ community, bggAvailable, initialGatheringId, initialGameId, onGameConsumed, onInitialConsumed, editRegistration, openCollection, backFromInitial }: { community: Community; bggAvailable: boolean; initialGatheringId?: string; initialGameId?: number; onGameConsumed?: () => void; onInitialConsumed: () => void; editRegistration: () => void; openCollection: (bggId: number, gatheringId: string) => void; backFromInitial?: () => void }) {
  const [screen, setScreen] = useState<"list" | "create" | "detail">(initialGatheringId ? "detail" : initialGameId ? "create" : "list");
  const [seedGameId, setSeedGameId] = useState(initialGameId);
  const [copy, setCopy] = useState<GatheringDetail>();
  const [selected, setSelected] = useState<string | undefined>(initialGatheringId);
  const [initialBack] = useState<(() => void) | undefined>(() => initialGatheringId ? backFromInitial : undefined);
  const [listState, setListState] = useState<GatheringListState>(initialGatheringListState);
  useBackButton(screen !== "list", () => { if (screen === "detail" && initialBack) initialBack(); else { setScreen("list"); setSelected(undefined); setSeedGameId(undefined); setCopy(undefined); } });
  useEffect(() => { if (initialGatheringId) onInitialConsumed(); if (initialGameId) onGameConsumed?.(); }, []);
  if (screen === "create") return <CreateGathering copy={copy} initialGameId={seedGameId} community={community} bggAvailable={bggAvailable} onDone={() => { setScreen("list"); setSeedGameId(undefined); setCopy(undefined); }} editRegistration={editRegistration} />;
  if (screen === "detail" && selected) return <GatheringDetails onCopy={value => { setCopy(value); setScreen("create"); }} key={`${community.key}-${selected}`} community={community} id={selected} onBack={() => { if (initialBack) initialBack(); else setScreen("list"); }} onCancelled={() => { setListState({ ...listState, scope: "cancelled", page: 1 }); setSelected(undefined); setScreen("list"); }} editRegistration={editRegistration} openCollection={bggId => openCollection(bggId, selected)} />;
  return <GatheringList hasDraft={Boolean(readGatheringDraft(community.key))} community={community} listState={listState} setListState={setListState} open={id => { setSelected(id); setScreen("detail"); }} create={() => { setCopy(undefined); setScreen("create"); }} />;
}

function GatheringList({ community, listState, setListState, open, create, hasDraft }: { hasDraft: boolean; community: Community; listState: GatheringListState; setListState: (state: GatheringListState) => void; open: (id: string) => void; create: () => void }) {
  const { scope, page, seats = "all" } = listState;
  const view = gatheringListView(scope);
  const historyFilter = gatheringHistoryFilter(scope);
  const params = buildGatheringListQuery(community.key, listState);
  const state = useAsync(async () => ({ params, result: await api<GatheringListPage>(`/gatherings?${params}`, { cache: "no-store" }) }), [params]);
  const result = state.data?.params === params ? state.data.result : undefined;
  useRefresh(state.reload);
  const selectView = (next: GatheringListView) => setListState(changeGatheringView(listState, next));
  const selectHistoryFilter = (next: GatheringHistoryFilter) => setListState(changeGatheringHistoryFilter(listState, next));
  return <div className="gathering-list-screen"><Page title="Сборы" hideTitle>
    <div className="gathering-list-toolbar">
    <Tabs label="Раздел сборов" active={view} onChange={id => selectView(id as GatheringListView)} items={[{ id: "upcoming", label: "Предстоящие" }, { id: "history", label: "История" }]} />
    <button className="primary" onClick={create}>{hasDraft ? "Продолжить сбор" : "Создать сбор"}</button>
    </div>
    {view === "upcoming" && <SegmentedControl label="Наличие мест" active={seats} onChange={id => setListState(changeGatheringSeats(listState, id as GatheringSeatFilter))} items={[{ id: "all", label: "Все" }, { id: "available", label: "Есть места" }, { id: "full", label: "Мест нет" }]} />}
    {view === "history" && <SegmentedControl className="history-filters" label="Фильтр истории" active={historyFilter} onChange={id => selectHistoryFilter(id as GatheringHistoryFilter)} items={[{ id: "all", label: "Все" }, { id: "completed", label: "Завершены" }, { id: "cancelled", label: "Отменены" }]} />}
    {result && state.error && <ErrorState message={state.error} retry={state.reload} />}
    {!result && state.loading ? <Loading /> : !result && state.error ? <ErrorState message={state.error} retry={state.reload} /> : !result?.items.length ? view === "upcoming" ? seats !== "all" ? <><Empty>{seats === "available" ? "Сборов со свободными местами пока нет" : "Полных сборов пока нет"}</Empty><button onClick={() => setListState(changeGatheringSeats(listState, "all"))}>Показать все</button></> : <><Empty>Пока никто не собирается играть. Создайте первый сбор.</Empty><button className="primary" onClick={create}>{hasDraft ? "Продолжить сбор" : "Создать сбор"}</button></> : <Empty>{historyFilter === "completed" ? "Завершённых сборов пока нет." : historyFilter === "cancelled" ? "Отменённых сборов нет." : "История сборов пока пуста."}</Empty> :
      <><div className="gathering-days">{gatheringDays(result.items, community.timeZoneId).map((day, index) => <section className="gathering-day" key={`${day.date ?? "undated"}:${index}`}>
        {day.label && <h2 className="gathering-day-heading"><time dateTime={day.date}>{day.label}</time></h2>}
        <div className="stack">{day.items.map(({ item, time }) => <GatheringCard key={item.card.publicId} item={item} time={time} open={open} />)}</div>
      </section>)}</div><Pagination page={page} hasPrevious={result.hasPrevious} hasNext={result.hasNext} onChange={page => setListState({ ...listState, page })} label="Страницы сборов" /></>}
  </Page></div>;
}

export function CreateGathering({ community, bggAvailable, onDone, editRegistration, initialGameId, copy }: { copy?: GatheringDetail; initialGameId?: number; community: Community; bggAvailable: boolean; onDone: () => void; editRegistration: () => void }) {
  const games = useAsync(() => api<ClubGame[]>(`/games?community=${encodeURIComponent(community.key)}`), [community.key, community.mode]);
  const [restoredDraft, setRestoredDraft] = useState(() => readGatheringDraft(community.key));
  useEffect(() => {
    // An explicitly new form is a new intent, even if a background submission
    // previously finished with identical values. A resumed draft keeps its ID.
    if (!restoredDraft) completeCreation(community.key);
  }, []);
  const [draftGeneration] = useState(profileGeneration);
  const draftFinished = useRef(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const dateBounds = gatheringDateTimeBounds(community, currentLocalMinute(community.timeZoneId));
  const [initialStarts] = useState(() => revalidateGatheringStart("", community, dateBounds));
  const [expansionOwnership, setExpansionOwnership] = useState(restoredDraft?.expansionOwnership ?? emptyExpansionOwnership);
  const [addToCollection, setAddToCollection] = useState(restoredDraft?.addToCollection ?? false);
  const [bringToCamp, setBringToCamp] = useState(restoredDraft?.bringToCamp ?? false);
  const [source, setSource] = useState<GatheringDraft["source"]>(restoredDraft?.source ?? "catalog");
  const [chosen, setChosen] = useState<ClubGame | undefined>(restoredDraft?.chosen);
  const [initialExpansionLookup, setInitialExpansionLookup] = useState<ExpansionLookup | undefined>(restoredDraft?.initialExpansionLookup);
  const [expansions, setExpansions] = useState(restoredDraft?.expansions ?? []);
  const [starts, setStarts] = useState(restoredDraft?.starts ?? initialStarts);
  const [minimum, setMinimum] = useState(restoredDraft?.minimum ?? 2);
  const [desired, setDesired] = useState(restoredDraft?.desired ?? 4);
  const [maximum, setMaximum] = useState(restoredDraft?.maximum ?? 4);
  const [description, setDescription] = useState(restoredDraft?.description ?? "");
  const [teach, setTeach] = useState(restoredDraft?.teach ?? true);
  const [copySourceId, setCopySourceId] = useState(restoredDraft?.copySourceId);
  const [seeded, setSeeded] = useState(Boolean(restoredDraft));
  const [changingGame, setChangingGame] = useState(false);
  const changeGameButton = useRef<HTMLButtonElement>(null);
  const picker = useRef<HTMLDivElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [attendanceRequired, setAttendanceRequired] = useState(false);
  const draft = useMemo<GatheringDraft>(() => ({ source, chosen, initialExpansionLookup, expansions, expansionOwnership,
    addToCollection, bringToCamp, starts, minimum, desired, maximum, description, teach, copySourceId }),
  [source, chosen, initialExpansionLookup, expansions, expansionOwnership, addToCollection, bringToCamp, starts, minimum, desired, maximum, description, teach, copySourceId]);
  useEffect(() => {
    if (draftFinished.current || draftGeneration !== profileGeneration()) return;
    if (chosen || description || !teach || starts !== initialStarts) saveGatheringDraft(community.key, draft);
    else clearGatheringDraft(community.key);
  }, [community.key, draft, draftGeneration, chosen, description, teach, starts, initialStarts]);
  useEffect(() => {
    setStarts(current => revalidateGatheringStart(current, community, dateBounds));
  }, [community.key, community.startDate, community.endDate, dateBounds.min, dateBounds.max]);
  useEffect(() => { if (changingGame) picker.current?.querySelector("input")?.focus(); }, [changingGame]);
  const expansionLookup = useGatheringExpansions(chosen?.bggId, chosen?.expansions ?? [], initialExpansionLookup);
  const chosenPlayers = resolvePlayerCountRange(chosen?.minPlayers, chosen?.maxPlayers, expansionLookup.expansions, expansions);
  function changeLimits(value: PlayerLimits) { setMinimum(value.minimum); setDesired(value.desired); setMaximum(value.maximum); }
  function changeExpansions(ids: number[]) {
    changeLimits(fitPlayerLimits({ minimum, desired, maximum }, chosenPlayers,
      resolvePlayerCountRange(chosen?.minPlayers, chosen?.maxPlayers, expansionLookup.expansions, ids)));
    setExpansions(ids);
  }
  function closePicker() {
    setChangingGame(false);
    requestAnimationFrame(() => changeGameButton.current?.focus({ preventScroll: true }));
  }
  function chooseGame(game: ClubGame, nextSource: "catalog" | "bgg", initialExpansions: number[] = [], lookup?: ExpansionLookup) {
    setInitialExpansionLookup(lookup);
    setCopySourceId(undefined); setExpansionOwnership(emptyExpansionOwnership()); setAddToCollection(false); setBringToCamp(false);
    setSource(nextSource); setChosen({ ...game, expansions: game.expansions ?? [] }); setExpansions(initialExpansions); setError(undefined);
    if (changingGame) closePicker();
    const players = resolvePlayerCountRange(game.minPlayers, game.maxPlayers, game.expansions, []);
    const suggested = Number.parseInt(game.bestPlayers?.match(/\d+/)?.[0] ?? "", 10);
    const nextDesired = Number.isFinite(suggested) ? Math.min(players.maximum, Math.max(players.minimum, suggested)) : players.maximum;
    changeLimits(fitPlayerLimits({ minimum: players.minimum, desired: nextDesired, maximum: players.maximum }, players,
      resolvePlayerCountRange(game.minPlayers, game.maxPlayers, game.expansions, initialExpansions)));
  }
  useEffect(() => {
    if (seeded) return;
    if (copy?.gathering.bggId) {
      chooseGame({ bggId: copy.gathering.bggId, name: copy.gathering.gameName, thumbnailImageUrl: copy.gathering.imageUrl,
        minPlayers: copy.gameBaseMinimumPlayers ?? copy.gameMinimumPlayers, maxPlayers: copy.gameBaseMaximumPlayers ?? copy.gameMaximumPlayers,
        expansions: copy.knownExpansions, complexityInfo: copy.gathering.complexityInfo }, "catalog", copy.selectedExpansionIds);
      setMinimum(copy.minimumPlayers); setDesired(copy.desiredPlayers); setMaximum(copy.maximumPlayers);
      setDescription(copy.description ?? ""); setTeach(copy.canTeachRules); setCopySourceId(copy.gathering.publicId); setSeeded(true); return;
    }
    if (!initialGameId || !games.data) return;
    const game = games.data.find(x => x.bggId === initialGameId);
    if (game) chooseGame(game, "catalog");
    else {
      let current = true;
      void api<ClubGame>(`/catalog/demand/${initialGameId}?community=${encodeURIComponent(community.key)}`).then(game => {
        if (!current) return;
        if (game.bggId === initialGameId) { chooseGame(game, "catalog"); setSource("demand"); }
        else setError("Игра больше не доступна в каталоге. Выберите её заново.");
        setSeeded(true);
      }).catch(e => { if (current) { setError(e instanceof Error ? e.message : String(e)); setSeeded(true); } });
      return () => { current = false; };
    }
    setSeeded(true);
  }, [games.data, initialGameId, seeded, copy, community.key]);
  async function discard(restart = false) {
    if (busy || !await telegram.confirm(restart ? "Удалить сохранённый черновик и начать новый сбор?" : "Удалить черновик сбора? Введённые данные будут потеряны.")) return;
    clearGatheringDraft(community.key); completeCreation(community.key);
    if (!restart) { draftFinished.current = true; onDone(); return; }
    setRestoredDraft(undefined); setChosen(undefined); setInitialExpansionLookup(undefined); setExpansions([]);
    setExpansionOwnership(emptyExpansionOwnership()); setAddToCollection(false); setBringToCamp(false); setSource("catalog");
    setStarts(initialStarts); setMinimum(2); setDesired(4); setMaximum(4); setDescription(""); setTeach(true);
    setCopySourceId(undefined); setError(undefined); setAttendanceRequired(false); setChangingGame(false); setSeeded(false);
  }
  async function submit() {
    if (busy) return;
    if (!chosen || !starts) { setError("Выберите игру, дату и время."); return; }
    if (!isWithinCampDateRange(starts, community)) { setError("Дата сбора должна быть в пределах дат кэмпа."); return; }
    if (!isFutureLocalDateTime(starts, community.timeZoneId)) { setError("Выберите дату и время в будущем."); return; }
    if (minimum < 1 || minimum > desired || desired > maximum) { setError("Проверьте лимиты игроков: минимум ≤ желаемое число ≤ максимум."); return; }
    setBusy(true); setError(undefined); setAttendanceRequired(false);
    try {
      const body = { copyFromPublicId: copySourceId, communityKey: community.key, gameSource: source, bggId: chosen.bggId, selectedExpansionIds: expansions, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach, addToCollection, bringToCamp, addExpansionToCollectionIds: expansionOwnership.add, bringExpansionIds: expansionOwnership.bring };
      const operationId = creationOperation(community.key, body);
      await gatheringMutation("/gatherings", json("POST", { ...body, operationId }));
      if (draftGeneration !== profileGeneration()) return;
      draftFinished.current = true; clearGatheringDraft(community.key, draft);
      // A resumed form may still hold the submitted values. Retain the retry
      // identity when this instance has gone away so resubmission stays safe.
      if (mounted.current) completeCreation(community.key, operationId);
      telegram.success("Сбор создан"); if (mounted.current) onDone();
    } catch (e) { setAttendanceRequired(e instanceof ApiError && e.code === "camp_attendance_date_required"); setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }
  const repeating = Boolean(copySourceId || copy && !seeded && !restoredDraft);
  return <div className="gathering-form-screen"><Page title={repeating ? "Повторить сбор" : "Новый сбор"} actions={<BackButton onClick={onDone} />}>
    {restoredDraft && <div className="gathering-draft-notice"><p role="status">Черновик восстановлен.</p>{(initialGameId || copy) && <button type="button" disabled={busy} onClick={() => void discard(true)}>Начать новый сбор</button>}</div>}
    {repeating && <Notice>Укажите дату и коробку. Игроки запишутся заново.</Notice>}
    {!bggAvailable && <Notice kind="warning">BGG недоступен. Выберите игру из каталога.</Notice>}
    {chosen && !changingGame ? <section className="content-section gathering-selected-game">
      <Cover src={chosen.thumbnailImageUrl} name={chosen.name} /><div><h2>{chosen.name}</h2><button ref={changeGameButton} type="button" disabled={busy} onClick={() => setChangingGame(true)}>Изменить игру</button></div>
    </section> : <section ref={picker} className="content-section gathering-create-section"><GamePicker catalog={games.data} catalogLoading={games.loading} catalogError={games.error}
      bggAvailable={bggAvailable} selected={chosen} onSelect={chooseGame}
      hint="Название, ссылка или ID в BGG" />
      {changingGame && <button type="button" onClick={closePicker}>Оставить выбранную игру</button>}
    </section>}
    <section className="content-section gathering-create-section form-grid"><h2>Когда и с кем играем</h2>
      <DateTimeField label="Дата и время" hint={`${community.timeZoneId} · ${community.mode === "Camp" ? "Можно выбрать только дату кэмпа" : "Прошедшее время выбрать нельзя"}`} min={dateBounds.min} max={dateBounds.max} value={starts} onChange={setStarts} required disabled={busy} />
      <GatheringPlayerLimits range={chosenPlayers} value={{ minimum, desired, maximum }} onChange={changeLimits} disabled={!chosen || busy} />
      {(chosen?.playerRangeDefaulted || chosen && chosenPlayers.wasDefaulted) && <Notice kind="warning">В BGG нет полного диапазона игроков. Проверьте значения 1–12.</Notice>}
    </section>
    {chosen && <details className="content-section gathering-game-options"><summary>Дополнения и коробка{expansions.length ? ` · выбрано дополнений: ${expansions.length}` : ""}</summary>
      <GameProviderNotice disabled={busy} mode={community.mode} communityKey={community.key} bggId={chosen.bggId} startsAtLocal={starts} ownership={source === "bgg" || community.mode === "Camp" ? { gameName: chosen.name, canAdd: source === "bgg", add: addToCollection, bring: bringToCamp, camp: community.mode === "Camp", setAdd: value => { setAddToCollection(value); if (!value) setBringToCamp(false); }, setBring: setBringToCamp } : undefined} />
      {expansionLookup.notice}<GatheringExpansionPicker expansions={expansionLookup.expansions} selected={expansions} onChange={changeExpansions} ownership={expansionOwnership} onOwnership={setExpansionOwnership} camp={community.mode === "Camp"} disabled={busy} />
      <GameMeta game={chosen} />
    </details>}
    <section className="content-section gathering-create-section form-grid"><Field label="Описание"><textarea disabled={busy} value={description} maxLength={300} placeholder="Необязательно" onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" disabled={busy} checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label></section>
    {error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Изменить дни участия</button>}</Notice>}
    <button className="ghost" type="button" disabled={busy} onClick={() => void discard()}>Удалить черновик</button>
    <div className="gathering-create-submit sticky-action"><p>{starts ? formatLocalDateTime(starts) : "Выберите дату и время"} · {desired} игроков, считая вас</p><button className="primary" disabled={busy || !chosen || changingGame} onClick={() => void submit()}>{busy ? "Создаём…" : "Создать сбор"}</button></div>
  </Page></div>;
}

export function GatheringDetails({ community, id, onBack, onCancelled, editRegistration, openCollection, onCopy, readOnly = false }: { onCopy?: (value: GatheringDetail) => void; community: Community; id: string; onBack: () => void; onCancelled: () => void; editRegistration: () => void; openCollection: (bggId: number) => void; readOnly?: boolean }) {
  const state = useAsync(() => api<GatheringDetail>(`/gatherings/${id}?community=${encodeURIComponent(community.key)}`), [community.key, id]);
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [attendanceRequired, setAttendanceRequired] = useState(false); const [editing, setEditing] = useState(false); const [cancelling, setCancelling] = useState(false); const [cancellationReason, setCancellationReason] = useState("");
  const [recruitmentMessage, setRecruitmentMessage] = useState<string>();
  async function requestRecruitment() { if (busy) return; setBusy(true); setError(undefined); try { const result = await api<{ message: string }>(`/gatherings/${id}/recruitment`, json("POST", { communityKey: community.key })); setRecruitmentMessage(result.message); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  const [guestName, setGuestName] = useState("");
  async function action(path: string, reason?: string) { if (busy) return; setBusy(true); setError(undefined); setAttendanceRequired(false); try { await gatheringMutation(`/gatherings/${id}/${path}`, json("POST", { communityKey: community.key, reason })); telegram.success(({ join: "Вы записались на сбор", leave: "Вы вышли из сбора", close: "Запись закрыта", reopen: "Запись открыта", cancel: "Сбор отменён", "publication/retry": "Объявление опубликовано" } as Record<string, string>)[path] ?? "Изменения сохранены"); setCancelling(false); if (path === "cancel") onCancelled(); else state.reload(); } catch (e) { setAttendanceRequired(e instanceof ApiError && e.code === "camp_attendance_date_required"); setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function guestAction(method: "POST" | "PUT" | "DELETE", guestId?: number, displayName?: string) { if (busy) return false; setBusy(true); setError(undefined); try { await api(`/gatherings/${id}/guests${guestId ? `/${guestId}` : ""}`, json(method, { communityKey: community.key, displayName })); telegram.success(method === "DELETE" ? "Гость удалён" : guestId ? "Имя гостя изменено" : "Гость добавлен"); if (method === "POST") setGuestName(""); state.reload(); return true; } catch (e) { setError(e instanceof Error ? e.message : String(e)); return false; } finally { setBusy(false); } }
  if (state.loading && !state.data) return <Page title="Сбор"><Loading /></Page>; if (state.error || !state.data) return <Page title="Сбор" actions={<BackButton onClick={onBack} />}><ErrorState message={state.error ?? "Сбор не найден"} retry={state.reload} /></Page>;
  const value = state.data;
  const working = busy || state.loading;
  if (editing && value.canEdit) return <EditGatheringLoader community={community} id={id} done={() => { setEditing(false); state.reload(); }} cancel={() => setEditing(false)} />;
  const organizer = value.confirmedParticipants.find(participant => participant.isOrganizer);
  const freeSeats = Math.max(0, value.maximumPlayers - value.gathering.occupiedSeats);
  const occupiedPercent = Math.min(100, Math.round(value.gathering.occupiedSeats / value.maximumPlayers * 100));
  const statusTone = gatheringStatusTone(value.status);
  const typeNames = value.gathering.typeNames?.length
    ? value.gathering.typeNames
    : value.gathering.typeName ? [value.gathering.typeName] : [];
  const canManage = (!readOnly && (value.canEdit || value.canClose || value.canReopen || value.canCancel || value.canRequestRecruitment)) || value.canRetryPublication;
  return <div className="gathering-detail-screen"><Page>
    {!telegram.hasBackButton && <div className="gathering-detail-nav"><BackButton onClick={onBack} /></div>}
    <Card className="gathering-overview">
      <div className="gathering-overview-top">
        <p className="gathering-date"><GameFact kind="date" label="Начало">{value.startsAtLocal ? formatLocalDateTime(value.startsAtLocal) : value.gathering.localDateTime}</GameFact></p>
        <ComplexityBadge info={value.gathering.complexityInfo} />
      </div>
      <div className="gathering-detail-hero">
        <Cover src={value.gathering.imageUrl} name={value.gathering.gameName} />
        <header className="gathering-overview-header">
          <h1>{value.gathering.bggUrl ? <a className="page-title-link" href={value.gathering.bggUrl} target="_blank" rel="noreferrer">{value.gathering.gameName}</a> : value.gathering.gameName}</h1>
          <p className="gathering-organizer"><span className="muted">Собирает</span> <ContactLink url={organizer?.contactUrl}>{organizer?.name ?? value.gathering.organizerName}</ContactLink></p>
        </header>
      </div>
      <div className="gathering-summary">
        <GameFacts players={<strong>{value.gathering.occupiedSeats} из {value.maximumPlayers}</strong>} playersLabel="Занято мест" duration={formatDuration(value.gathering.minPlayTimeMinutes, value.gathering.maxPlayTimeMinutes)} />
        <div className={`seat-meter ${statusTone}`} role="progressbar" aria-label="Занятые места" aria-valuemin={0} aria-valuemax={value.maximumPlayers} aria-valuenow={value.gathering.occupiedSeats}><span style={{ width: `${occupiedPercent}%` }} /></div>
        <p className={`gathering-seat-status ${statusTone === "danger" ? "danger-text" : "muted"}`}>{value.gathering.statusText}</p>
        {value.gathering.recruitment?.belowDesired && value.status === "Ready" && <p className="gathering-recruitment-summary">{value.gathering.recruitment.text}</p>}
        <p className="capacity-caption">Для начала: {plural(value.minimumPlayers, "игрок", "игрока", "игроков")}{value.desiredPlayers !== value.minimumPlayers && <> · собираем {value.desiredPlayers}</>}</p>
      </div>
      {value.gathering.rulesText && <p className="gathering-rules"><GameFact kind="rules" label="Правила">{value.gathering.rulesText}</GameFact></p>}
      {community.mode === "Camp" && !value.hasStarted && value.status !== "Cancelled" && value.provider?.warning && <p className="gathering-box-warning"><GameFact kind="box" label="Коробка">{value.provider.warning}</GameFact></p>}
      {!readOnly && (value.canJoin || value.canLeave) && <div className="gathering-participation" aria-label="Участие в сборе">
        {value.canJoin && <button className="primary" disabled={working} onClick={() => action("join")}>{busy ? "Сохраняем…" : freeSeats > 0 ? "Занять место" : "Встать в лист ожидания"}</button>}
        {value.canLeave && <><Badge tone={participationStatusTone(value.currentUserStatus)}>{value.currentUserStatus === "Waitlisted" ? `Ваше место в очереди: ${value.waitlistPosition ?? "—"}` : "Вы записаны на сбор"}</Badge><button className="danger" disabled={working} onClick={() => action("leave")}>{value.currentUserStatus === "Waitlisted" ? "Выйти из листа ожидания" : "Отказаться от места"}</button></>}
      </div>}
      {value.gathering.cancellationReason && <Notice kind="danger">Причина отмены: {value.gathering.cancellationReason}</Notice>}
      {community.mode === "Club" && value.provider && <p className="gathering-box-status"><GameFact kind="box" label="Коробка">{value.provider.summary}</GameFact></p>}
      {value.gathering.expansions.length > 0 && <div className="gathering-expansions"><strong>Дополнения</strong><div className="tag-list">{value.gathering.expansions.map(name => <span className="tag" key={name}>{name}</span>)}</div></div>}
      {value.gathering.description && <section className="gathering-description"><h2>От организатора</h2><div className="gathering-description-scroll">{value.gathering.description}</div></section>}
    </Card>
    <div className="gathering-feedback">
    {!readOnly && <BotStartNotice required={value.botStartRequired} startUrl={value.startUrl} refresh={state.reload} />}
    {error && <Notice kind="danger"><p>{error}</p>{attendanceRequired && <button onClick={editRegistration}>Изменить дни участия</button>}</Notice>}
    </div>
    {value.canCopy && onCopy && <button onClick={() => onCopy(value)}>Повторить сбор</button>}
    {value.hasStarted && <div className="gathering-context-note"><Notice>Сбор уже начался. Запись и изменение времени закрыты.</Notice></div>}
    {canManage && <Card className="gathering-management">
      <details open={["Failed", "DeliveryUnknown"].includes(value.publicationStatus) ? true : undefined}>
        <summary><span><strong>Управление сбором</strong><small>{["Failed", "DeliveryUnknown"].includes(value.publicationStatus) ? "Объявление требует внимания" : "Настройки, запись и приглашение игроков"}</small></span></summary>
        <div className="gathering-management-content">
          {!readOnly && (value.canEdit || value.canClose || value.canReopen) && <div className="gathering-management-buttons">
            {value.canEdit && <button disabled={working} onClick={() => setEditing(true)}>Изменить сбор</button>}
            {value.canClose && <button disabled={working} onClick={() => action("close")}>Закрыть запись</button>}
            {value.canReopen && <button disabled={working} onClick={() => action("reopen")}>Открыть запись</button>}
          </div>}
          {value.publicationStatus === "Failed" && <Notice kind="danger"><p>Объявление в Telegram не удалось обновить. Оно может показывать прежние данные.</p>{value.canRetryPublication && <button disabled={working} onClick={() => action("publication/retry")}>Повторить обновление</button>}</Notice>}
          {value.publicationStatus === "DeliveryUnknown" && <Notice kind="warning">Проверьте объявление в группе: Telegram не подтвердил отправку. Автоматического повтора не будет.</Notice>}
          {["Pending", "Preparing", "Delivering"].includes(value.publicationStatus) && <Notice>Объявление ожидает публикации или обновляется. <button className="ghost" onClick={state.reload}>Обновить статус</button></Notice>}
          {!readOnly && (value.canRequestRecruitment || recruitmentMessage || value.recruitmentDelivery) && <section className="gathering-recruitment-action">
            <h3>Пригласить игроков</h3>
            {value.canRequestRecruitment && <><p className="muted">В группу придёт список ближайших сборов, которым нужны игроки.</p><button disabled={working} onClick={() => void requestRecruitment()}>Напомнить о сборе</button></>}
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
      <div className="row gathering-section-heading"><h2>Кто играет</h2><span className="muted">{value.gathering.occupiedSeats} / {value.maximumPlayers}</span></div>
      <ul className="participant-roster gathering-roster">
        {value.confirmedParticipants.map((participant, index) => <li key={participant.id ?? `${participant.name}-${index}`}><span className={`participant-marker ${participant.isOrganizer ? "organizer" : "confirmed"}`} aria-hidden>{participant.isOrganizer ? "★" : index + 1}</span><span><ContactLink url={participant.contactUrl}>{participant.name}</ContactLink>{participant.isOrganizer && <small>Организатор</small>}</span></li>)}
        {value.guestParticipants.map(guest => <GuestRow key={guest.id} name={guest.displayName} editable={!readOnly && value.canManageGuests} busy={working} rename={name => guestAction("PUT", guest.id, name)} remove={() => guestAction("DELETE", guest.id)} />)}
      </ul>
      {!readOnly && value.canManageGuests && <div className="inline-form guest-form"><input value={guestName} maxLength={80} placeholder="Имя или описание гостя" onChange={event => setGuestName(event.target.value)} /><button disabled={working || !guestName.trim()} onClick={() => guestAction("POST", undefined, guestName)}>Добавить гостя</button></div>}
      {value.waitlistedParticipants.length > 0 && <section className="gathering-waitlist"><h3>Лист ожидания <span>{value.waitlistedParticipants.length}</span></h3><ol>{value.waitlistedParticipants.map(participant => <li key={participant.id ?? `${participant.position}-${participant.name}`}><span>{participant.position}</span><ContactLink url={participant.contactUrl}>{participant.name}</ContactLink></li>)}</ol></section>}
    </section>
    {community.mode === "Camp" && value.provider && (value.provider.providers.length > 0 || !readOnly && value.provider.canBring && !value.hasStarted) && <section className="content-section gathering-provider"><h2>Коробка</h2><Notice kind={value.provider.isConfirmed ? "success" : "warning"}>{value.provider.providers.length ? value.provider.providers.map(p => <div key={p.participantId}>{p.displayName} — <span className={`provider-status${p.commitment === "Bringing" ? " success" : ""}`}>{p.commitment === "Bringing" ? "привезёт" : "может привезти"}</span></div>) : value.provider.summary}</Notice>{!readOnly && value.provider.canBring && !value.hasStarted && <button className="primary" disabled={working} onClick={() => action("bring")}>Я привезу</button>}</section>}
    {value.canRecordPlay && <div className="gathering-play-section"><PlayPanel community={community} id={id} onSaved={state.reload} /></div>}
    <Card className="gathering-game-info">
      <details>
        <summary>Об игре</summary>
        <ComplexityDetails info={value.gathering.complexityInfo} />
        <GameTaxonomy className="gathering-taxonomy" typeNames={typeNames} categoryNames={value.gathering.categoryNames} mechanicNames={value.gathering.mechanicNames} />
      </details>
      {!readOnly && value.gathering.bggId && <div className="gathering-game-actions">
        <WishButton key={value.gathering.bggId} communityKey={community.key} bggId={value.gathering.bggId} />
        <GatheringCollectionAction bggId={value.gathering.bggId} open={openCollection} />
      </div>}
    </Card>
  </Page></div>;
}

function EditGatheringLoader({ community, id, done, cancel }: { community: Community; id: string; done: () => void; cancel: () => void }) {
  const state = useAsync(() => api<GatheringDetail>(`/gatherings/${id}?community=${encodeURIComponent(community.key)}&forEdit=true`), [community.key, id]);
  if (state.loading) return <Loading />;
  if (state.error || !state.data) return <Page title="Изменить сбор" actions={<BackButton onClick={cancel} />}><ErrorState message={state.error ?? "Сбор не найден"} retry={state.reload} /></Page>;
  return <EditGathering community={community} id={id} value={state.data} done={done} cancel={cancel} />;
}

export function EditGathering({ community, id, value, done, cancel }: { community: Community; id: string; value: GatheringDetail; done: () => void; cancel: () => void }) {
  const [expansionOwnership, setExpansionOwnership] = useState(emptyExpansionOwnership);
  const [names, setNames] = useState<Record<string, string | null>>({});
  const [starts, setStarts] = useState(value.startsAtLocal); const [minimum, setMinimum] = useState(value.minimumPlayers); const [desired, setDesired] = useState(value.desiredPlayers); const [maximum, setMaximum] = useState(value.maximumPlayers); const [description, setDescription] = useState(value.description ?? ""); const [teach, setTeach] = useState(value.canTeachRules); const [selected, setSelected] = useState(value.selectedExpansionIds); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const expansionLookup = useGatheringExpansions(value.gathering?.bggId, value.knownExpansions);
  const gamePlayers = resolvePlayerCountRange(value.gameBaseMinimumPlayers ?? value.gameMinimumPlayers, value.gameBaseMaximumPlayers ?? value.gameMaximumPlayers, expansionLookup.expansions, selected);
  function changeLimits(value: PlayerLimits) { setMinimum(value.minimum); setDesired(value.desired); setMaximum(value.maximum); }
  function changeExpansions(ids: number[]) {
    changeLimits(fitPlayerLimits({ minimum, desired, maximum }, gamePlayers,
      resolvePlayerCountRange(value.gameBaseMinimumPlayers ?? value.gameMinimumPlayers,
        value.gameBaseMaximumPlayers ?? value.gameMaximumPlayers, expansionLookup.expansions, ids)));
    setSelected(ids);
  }
  const dateBounds = gatheringDateTimeBounds(community, currentLocalMinute(community.timeZoneId));
  const invalidCampDate = !isWithinCampDateRange(starts, community);
  async function save() { if (busy) return; if (invalidCampDate) { setError("Дата сбора должна быть в пределах дат кэмпа."); return; } if (!isFutureLocalDateTime(starts, community.timeZoneId)) { setError("Выберите дату и время в будущем."); return; } setBusy(true); setError(undefined); try { await gatheringMutation(`/gatherings/${id}`, json("PUT", { communityKey: community.key, startsAtLocal: starts, minimumPlayers: minimum, desiredPlayers: desired, maximumPlayers: maximum, description, canTeachRules: teach, selectedExpansionIds: selected, addExpansionToCollectionIds: expansionOwnership.add, bringExpansionIds: expansionOwnership.bring, participantNames: Object.entries(names).map(([participantId, displayNameOverride]) => ({ participantId, displayNameOverride })) })); telegram.success("Сбор обновлён"); done(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  return <div className="gathering-form-screen"><Page title="Изменить сбор" actions={<button className="ghost" onClick={cancel}>Отмена</button>}><section className="content-section form-grid"><DateTimeField label="Дата и время" hint={`${community.timeZoneId} · ${community.mode === "Camp" ? "Можно выбрать только дату кэмпа" : "Прошедшее время выбрать нельзя"}`} min={dateBounds.min} max={dateBounds.max} value={starts} onChange={setStarts} required disabled={busy} />{invalidCampDate && <Notice kind="warning">Дата сбора вне дат кэмпа. Выберите другую дату.</Notice>}<GatheringPlayerLimits range={gamePlayers} value={{ minimum, desired, maximum }} onChange={changeLimits} />{(value.gamePlayerRangeDefaulted || gamePlayers.wasDefaulted) && <Notice kind="warning">В BGG не указан полный диапазон игроков, поэтому мы поставили 1–12.</Notice>}<Field label="Описание"><textarea maxLength={300} value={description} onChange={e => setDescription(e.target.value)} /></Field><label className="check"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} />Могу объяснить правила</label>{expansionLookup.notice}<GatheringExpansionPicker expansions={expansionLookup.expansions} selected={selected} onChange={changeExpansions} ownership={expansionOwnership} onOwnership={setExpansionOwnership} camp={community.mode === "Camp"} disabled={busy} /><fieldset className="participant-name-roster form-grid"><legend>Имена в этой партии</legend><small>Имя изменится только в этом сборе.</small>{[...value.confirmedParticipants, ...value.waitlistedParticipants].filter(p => p.id).map(p => <ParticipantNameEditor key={p.id} originalName={p.originalName ?? p.name} displayNameOverride={Object.hasOwn(names, p.id!) ? names[p.id!] : p.displayNameOverride} disabled={busy || p.canRename === false} onChange={name => setNames(old => ({ ...old, [p.id!]: name }))} />)}</fieldset>{error && <Notice kind="danger">{error}</Notice>}<button className="primary" disabled={busy || invalidCampDate} onClick={save}>{busy ? "Сохраняем…" : "Сохранить"}</button></section></Page></div>;
}
