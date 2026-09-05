import { groupCollectionItems } from "../../app/collectionGroups";
import { type ReactNode, useEffect, useMemo, useState } from "react";
import { ApiError, api, json } from "../../api/client";
import type { CampImport, ClubGame, Community, Contribution, PersonalCollectionItem, ImportDraftItem } from "../../api/types";
import { Card, Cover, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { GameMeta, GamePicker, searchGames } from "../../components/GamePicker";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { formatDate, formatInstant, plural } from "../../app/format";
import { bggImportProgressText } from "../../app/bggImportProgress";
import { defaultImportSelection, expansionBelongsToBase, importItemKey, importParentIds, isImportItemSelectable } from "../camp/importSelection";

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
  async function submit() { if (busy) return; setValidated(true); if (!city.trim() || selectedDates.length === 0) return; setBusy(true); setError(undefined); try { await save(); telegram.success("Регистрация сохранена"); done(); } catch (e) { if (e instanceof ApiError && e.code === "registration_dates_affect_gatherings" && e.affectedGatherings?.length) { const summary = e.affectedGatherings.map(x => `• ${x.gameName}, ${formatInstant(x.startsAtUtc, community.timeZoneId)}`).join("\n"); if (await telegram.confirm(`Вы больше не сможете участвовать в этих сборах:\n${summary}\n\nПродолжить?`)) { try { await save(true); telegram.success("Регистрация обновлена"); done(); return; } catch (retry) { setError(retry instanceof Error ? retry.message : String(retry)); } } } else setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function unregister() { if (busy || !await telegram.confirm("Отменить регистрацию?\n\nОтметки доступности в этом кэмпе будут удалены. Личная коллекция сохранится. Вы выйдете из будущих сборов, а история прошедших сборов сохранится.")) return; setBusy(true); setError(undefined); try { await api("/camp/registration/unregister", json("POST", { communityKey: community.key })); telegram.success("Регистрация отменена"); done(); } catch (e) { if (e instanceof ApiError && e.code === "registration_organizer_conflict" && e.affectedGatherings?.length) setError(`Сначала отмените свои будущие сборы:\n${e.affectedGatherings.map(item => `• ${item.gameName} · ${formatInstant(item.startsAtUtc, community.timeZoneId)}`).join("\n")}`); else setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  return <Page as="section" title={state.registration?.registered ? "Редактировать регистрацию" : "Регистрация на кэмп"} subtitle={`${formatDate(state.startDate)} — ${formatDate(state.endDate)}`} actions={cancel && <button onClick={cancel}>Назад</button>}><Card className="form-grid"><Field label="Имя" hint="Так вас увидят в списке участников и среди владельцев игр этого кэмпа."><input maxLength={128} value={name} onChange={e => setName(e.target.value)} /></Field><Field label="Город" hint="Его увидят только участники этого кэмпа" error={validated && !city.trim() ? "Укажите город." : undefined}><input required maxLength={100} value={city} onChange={e => setCity(e.target.value)} placeholder="Например, Астана" /></Field><fieldset><legend>Когда вы будете на кэмпе?</legend><p className="muted">Отметьте дни, когда будете на кэмпе. Создавать сборы и записываться можно только на выбранные дни.</p>{state.availableDates.map(date => <label className="check" key={date}><input type="checkbox" checked={selectedDates.includes(date)} onChange={() => setSelectedDates(current => current.includes(date) ? current.filter(x => x !== date) : [...current, date].sort())} />{new Date(`${date}T00:00:00`).toLocaleDateString("ru-RU", { weekday: "short", day: "numeric", month: "long" })}{state.dateLabels?.[date] ? ` · ${state.dateLabels[date]}` : ""}</label>)}{validated && selectedDates.length === 0 && <small className="field-error" role="alert">Выберите хотя бы один день.</small>}</fieldset><label className="check"><input type="checkbox" checked={housing} onChange={e => setHousing(e.target.checked)} />Нужно жильё</label>{error && <Notice kind="danger"><span className="pre-line">{error}</span></Notice>}<button className="primary" disabled={busy || state.campStatus !== "Active"} aria-busy={busy} onClick={submit}>{busy ? "Сохраняем…" : state.registration?.registered ? "Сохранить изменения" : "Зарегистрироваться"}</button></Card>{state.registration?.registered && <Card className="danger-zone"><h2>Отменить регистрацию</h2><p>Отметки доступности в этом кэмпе будут удалены. Личная коллекция сохранится, а места в будущих сборах освободятся. История сохранится.</p><button className="danger ghost" disabled={busy} onClick={unregister}>Отменить регистрацию</button></Card>}</Page>;
}

export function ProfileCollectionPage({ community, bggAvailable }: { community?: Community; bggAvailable: boolean }) {
  const state = useAsync(() => api<PersonalCollectionItem[]>("/profile/collection/"), [community?.key]);
  const campState = useAsync(() => community?.mode === "Camp" ? api<Contribution[]>(`/camp/contributions?community=${encodeURIComponent(community?.key ?? "")}`) : Promise.resolve([]), [community?.key]);
  const legacyKey = `oyinq-camp-import-${community?.key}`;
  const [legacyImport, setLegacyImport] = useState(() => !new URLSearchParams(location.search).get("profileImport") && Boolean(community && (new URLSearchParams(location.search).get("import") ?? localStorage.getItem(legacyKey))));
  const storageKey = legacyImport ? legacyKey : "oyinq-profile-import";
  const importBase = legacyImport ? "/camp" : "/profile/collection";
  const [importId, setImportId] = useState<string | undefined>(() => new URLSearchParams(location.search).get("profileImport") ?? (community ? new URLSearchParams(location.search).get("import") : null) ?? localStorage.getItem(storageKey) ?? undefined); const [chosen, setChosen] = useState<ClubGame>(); const [selectedExpansions, setSelectedExpansions] = useState<number[]>([]); const [listQuery, setListQuery] = useState(""); const [adding, setAdding] = useState(false); const [error, setError] = useState<string>();
  const collectionGroups = useMemo(() => {
    const values = state.data ?? [];
    const matches = new Set(searchGames(values.map(item => ({ bggId: item.bggId, ...item.snapshot, expansions: [] })), listQuery).map(game => game.bggId));
    return groupCollectionItems(values, item => !listQuery.trim() || matches.has(item.bggId));
  }, [state.data, listQuery]);
  useEffect(() => telegram.back(Boolean(importId), () => { setImportId(undefined); state.reload(); }), [importId]);
  async function startImport(input: string) { setError(undefined); try { const result = await api<{ publicId: string }>("/profile/collection/imports", json("POST", { communityKey: community?.key, bggInput: input })); setLegacyImport(false); localStorage.setItem("oyinq-profile-import", result.publicId); setImportId(result.publicId); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } }
  async function addManual() { if (adding || !chosen) return; setAdding(true); setError(undefined); try { await api("/profile/collection/manual", json("POST", { communityKey: community?.key, bggInput: String(chosen.bggId), expansionBggIds: selectedExpansions })); setChosen(undefined); setSelectedExpansions([]); telegram.success("Игра и данные BGG добавлены"); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setAdding(false); } }
  async function remove(item: PersonalCollectionItem) { if (!await telegram.confirm(`Убрать «${item.snapshot.name}» из вашей личной коллекции?`)) return; setError(undefined); try { await api(`/profile/collection/${item.itemType}/${item.bggId}`, { method: "DELETE" }); telegram.success("Игра удалена"); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } }
  async function commitment(item: PersonalCollectionItem, next: "Available" | "Bringing" | null) { if (!community) return;
    setError(undefined);
    try {
      if (next) await api(`/camp/contributions/${item.itemType}/${item.bggId}/commitment`, json("PUT", { communityKey: community?.key, commitment: next }));
      else await api(`/camp/contributions/${item.itemType}/${item.bggId}?community=${encodeURIComponent(community?.key ?? "")}`, { method: "DELETE" });
      telegram.success("Доступность обновлена"); campState.reload();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
  }
  const renderItem = (item: PersonalCollectionItem) => <div className="row contribution-row"><div className="media"><Cover src={item.snapshot.thumbnailImageUrl} name={item.snapshot.name} /><div><h2>{item.snapshot.name}</h2>{item.itemType === "Expansion" && <span className="muted"> Дополнение</span>}<GameMeta game={{ bggId: item.bggId, ...item.snapshot, expansions: [] }} /></div></div><div className="row">{community?.mode === "Camp" && <label>Для «{community?.name}»<select disabled={campState.loading || Boolean(campState.error)} aria-label={`Доступность ${item.snapshot.name}`} value={campState.data?.find(x => x.bggId === item.bggId && x.itemType === item.itemType)?.commitment ?? ""} onChange={e => void commitment(item, (e.target.value || null) as "Available" | "Bringing" | null)}><option value="">Не предлагаю для кэмпа</option><option value="Available">Могу привезти</option><option value="Bringing">Точно привезу</option></select></label>}<button className="danger ghost" onClick={() => remove(item)}>Убрать</button></div></div>;
  if (importId) return <ImportProgress base={importBase} community={community} id={importId} close={(preserve = false) => { if (!preserve) localStorage.removeItem(storageKey); setImportId(undefined); setLegacyImport(false); state.reload(); }} />;
  return <Page as="section" title="Моя коллекция" subtitle="Ваши игры во всех сообществах. Доступность для кэмпа отмечается отдельно.">
    {!bggAvailable && <Notice kind="warning">BGG временно недоступен. Сохранённые игры можно просматривать, отмечать и удалять.</Notice>}
    {campState.error && <Notice kind="warning">{campState.error}</Notice>}
    <section className="page-section"><h2>Добавить игры</h2><Card><h3>Импортировать коллекцию BGG</h3><p className="muted">Загрузите коллекцию из BGG и выберите игры, которыми владеете.</p><BggImportButton available={bggAvailable} start={startImport} /></Card><div className="section-divider"><span>или</span></div>
    <Card><h3>Добавить одну игру</h3><GamePicker bggAvailable={bggAvailable} selected={chosen} label="Найдите игру по названию или BGG-ссылке"
      onSelect={game => { setChosen(game); setSelectedExpansions([]); }}
      onClear={() => { setChosen(undefined); setSelectedExpansions([]); }} />
      {chosen && <div className="selected-game"><div className="media"><Cover src={chosen.thumbnailImageUrl} name={chosen.name} /><div><h3>{chosen.name}</h3><GameMeta game={chosen} /></div></div>{chosen.expansions.length > 0 && <fieldset><legend>Дополнения в вашей коллекции</legend>{chosen.expansions.map(expansion => <label className="check" key={expansion.bggId}><input type="checkbox" checked={selectedExpansions.includes(expansion.bggId)} onChange={() => setSelectedExpansions(current => current.includes(expansion.bggId) ? current.filter(id => id !== expansion.bggId) : [...current, expansion.bggId])} />{expansion.name}</label>)}</fieldset>}<button className="primary" disabled={adding} onClick={addManual}>{adding ? "Добавляем…" : "Добавить в мои игры"}</button></div>}
    </Card></section>
    <section className="page-section"><h2>Ваши игры</h2>{error && <Notice kind="danger">{error}</Notice>}{state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.length ? <Empty>Вы пока не добавили игры. Импортируйте коллекцию BGG или добавьте одну игру.</Empty> : <><Field label="Поиск среди добавленных игр"><input type="search" value={listQuery} onChange={event => setListQuery(event.target.value)} placeholder="Название игры" /></Field>{!collectionGroups.length ? <Empty>Ничего не найдено. Попробуйте сократить запрос.</Empty> : <div className="stack">{collectionGroups.map(group => <Card key={`${group.item.itemType}-${group.item.bggId}`}>
          {renderItem(group.item)}
          {group.expansions.length > 0 && <details className="collection-expansions" open={listQuery.trim() ? true : undefined}>
            <summary>Дополнения ({group.expansions.length})</summary>
            {group.expansions.map(item => <div className="collection-expansion" key={item.bggId}>{renderItem(item)}</div>)}
          </details>}
        </Card>)}</div>}</>}</section>
  </Page>;
}

function BggImportButton({ available, start }: { available: boolean; start: (input: string) => Promise<void> }) {
  const [open, setOpen] = useState(false); const [input, setInput] = useState(""); const [busy, setBusy] = useState(false);
  async function submit() { if (busy || !input.trim()) return; setBusy(true); try { await start(input); } finally { setBusy(false); } }
  return open ? <><Field label="Профиль BoardGameGeek" hint="Имя пользователя или ссылка на профиль"><input disabled={busy} value={input} onChange={e => setInput(e.target.value)} placeholder="Например, John90" /></Field><button className="primary" disabled={busy || !available || !input.trim()} aria-busy={busy} onClick={submit}>{busy ? "Запускаем импорт…" : "Импортировать коллекцию"}</button></> : <button className="primary" disabled={!available} onClick={() => setOpen(true)}>Импортировать коллекцию</button>;
}

function ImportProgress({ base, community, id, close }: { base: string; community?: Community; id: string; close: (preserve?: boolean) => void }) {
  const state = useAsync(() => api<CampImport>(`${base}/imports/${id}?community=${encodeURIComponent(community?.key ?? "")}`), [community?.key, id]);
  const [actionError, setActionError] = useState<string>();
  const [actionBusy, setActionBusy] = useState(false);
  useEffect(() => { if (!state.data || ["Queued", "Running"].includes(state.data.status)) { const timer = window.setInterval(state.reload, 3000); return () => clearInterval(timer); } }, [state.data?.status, state.reload]);
  async function cancel() { if (actionBusy || !await telegram.confirm("Отменить импорт? Выбранные игры не будут сохранены.")) return; setActionBusy(true); setActionError(undefined); try { await api(`${base}/imports/${id}/cancel`, json("POST", { communityKey: community?.key })); telegram.success("Импорт отменён"); close(); } catch (e) { setActionError(e instanceof Error ? e.message : String(e)); } finally { setActionBusy(false); } }
  async function retry() { if (actionBusy) return; setActionBusy(true); setActionError(undefined); try { await api(`${base}/imports/${id}/retry`, json("POST", { communityKey: community?.key })); state.reload(); } catch (e) { setActionError(e instanceof Error ? e.message : String(e)); } finally { setActionBusy(false); } }
  if (state.loading && !state.data) return <Page as="section" title="Импорт BGG"><Loading label="BGG готовит коллекцию. Можно закрыть и вернуться позже." /></Page>;
  if (state.error) return <Page as="section" title="Импорт BGG" actions={<button onClick={() => close()}>Закрыть</button>}><ErrorState message={state.error} retry={state.reload} /></Page>;
  if (!state.data) return null;
  if (state.data.status === "Failed") return <Page as="section" title="Импорт BGG"><Notice kind="danger">Не удалось импортировать коллекцию BGG. Сохранённые игры не изменены; попробуйте ещё раз позже.</Notice>{actionError && <Notice kind="danger">{actionError}</Notice>}<button className="primary" disabled={actionBusy} onClick={retry}>{actionBusy ? "Запускаем повтор…" : "Повторить"}</button><button disabled={actionBusy} onClick={() => close()}>К моим играм</button></Page>;
  if (state.data.status === "Cancelled") return <Page as="section" title="Импорт отменён"><Notice>Черновик не был применён.</Notice><button onClick={() => close()}>К моим играм</button></Page>;
  if (state.data.status !== "Completed" && state.data.status !== "Confirmed") return <Page as="section" title="Импорт BGG" subtitle={bggImportProgressText(state.data)}><Loading label="Импорт выполняется в фоне. Эту страницу можно закрыть." />{actionError && <Notice kind="danger">{actionError}</Notice>}<div className="row"><button disabled={actionBusy} onClick={() => close(true)}>Продолжить позже</button><button disabled={actionBusy} className="danger ghost" onClick={cancel}>{actionBusy ? "Отменяем…" : "Отменить импорт"}</button></div></Page>;
  if (state.data.status === "Confirmed") { const overridable = state.data.draft?.items.filter(item => item.skipReason === "AlreadyInBaseCollection" && item.isOverridable) ?? []; return <Page as="section" title="Импорт завершён"><Notice kind="success">Выбранные игры сохранены.</Notice>{state.data.hasSelectedOverridableItems && !state.data.overrideResolution && <BaseDuplicateResolution community={community} id={id} count={overridable.length} done={state.reload} />}{state.data.overrideResolution && <Notice>{state.data.overrideResolution === "AddPersonalCopies" ? "Ваши личные копии добавлены." : "Игры остаются только в общей коллекции кэмпа."}</Notice>}<button onClick={() => close()}>К моим играм</button></Page>; }
  return <ImportSelection base={base} community={community} id={id} items={state.data.draft?.items ?? []} foundGames={state.data.foundGames} foundExpansions={state.data.foundExpansions} done={() => close()} cancel={cancel} cancellationError={actionError} />;
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
  return <Page as="section" title="Выберите игры" subtitle={`Выбрано ${selected.size} из ${items.length}`} actions={<button className="danger ghost" onClick={cancel}>Отменить импорт</button>}><Notice kind="success">Получили коллекцию BGG — {plural(displayedGames, "игра", "игры", "игр")} · {plural(displayedExpansions, "дополнение", "дополнения", "дополнений")}.</Notice><div className="row"><button onClick={() => setSelected(new Set(items.filter(selectable).map(x => `${x.itemType}-${x.bggId}`)))}>Выбрать всё доступное</button><button onClick={() => setSelected(new Set())}>Очистить</button></div><Field label="Поиск"><input type="search" value={query} onChange={e => setQuery(e.target.value)} placeholder="Игра или дополнение" /></Field>{!hasMatches ? <Empty>Ничего не найдено.</Empty> : <div className="import-groups">{bases.map(base => { const children = expansions.filter(x => expansionBelongsToBase(x, base.bggId) && (matchesName(base.snapshot) || matchesName(x.snapshot))); return <details open={normalizedQuery ? true : undefined} key={base.bggId}><summary><label className="check"><input type="checkbox" disabled={!selectable(base)} checked={selected.has(`${base.itemType}-${base.bggId}`)} onChange={() => toggle(base)} /><ImportItemSummary item={base} /></label></summary>{children.map(exp => <label className={`check nested ${selected.has(`${exp.itemType}-${exp.bggId}`) && !hasBase(exp) ? "missing-base" : ""}`} key={exp.bggId}><input type="checkbox" disabled={!selectable(exp)} checked={selected.has(`${exp.itemType}-${exp.bggId}`)} onChange={() => toggle(exp)} /><ImportItemSummary item={exp} />{selected.has(`${exp.itemType}-${exp.bggId}`) && !hasBase(exp) && <span className="missing-base-note">Нет базовой игры</span>}</label>)}</details>; })}{orphans.length > 0 && <section className="notice warning"><strong>Дополнения без базовой игры</strong><p className="muted">Это допустимо: их можно привезти отдельно.</p>{orphans.map(exp => <label className={`check nested ${selected.has(`${exp.itemType}-${exp.bggId}`) ? "missing-base" : ""}`} key={exp.bggId}><input type="checkbox" disabled={!selectable(exp)} checked={selected.has(`${exp.itemType}-${exp.bggId}`)} onChange={() => toggle(exp)} /><ImportItemSummary item={exp} />{selected.has(`${exp.itemType}-${exp.bggId}`) && <span className="missing-base-note">Базовой игры нет в вашей коллекции</span>}</label>)}</section>}</div>}{(error || cancellationError) && <Notice kind="danger">{error ?? cancellationError}</Notice>}<button className="primary sticky-action" disabled={busy} onClick={confirm}>{busy ? "Сохраняем…" : `Сохранить (${selected.size})`}</button></Page>;
}

function ImportItemSummary({ item }: { item: ImportDraftItem }) {
  const reason = item.skipReason === "AlreadyInBaseCollection" ? "Уже есть в базовой коллекции" : item.skipReason === "AlreadyAddedManually" ? "Уже добавлено вручную" : item.skipReason ? "Не удалось добавить" : undefined;
  return <span className="import-item-summary"><Cover src={item.snapshot.thumbnailImageUrl} name={item.snapshot.name} /><span><strong>{item.snapshot.name}</strong>{reason && <small>{reason}</small>}<GameMeta game={{ bggId: item.bggId, ...item.snapshot, expansions: [] }} compact /></span></span>;
}

function BaseDuplicateResolution({ community, id, count, done }: { community?: Community; id: string; count: number; done: () => void }) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  async function resolve(resolution: "KeepBaseCollection" | "AddPersonalCopies") { if (busy) return; setBusy(true); setError(undefined); try { await api(`/camp/imports/${id}/resolve-base-duplicates`, json("POST", { communityKey: community?.key, resolution })); telegram.success(resolution === "AddPersonalCopies" ? "Личные копии добавлены" : "Оставлено без изменений"); done(); } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); } finally { setBusy(false); } }
  return <Card className="form-grid"><h2>Игры уже есть в кэмпе</h2><p>{count} игр уже доступны из базовой коллекции. Личная копия обычно не нужна, но вы можете зарегистрировать её отдельно.</p><div className="row"><button disabled={busy} onClick={() => resolve("KeepBaseCollection")}>Оставить как есть</button><button className="primary" disabled={busy} onClick={() => resolve("AddPersonalCopies")}>Добавить мои копии</button></div>{error && <Notice kind="danger">{error}</Notice>}</Card>;
}
