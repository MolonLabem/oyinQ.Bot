import { type ReactNode, useEffect, useMemo, useState } from "react";
import { api, json } from "../../api/client";
import type { CampImport, Community, Contribution, ImportDraftItem } from "../../api/types";
import { Badge, Card, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { formatDate } from "../../app/format";

type RegistrationState = { campStatus: string; startDate?: string; endDate?: string; registration?: { daysStaying: number; needsAccommodation: boolean } };

export function MyGamesPage({ community, bggAvailable }: { community: Community; bggAvailable: boolean }) {
  return <Contributions community={community} bggAvailable={bggAvailable} />;
}

export function CampRegistrationGate({ community, isAdministrator, children }: { community: Community; isAdministrator: boolean; children: ReactNode }) {
  const registration = useAsync(() => api<RegistrationState>(`/camp/registration?community=${encodeURIComponent(community.key)}`), [community.key]);
  if (registration.loading) return <Page title="Кэмп"><Loading /></Page>;
  if (registration.error) return <Page title="Кэмп"><ErrorState message={registration.error} retry={registration.reload} /></Page>;
  if (!registration.data?.startDate || !registration.data.endDate) return <Page title="Кэмп ещё настраивается" subtitle={community.name}><Notice kind="warning">Организатор ещё не указал даты кэмпа. Регистрация и создание сборов станут доступны после настройки.</Notice>{isAdministrator && <a className="button primary-link" href="?admin=1">Указать даты в админ-панели</a>}</Page>;
  if (!registration.data.registration) return <Registration community={community} state={registration.data} done={registration.reload} />;
  return <>{children}</>;
}

function Registration({ community, state, done }: { community: Community; state: RegistrationState; done: () => void }) {
  const duration = state.startDate && state.endDate ? Math.round((Date.parse(state.endDate) - Date.parse(state.startDate)) / 86400000) + 1 : 1;
  const [days, setDays] = useState(duration); const [housing, setHousing] = useState(false); const [name, setName] = useState(""); const [error, setError] = useState<string>(); const [busy, setBusy] = useState(false);
  async function submit() { setBusy(true); try { await api("/camp/registration", json("PUT", { communityKey: community.key, daysStaying: days, needsAccommodation: housing, displayName: name })); telegram.success("Регистрация сохранена"); done(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  return <Page title="Регистрация" subtitle={`${formatDate(state.startDate)} — ${formatDate(state.endDate)}`}><Card><Field label="Имя для участников"><input value={name} onChange={e => setName(e.target.value)} /></Field><Field label="Сколько дней"><input type="number" min="1" max={duration} value={days} onChange={e => setDays(+e.target.value)} /></Field><label className="check"><input type="checkbox" checked={housing} onChange={e => setHousing(e.target.checked)} />Нужно жильё</label>{error && <Notice kind="danger">{error}</Notice>}<button className="primary" disabled={busy || state.campStatus !== "Active"} onClick={submit}>Зарегистрироваться</button></Card></Page>;
}

function Contributions({ community, bggAvailable }: { community: Community; bggAvailable: boolean }) {
  const state = useAsync(() => api<Contribution[]>(`/camp/contributions?community=${encodeURIComponent(community.key)}`), [community.key]);
  const storageKey = `oyinq-camp-import-${community.key}`;
  const [importId, setImportId] = useState<string | undefined>(() => localStorage.getItem(storageKey) ?? undefined); const [manual, setManual] = useState(""); const [error, setError] = useState<string>();
  useEffect(() => telegram.back(Boolean(importId), () => { localStorage.removeItem(storageKey); setImportId(undefined); state.reload(); }), [importId]);
  async function startImport(input: string) { setError(undefined); try { const result = await api<{ publicId: string }>("/camp/imports", json("POST", { communityKey: community.key, bggInput: input })); localStorage.setItem(storageKey, result.publicId); setImportId(result.publicId); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } }
  async function addManual() { try { await api("/camp/contributions/manual", json("POST", { communityKey: community.key, bggInput: manual, expansionBggIds: [] })); setManual(""); telegram.success("Игра добавлена"); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } }
  async function remove(item: Contribution) { if (!await telegram.confirm(`Убрать «${item.snapshot.name}»?`)) return; setError(undefined); try { await api(`/camp/contributions/${item.itemType}/${item.bggId}?community=${encodeURIComponent(community.key)}`, { method: "DELETE" }); telegram.success("Игра удалена"); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } }
  if (importId) return <ImportProgress community={community} id={importId} close={() => { localStorage.removeItem(storageKey); setImportId(undefined); state.reload(); }} />;
  return <Page title="Мои игры" actions={<BggImportButton available={bggAvailable} start={startImport} />}>
    {!bggAvailable && <Notice kind="warning">BGG временно недоступен. Уже сохранённые игры можно просматривать и удалять.</Notice>}
    <Card><h2>Добавить вручную</h2><Field label="Ссылка BGG"><input disabled={!bggAvailable} value={manual} onChange={e => setManual(e.target.value)} placeholder="Ссылка на игру" /></Field><button disabled={!bggAvailable || !manual} onClick={addManual}>Добавить</button></Card>
    {error && <Notice kind="danger">{error}</Notice>}{state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : !state.data?.length ? <Empty>Вы пока не добавили игры.</Empty> : <div className="stack">{state.data.map(item => <Card key={`${item.itemType}-${item.bggId}`}><div className="row"><div><h2>{item.snapshot.name}</h2><Badge tone={item.source === "Manual" ? "accent" : "neutral"}>{item.source === "BggImport" ? "BGG" : item.source === "Manual" ? "Вручную" : "Перенесено"}</Badge>{item.itemType === "Expansion" && <span className="muted"> Дополнение</span>}</div><button className="danger ghost" onClick={() => remove(item)}>Убрать</button></div></Card>)}</div>}
  </Page>;
}

function BggImportButton({ available, start }: { available: boolean; start: (input: string) => void }) {
  const [open, setOpen] = useState(false); const [input, setInput] = useState("");
  return open ? <div className="inline-form"><input value={input} onChange={e => setInput(e.target.value)} placeholder="Имя пользователя BGG" aria-label="Имя пользователя BGG" /><button disabled={!input.trim()} onClick={() => start(input)}>Начать</button></div> : <button className="primary" disabled={!available} onClick={() => setOpen(true)}>Импорт BGG</button>;
}

function ImportProgress({ community, id, close }: { community: Community; id: string; close: () => void }) {
  const state = useAsync(() => api<CampImport>(`/camp/imports/${id}?community=${encodeURIComponent(community.key)}`), [community.key, id]);
  const [actionError, setActionError] = useState<string>();
  useEffect(() => { if (!state.data || ["Queued", "Running"].includes(state.data.status)) { const timer = window.setInterval(state.reload, 3000); return () => clearInterval(timer); } }, [state.data?.status, state.reload]);
  async function cancel() { if (!await telegram.confirm("Отменить импорт? Выбранные игры не будут сохранены.")) return; setActionError(undefined); try { await api(`/camp/imports/${id}/cancel`, json("POST", { communityKey: community.key })); telegram.success("Импорт отменён"); close(); } catch (e) { setActionError(e instanceof Error ? e.message : String(e)); } }
  if (state.loading && !state.data) return <Page title="Импорт BGG"><Loading label="BGG готовит коллекцию. Можно закрыть и вернуться позже." /></Page>;
  if (state.error) return <Page title="Импорт BGG" actions={<button onClick={close}>Закрыть</button>}><ErrorState message={state.error} retry={state.reload} /></Page>;
  if (!state.data) return null;
  if (state.data.status === "Failed") return <Page title="Импорт BGG"><Notice kind="danger">{state.data.error}</Notice><button onClick={() => api(`/camp/imports/${id}/retry`, json("POST", { communityKey: community.key })).then(state.reload)}>Повторить</button><button onClick={close}>Закрыть</button></Page>;
  if (state.data.status === "Cancelled") return <Page title="Импорт отменён"><Notice>Черновик не был применён.</Notice><button onClick={close}>К моим играм</button></Page>;
  if (state.data.status !== "Completed" && state.data.status !== "Confirmed") return <Page title="Импорт BGG" subtitle={state.data.progressTotal ? `Этап ${state.data.progressCurrent} из ${state.data.progressTotal}` : "В очереди"}><Loading label="Импорт выполняется в фоне. Эту страницу можно закрыть." />{actionError && <Notice kind="danger">{actionError}</Notice>}<div className="row"><button onClick={close}>Продолжить позже</button><button className="danger ghost" onClick={cancel}>Отменить импорт</button></div></Page>;
  if (state.data.status === "Confirmed") return <Page title="Импорт завершён"><Notice kind="success">Выбранные игры сохранены.</Notice><button onClick={close}>К моим играм</button></Page>;
  return <ImportSelection community={community} id={id} items={state.data.draft?.items ?? []} done={close} cancel={cancel} cancellationError={actionError} />;
}

function ImportSelection({ community, id, items, done, cancel, cancellationError }: { community: Community; id: string; items: ImportDraftItem[]; done: () => void; cancel: () => void; cancellationError?: string }) {
  const [selected, setSelected] = useState(() => new Set(items.filter(x => x.selectedByDefault).map(x => `${x.itemType}-${x.bggId}`)));
  const [query, setQuery] = useState(""); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const isExpansion = (item: ImportDraftItem) => item.itemType === "Expansion";
  const normalizedQuery = query.trim().toLowerCase();
  const expansions = useMemo(() => items.filter(isExpansion), [items]);
  const bases = useMemo(() => items.filter(x => !isExpansion(x) && (!normalizedQuery || x.snapshot.name.toLowerCase().includes(normalizedQuery) || expansions.some(exp => exp.parentBggId === x.bggId && exp.snapshot.name.toLowerCase().includes(normalizedQuery)))), [items, expansions, normalizedQuery]);
  const orphans = useMemo(() => expansions.filter(exp => !items.some(base => !isExpansion(base) && base.bggId === exp.parentBggId) && (!normalizedQuery || exp.snapshot.name.toLowerCase().includes(normalizedQuery))), [items, expansions, normalizedQuery]);
  const toggle = (item: ImportDraftItem) => setSelected(current => { const next = new Set(current); const key = `${item.itemType}-${item.bggId}`; next.has(key) ? next.delete(key) : next.add(key); return next; });
  const hasBase = (item: ImportDraftItem) => !item.parentBggId || items.some(base => !isExpansion(base) && base.bggId === item.parentBggId && selected.has(`${base.itemType}-${base.bggId}`));
  async function confirm() { setBusy(true); try { await api(`/camp/imports/${id}/confirm`, json("POST", { communityKey: community.key, selectedBaseGameIds: items.filter(x => !isExpansion(x) && selected.has(`${x.itemType}-${x.bggId}`)).map(x => x.bggId), selectedExpansionIds: items.filter(x => isExpansion(x) && selected.has(`${x.itemType}-${x.bggId}`)).map(x => x.bggId) })); telegram.success("Игры добавлены"); done(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  const hasMatches = bases.length > 0 || orphans.length > 0;
  return <Page title="Выберите игры" subtitle={`Выбрано ${selected.size} из ${items.length}`} actions={<button className="danger ghost" onClick={cancel}>Отменить импорт</button>}><div className="row"><button onClick={() => setSelected(new Set(items.map(x => `${x.itemType}-${x.bggId}`)))}>Выбрать все</button><button onClick={() => setSelected(new Set())}>Очистить</button></div><Field label="Поиск"><input type="search" value={query} onChange={e => setQuery(e.target.value)} placeholder="Игра или дополнение" /></Field>{!hasMatches ? <Empty>Ничего не найдено.</Empty> : <div className="import-groups">{bases.map(base => { const children = expansions.filter(x => x.parentBggId === base.bggId && (!normalizedQuery || base.snapshot.name.toLowerCase().includes(normalizedQuery) || x.snapshot.name.toLowerCase().includes(normalizedQuery))); return <details open={normalizedQuery ? true : undefined} key={base.bggId}><summary><label className="check"><input type="checkbox" checked={selected.has(`${base.itemType}-${base.bggId}`)} onChange={() => toggle(base)} />{base.snapshot.name}</label></summary>{children.map(exp => <label className={`check nested ${selected.has(`${exp.itemType}-${exp.bggId}`) && !hasBase(exp) ? "missing-base" : ""}`} key={exp.bggId}><input type="checkbox" checked={selected.has(`${exp.itemType}-${exp.bggId}`)} onChange={() => toggle(exp)} />{exp.snapshot.name}{selected.has(`${exp.itemType}-${exp.bggId}`) && !hasBase(exp) && <span> — нет базовой игры</span>}</label>)}</details>; })}{orphans.length > 0 && <section className="notice warning"><strong>Дополнения без базовой игры</strong><p className="muted">Это допустимо: их можно привезти отдельно.</p>{orphans.map(exp => <label className={`check nested ${selected.has(`${exp.itemType}-${exp.bggId}`) ? "missing-base" : ""}`} key={exp.bggId}><input type="checkbox" checked={selected.has(`${exp.itemType}-${exp.bggId}`)} onChange={() => toggle(exp)} />{exp.snapshot.name}{selected.has(`${exp.itemType}-${exp.bggId}`) && <span> — базовой игры нет в вашей коллекции</span>}</label>)}</section>}</div>}{(error || cancellationError) && <Notice kind="danger">{error ?? cancellationError}</Notice>}<button className="primary sticky-action" disabled={busy} onClick={confirm}>{busy ? "Сохраняем…" : `Сохранить (${selected.size})`}</button></Page>;
}
