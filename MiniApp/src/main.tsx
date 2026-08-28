import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import "./styles.css";

type Card = { publicId: string; gameName: string; imageUrl?: string; description?: string; rulesText: string; localDateTime: string; confirmedPlayers: number; desiredPlayers: number; maximumPlayers: number; statusText: string };
type Detail = Card & { canTeachRules: boolean; organizerName: string; minimumPlayers: number; expansions: string[] };
type Game = { id: number; bggId: number; name: string; thumbnailImageUrl?: string; source: "catalog"; expansions: { bggId: number; name: string }[]; contributorCount: number };
type Community = { key: string; name: string; mode: "Club" | "Camp" };
type AdminCommunity = Community & { telegramChatId: number; timeZoneId: string; isActive: boolean };
type AdminClub = { id: number; botChatKey: string; name: string; bggUsername?: string; gameCount: number };
type Administrator = { telegramUserId: number; addedByTelegramUserId?: number; createdAt: string };
type ClubSyncItem = { bggId: number; name: string };
type ClubSyncPreview = { username: string; document: object; added: ClubSyncItem[]; removed: ClubSyncItem[]; changed: ClubSyncItem[]; orphanExpansions: (ClubSyncItem & { parentBggIds: number[] })[]; isEmpty: boolean };
type CampRegistration = { registered: boolean; daysStaying?: number; needsAccommodation?: boolean };
type ImportItem = { bggId: number; itemType: 0 | 1; parentBggId?: number; name: string; selected: boolean };
type BggDetails = { bggId: number; name: string; thumbnailImageUrl?: string; imageUrl?: string; minPlayers?: number; maxPlayers?: number; bestPlayers?: string; expansions: { bggId: number; name: string }[] };

const webApp = window.Telegram?.WebApp;
const startParam = webApp?.initDataUnsafe.start_param;
const initialCommunity = (startParam?.startsWith("community-") ? startParam.slice(10) : null)
  ?? new URLSearchParams(location.search).get("community") ?? "";
const initialGathering = new URLSearchParams(location.search).get("gathering");
const previewMode = import.meta.env.DEV && initialCommunity === "preview";
const previewImage = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='600' height='800'%3E%3Crect width='600' height='800' fill='%232b4260'/%3E%3Ccircle cx='300' cy='290' r='180' fill='%23d79b45'/%3E%3Ctext x='300' y='570' text-anchor='middle' fill='white' font-size='52' font-family='sans-serif'%3ETERRAFORMING%3C/text%3E%3Ctext x='300' y='635' text-anchor='middle' fill='white' font-size='68' font-family='sans-serif'%3EMARS%3C/text%3E%3C/svg%3E";
const previewCard: Card = { publicId: "preview", gameName: "Terraforming Mars", imageUrl: previewImage, description: "Играем с дополнением Prelude. Новичкам тоже можно, но партия будет довольно плотная.", rulesText: "Могу объяснить правила", localDateTime: "5 сентября, 19:00", confirmedPlayers: 3, desiredPlayers: 4, maximumPlayers: 5, statusText: "✅ Мест достаточно" };

async function api<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...options,
    headers: { "Content-Type": "application/json", "X-Telegram-Init-Data": webApp?.initData ?? "", ...options?.headers }
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({ message: "Не удалось выполнить запрос." }));
    throw new Error(error.message ?? "Не удалось выполнить запрос.");
  }
  return response.status === 204 ? (undefined as T) : response.json();
}

function Cover({ src, name, large = false }: { src?: string; name: string; large?: boolean }) {
  const [failed, setFailed] = useState(false);
  return src && !failed
    ? <img className={large ? "cover cover-large" : "cover"} src={src} alt={`Обложка игры ${name}`} loading="lazy" onError={() => setFailed(true)} />
    : <div className={large ? "cover placeholder cover-large" : "cover placeholder"} aria-label="Обложка отсутствует">🎲</div>;
}

function App() {
  const [community, setCommunity] = useState(initialCommunity);
  const [communities, setCommunities] = useState<Community[]>([]);
  const [adminCommunities, setAdminCommunities] = useState<AdminCommunity[] | null>(null);
  const [adminOpen, setAdminOpen] = useState(false);
  const [cards, setCards] = useState<Card[]>(previewMode ? [previewCard] : []);
  const [detail, setDetail] = useState<{ gathering: Detail; canEdit: boolean }>();
  const [games, setGames] = useState<Game[]>([]);
  const [teachOnly, setTeachOnly] = useState(false);
  const [creating, setCreating] = useState(false);
  const [campRegistration, setCampRegistration] = useState<CampRegistration>();
  const [importing, setImporting] = useState(false);
  const [error, setError] = useState(community ? "" : "Откройте приложение из чата клуба.");
  const activeCommunity = communities.find(value => value.key === community);

  useEffect(() => {
    webApp?.ready();
    webApp?.expand();
    if (previewMode) return;
    void api<Community[]>("/api/miniapp/communities")
      .then(values => {
        setCommunities(values);
        if (values.length === 0) {
          setError("Не удалось подтвердить доступ к сообществам OyinQ.");
        } else if (!values.some(value => value.key === community)) {
          setCommunity(values[0].key);
          setError("");
        }
      })
      .catch(e => setError((e as Error).message));
    void api<AdminCommunity[]>("/api/miniapp/admin/communities")
      .then(setAdminCommunities)
      .catch(() => undefined);
  }, []);

  const load = async () => {
    if (!community || previewMode || !activeCommunity || (activeCommunity.mode === "Camp" && !campRegistration?.registered)) return;
    try {
      setCards(await api<Card[]>(`/api/miniapp/gatherings?community=${encodeURIComponent(community)}${teachOnly ? "&canTeachRules=true" : ""}`));
    } catch (e) { setError((e as Error).message); }
  };
  useEffect(() => {
    if (activeCommunity?.mode !== "Camp") { setCampRegistration(undefined); return; }
    void api<CampRegistration>(`/api/miniapp/camp/registration?community=${encodeURIComponent(community)}`)
      .then(setCampRegistration)
      .catch(e => setError((e as Error).message));
  }, [community, activeCommunity?.mode]);
  useEffect(() => { void load(); }, [teachOnly, community, activeCommunity?.mode, campRegistration?.registered]);
  useEffect(() => { if (initialGathering && activeCommunity) void open(initialGathering); }, [activeCommunity?.mode]);

  const open = async (id: string) => {
    if (previewMode) { setDetail({ gathering: { ...previewCard, canTeachRules: true, organizerName: "Sardar", minimumPlayers: 3, expansions: ["Prelude", "Hellas & Elysium"] }, canEdit: true }); return; }
    try { setDetail(await api(`/api/miniapp/gatherings/${id}?community=${encodeURIComponent(community)}`)); }
    catch (e) { setError((e as Error).message); }
  };
  const beginCreate = async () => {
    try { setGames(await api(`/api/miniapp/games?community=${encodeURIComponent(community)}`)); setCreating(true); }
    catch (e) { setError((e as Error).message); }
  };

  if (detail) return <DetailView value={detail} community={community} close={() => { setDetail(undefined); void load(); }} />;
  if (creating) return <CreateView games={games} community={community} close={() => { setCreating(false); void load(); }} />;
  if (importing) return <CampImportView community={community} close={() => { setImporting(false); void load(); }} />;
  if (adminOpen && adminCommunities) return <AdminCommunitiesView values={adminCommunities} onChange={setAdminCommunities} close={() => setAdminOpen(false)} />;

  const switchCommunity = (key: string) => {
    setCommunity(key);
    setCards([]);
    setDetail(undefined);
    setCreating(false);
    setImporting(false);
    setCampRegistration(undefined);
    setError("");
  };

  if (activeCommunity?.mode === "Camp" && campRegistration && !campRegistration.registered) {
    return <CampRegistrationView community={community} name={activeCommunity.name} onDone={() => setCampRegistration({ registered: true })} />;
  }

  return <main>
    <CommunitySwitcher values={communities} selected={community} onChange={switchCommunity} />
    <header><div><span className="eyebrow">{activeCommunity?.mode === "Camp" ? "BOARDGAME CAMP" : "BOARDGAME CLUB"}</span><h1>{activeCommunity?.name ?? "Сборы"}</h1></div><div className="actions">{adminCommunities && <button onClick={() => setAdminOpen(true)}>Настройки</button>}{activeCommunity?.mode === "Camp" && <button onClick={() => setImporting(true)}>BGG коллекция</button>}<button className="primary" onClick={beginCreate}>＋ Создать</button></div></header>
    <label className="filter"><input type="checkbox" checked={teachOnly} onChange={e => setTeachOnly(e.target.checked)} /> Подходит новичкам: правила объяснят</label>
    {error && <p className="error">{error}</p>}
    <section className="grid">{cards.map(card => <article className="card" key={card.publicId} onClick={() => open(card.publicId)}>
      <Cover src={card.imageUrl} name={card.gameName} />
      <div className="card-body"><span className="date">{card.localDateTime}</span><h2>{card.gameName}</h2>
        <p className="rules">{card.rulesText}</p>{card.description && <p className="description">{card.description}</p>}
        <div className="meta"><span>{card.statusText}</span><span>👥 {card.confirmedPlayers}/{card.desiredPlayers}–{card.maximumPlayers}</span></div>
      </div></article>)}</section>
    {!error && cards.length === 0 && <div className="empty">Пока нет открытых сборов.<br />Создайте первый!</div>}
  </main>;
}

function CommunitySwitcher({ values, selected, onChange }: { values: Community[]; selected: string; onChange: (key: string) => void }) {
  if (values.length < 2) return null;
  return <label className="community-switcher">Сообщество<select value={selected} onChange={event => onChange(event.target.value)}>{values.map(value => <option value={value.key} key={value.key}>{value.name}</option>)}</select></label>;
}

function AdminCommunitiesView({ values, onChange, close }: { values: AdminCommunity[]; onChange: (values: AdminCommunity[]) => void; close: () => void }) {
  const [form, setForm] = useState({ key: "", name: "", telegramChatId: "", mode: "Club", timeZoneId: "Asia/Qyzylorda" });
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);
  const [clubs, setClubs] = useState<AdminClub[]>([]); const [selectedClub, setSelectedClub] = useState(0); const [collectionJson, setCollectionJson] = useState(""); const [addBggId, setAddBggId] = useState("");
  const [administrators, setAdministrators] = useState<Administrator[]>([]); const [newAdministratorId, setNewAdministratorId] = useState("");
  const [bggUsername, setBggUsername] = useState(""); const [syncPreview, setSyncPreview] = useState<ClubSyncPreview>(); const [allowEmptySync, setAllowEmptySync] = useState(false);
  useEffect(() => {
    void api<AdminClub[]>("/api/miniapp/admin/clubs").then(result => { setClubs(result); if (result[0]) { setSelectedClub(result[0].id); setBggUsername(result[0].bggUsername ?? ""); } });
    void api<Administrator[]>("/api/miniapp/admin/administrators").then(setAdministrators);
  }, []);
  const loadCollection = async (clubId = selectedClub) => { if (!clubId) return; const document = await api<object>(`/api/miniapp/admin/clubs/${clubId}/collection`); setCollectionJson(JSON.stringify(document, null, 2)); setSyncPreview(undefined); setAllowEmptySync(false); };
  const replaceCollection = async () => { try { await api(`/api/miniapp/admin/clubs/${selectedClub}/collection`, { method: "PUT", body: collectionJson }); setError(""); setSyncPreview(undefined); setAllowEmptySync(false); setClubs(await api<AdminClub[]>("/api/miniapp/admin/clubs")); } catch (e) { setError((e as Error).message); } };
  const addGame = async () => { try { await api(`/api/miniapp/admin/clubs/${selectedClub}/games`, { method: "POST", body: JSON.stringify({ bggId: Number(addBggId), expansionBggIds: [] }) }); await loadCollection(); } catch (e) { setError((e as Error).message); } };
  const previewBggSync = async () => {
    if (!selectedClub) return;
    setSaving(true); setError(""); setSyncPreview(undefined); setAllowEmptySync(false);
    try {
      await api(`/api/miniapp/admin/clubs/${selectedClub}/bgg`, { method: "PUT", body: JSON.stringify({ bggInput: bggUsername }) });
      const preview = await api<ClubSyncPreview>(`/api/miniapp/admin/clubs/${selectedClub}/collection/bgg-preview`, { method: "POST" });
      setSyncPreview(preview); setCollectionJson(JSON.stringify(preview.document, null, 2));
      setClubs(current => current.map(club => club.id === selectedClub ? { ...club, bggUsername: preview.username } : club));
      setBggUsername(preview.username);
    } catch (e) { setError((e as Error).message); }
    finally { setSaving(false); }
  };
  const save = async () => {
    setSaving(true); setError("");
    try {
      await api("/api/miniapp/admin/communities", { method: "POST", body: JSON.stringify({ ...form, telegramChatId: Number(form.telegramChatId) }) });
      onChange(await api<AdminCommunity[]>("/api/miniapp/admin/communities"));
      setForm({ key: "", name: "", telegramChatId: "", mode: "Club", timeZoneId: "Asia/Qyzylorda" });
    } catch (e) { setError((e as Error).message); }
    finally { setSaving(false); }
  };
  const addAdministrator = async () => {
    try {
      await api("/api/miniapp/admin/administrators", { method: "POST", body: JSON.stringify({ telegramUserId: Number(newAdministratorId) }) });
      setAdministrators(await api<Administrator[]>("/api/miniapp/admin/administrators")); setNewAdministratorId(""); setError("");
    } catch (e) { setError((e as Error).message); }
  };
  const removeAdministrator = async (telegramUserId: number) => {
    try {
      await api(`/api/miniapp/admin/administrators/${telegramUserId}`, { method: "DELETE" });
      setAdministrators(await api<Administrator[]>("/api/miniapp/admin/administrators")); setError("");
    } catch (e) { setError((e as Error).message); }
  };
  return <main><button className="back" onClick={close}>← Назад</button><header><div><span className="eyebrow">АДМИНИСТРИРОВАНИЕ</span><h1>Сообщества</h1></div></header>
    <section className="grid">{values.map(value => <article className="community-card" key={value.key}><b>{value.name}</b><span>{value.mode} · {value.key}</span><span>Chat ID: {value.telegramChatId}</span><span>{value.timeZoneId}</span></article>)}</section>
    <section className="form"><h2>Коллекция клуба</h2><label>Клуб<select value={selectedClub} onChange={event => { const id = Number(event.target.value); const club = clubs.find(value => value.id === id); setSelectedClub(id); setBggUsername(club?.bggUsername ?? ""); setSyncPreview(undefined); setAllowEmptySync(false); void loadCollection(id); }}>{clubs.map(club => <option value={club.id} key={club.id}>{club.name} ({club.gameCount})</option>)}</select></label><label>Коллекция BGG<input value={bggUsername} maxLength={100} placeholder="Имя пользователя или ссылка BGG" onChange={event => setBggUsername(event.target.value)} /></label><div className="actions"><button disabled={saving || !selectedClub || !bggUsername} onClick={previewBggSync}>{saving ? "Загружаем BGG…" : "Сохранить и показать изменения"}</button><button onClick={() => loadCollection()}>Показать / экспортировать JSON</button></div>{syncPreview && <div className="sync-preview"><b>Предпросмотр BGG: {syncPreview.username}</b><span>Добавится: {syncPreview.added.length} · удалится: {syncPreview.removed.length} · обновится: {syncPreview.changed.length}</span>{syncPreview.added.length > 0 && <details><summary>Новые игры: {syncPreview.added.length}</summary><ul>{syncPreview.added.map(item => <li key={item.bggId}>{item.name} (BGG {item.bggId})</li>)}</ul></details>}{syncPreview.removed.length > 0 && <details><summary>Будут удалены: {syncPreview.removed.length}</summary><ul>{syncPreview.removed.map(item => <li key={item.bggId}>{item.name} (BGG {item.bggId})</li>)}</ul></details>}{syncPreview.changed.length > 0 && <details><summary>Обновятся метаданные: {syncPreview.changed.length}</summary><ul>{syncPreview.changed.map(item => <li key={item.bggId}>{item.name} (BGG {item.bggId})</li>)}</ul></details>}{syncPreview.orphanExpansions.length > 0 && <details><summary>Не включены дополнения без базовой игры: {syncPreview.orphanExpansions.length}</summary><ul>{syncPreview.orphanExpansions.map(item => <li key={item.bggId}>{item.name} (BGG {item.bggId})</li>)}</ul></details>}{syncPreview.isEmpty && <label className="warning"><input type="checkbox" checked={allowEmptySync} onChange={event => setAllowEmptySync(event.target.checked)} />Подтверждаю полную очистку коллекции клуба</label>}</div>}<div className="actions"><input inputMode="numeric" value={addBggId} placeholder="BGG ID" onChange={e => setAddBggId(e.target.value)} /><button disabled={!selectedClub || !addBggId} onClick={addGame}>Добавить игру BGG</button></div>{collectionJson && <><textarea rows={16} value={collectionJson} onChange={e => { setCollectionJson(e.target.value); setSyncPreview(undefined); setAllowEmptySync(false); }} /><button className="primary" disabled={syncPreview?.isEmpty && !allowEmptySync} onClick={replaceCollection}>{syncPreview ? "Применить снимок BGG" : "Проверить и заменить JSON"}</button></>}</section>
    <section className="form"><h2>Администраторы</h2><p>Права хранятся в PostgreSQL. Последнего администратора удалить нельзя.</p><div className="grid">{administrators.map(value => <article className="community-card" key={value.telegramUserId}><b>{value.telegramUserId}</b><span>{value.addedByTelegramUserId ? `Добавил: ${value.addedByTelegramUserId}` : "Добавлен при bootstrap"}</span><button disabled={administrators.length === 1} onClick={() => removeAdministrator(value.telegramUserId)}>Удалить</button></article>)}</div><div className="actions"><input inputMode="numeric" value={newAdministratorId} placeholder="Telegram user ID" onChange={event => setNewAdministratorId(event.target.value)} /><button disabled={!newAdministratorId || Number(newAdministratorId) <= 0} onClick={addAdministrator}>Добавить администратора</button></div></section>
    <section className="form"><h2>Добавить клуб</h2><p>Кэмп создаётся через <b>/admin → Создать кэмп</b>: бот попросит выбрать группу штатной кнопкой Telegram.</p><label>Ключ<input value={form.key} maxLength={32} placeholder="new-club" onChange={event => setForm({ ...form, key: event.target.value })} /></label><label>Название<input value={form.name} maxLength={160} onChange={event => setForm({ ...form, name: event.target.value })} /></label><label>Telegram chat ID клуба<input value={form.telegramChatId} inputMode="numeric" placeholder="-1001234567890" onChange={event => setForm({ ...form, telegramChatId: event.target.value })} /></label><label>Часовой пояс IANA<input value={form.timeZoneId} onChange={event => setForm({ ...form, timeZoneId: event.target.value })} /></label>{error && <p className="error">{error}</p>}<button className="primary wide" disabled={saving || !form.key || !form.name || !form.telegramChatId} onClick={save}>{saving ? "Сохраняем…" : "Добавить клуб"}</button></section>
  </main>;
}

function DetailView({ value, community, close }: { value: { gathering: Detail; canEdit: boolean }; community: string; close: () => void }) {
  const g = value.gathering;
  const [editing, setEditing] = useState(false);
  const [description, setDescription] = useState(g.description ?? "");
  const [canTeach, setCanTeach] = useState(g.canTeachRules);
  const [error, setError] = useState("");
  const save = async () => {
    await api(`/api/miniapp/gatherings/${g.publicId}/presentation`, { method: "PUT", body: JSON.stringify({ communityKey: community, description, canTeachRules: canTeach }) });
    close();
  };
  const participate = async (action: "join" | "leave") => {
    try { await api(`/api/miniapp/gatherings/${g.publicId}/${action}`, { method: "POST", body: JSON.stringify({ communityKey: community }) }); close(); }
    catch (e) { setError((e as Error).message); }
  };
  return <main><button className="back" onClick={close}>← Назад</button><article className="detail">
    <Cover src={g.imageUrl} name={g.gameName} large /><div className="detail-body"><span className="date">{g.localDateTime}</span><h1>{g.gameName}</h1>
    <p className="rules prominent">{g.canTeachRules ? "✅ Могу объяснить правила" : "Опыт с игрой желателен"}</p>
    {g.expansions.length > 0 && <div><b>Дополнения</b><ul>{g.expansions.map(x => <li key={x}>{x}</li>)}</ul></div>}
    {g.description && <p className="description detail-description">{g.description}</p>}
    <dl><dt>Организатор</dt><dd>{g.organizerName}</dd><dt>Игроки</dt><dd>{g.confirmedPlayers} сейчас · минимум {g.minimumPlayers} · оптимально {g.desiredPlayers} · максимум {g.maximumPlayers}</dd></dl>
    <div className="actions"><button className="primary" onClick={() => participate("join")}>Присоединиться</button><button onClick={() => participate("leave")}>Отказаться от места</button></div>
    {error && <p className="error">{error}</p>}{value.canEdit && <button onClick={() => setEditing(!editing)}>Изменить описание</button>}
    {editing && <div className="editor"><textarea maxLength={300} value={description} onChange={e => setDescription(e.target.value)} placeholder="1–2 предложения о партии" /><small>{description.length}/300</small><label><input type="checkbox" checked={canTeach} onChange={e => setCanTeach(e.target.checked)} /> Могу объяснить правила</label><button className="primary" onClick={save}>Сохранить</button></div>}
    </div></article></main>;
}

function CampRegistrationView({ community, name, onDone }: { community: string; name: string; onDone: () => void }) {
  const [days, setDays] = useState(1); const [accommodation, setAccommodation] = useState(false); const [displayName, setDisplayName] = useState(""); const [error, setError] = useState("");
  const save = async () => { try { await api("/api/miniapp/camp/registration", { method: "PUT", body: JSON.stringify({ communityKey: community, daysStaying: days, needsAccommodation: accommodation, displayName }) }); onDone(); } catch (e) { setError((e as Error).message); } };
  return <main><header><div><span className="eyebrow">BOARDGAME CAMP</span><h1>{name}</h1></div></header><section className="form"><h2>Регистрация</h2><p>После регистрации откроются каталог, личный импорт BGG и сборы игр.</p><label>Имя для участников<input value={displayName} maxLength={128} onChange={e => setDisplayName(e.target.value)} placeholder="Можно оставить пустым" /></label><label>Сколько дней вы будете на кэмпе?<input type="number" min="1" max="30" value={days} onChange={e => setDays(Number(e.target.value))} /></label><label className="filter"><input type="checkbox" checked={accommodation} onChange={e => setAccommodation(e.target.checked)} /> Нужно проживание</label>{error && <p className="error">{error}</p>}<button className="primary wide" onClick={save}>Завершить регистрацию</button></section></main>;
}

function CampImportView({ community, close }: { community: string; close: () => void }) {
  const [input, setInput] = useState(""); const [items, setItems] = useState<ImportItem[]>([]); const [error, setError] = useState(""); const [loading, setLoading] = useState(false);
  const [manualInput, setManualInput] = useState(""); const [manualGame, setManualGame] = useState<BggDetails>(); const [manualExpansions, setManualExpansions] = useState<number[]>([]);
  const load = async () => { setLoading(true); setError(""); try { const result = await api<{ items: ImportItem[] }>("/api/miniapp/camp/import/preview", { method: "POST", body: JSON.stringify({ communityKey: community, bggInput: input }) }); setItems(result.items); } catch (e) { setError((e as Error).message); } finally { setLoading(false); } };
  const toggle = (id: number, type: number) => setItems(values => values.map(value => value.bggId === id && value.itemType === type ? { ...value, selected: !value.selected } : value));
  const setAll = (selected: boolean) => setItems(values => values.map(value => ({ ...value, selected })));
  const save = async () => { try { await api("/api/miniapp/camp/contributions", { method: "PUT", body: JSON.stringify({ communityKey: community, items }) }); close(); } catch (e) { setError((e as Error).message); } };
  const loadManual = async () => { try { setManualGame(await api<BggDetails>(`/api/miniapp/bgg/game?community=${encodeURIComponent(community)}&input=${encodeURIComponent(manualInput)}`)); setManualExpansions([]); } catch (e) { setError((e as Error).message); } };
  const saveManual = async () => { try { await api("/api/miniapp/camp/catalog/games", { method: "POST", body: JSON.stringify({ communityKey: community, bggInput: manualInput, expansionBggIds: manualExpansions }) }); close(); } catch (e) { setError((e as Error).message); } };
  const bases = items.filter(value => value.itemType === 0); const orphans = items.filter(value => value.itemType === 1 && !bases.some(base => base.bggId === value.parentBggId));
  const row = (item: ImportItem, nested = false) => { const missingBase = item.itemType === 1 && item.selected && !bases.some(base => base.bggId === item.parentBggId && base.selected); return <label key={`${item.itemType}-${item.bggId}`} className={nested ? "nested" : ""}><input type="checkbox" checked={item.selected} onChange={() => toggle(item.bggId, item.itemType)} /> {missingBase ? "🟨 " : ""}{item.name}</label>; };
  return <main><button className="back" onClick={close}>← Назад</button><section className="form"><h1>Моя коллекция BGG</h1>{items.length === 0 ? <><label>Имя пользователя или ссылка BGG<input value={input} onChange={e => setInput(e.target.value)} /></label><button className="primary wide" disabled={loading || !input} onClick={load}>{loading ? "Загружаем…" : "Загрузить для выбора"}</button></> : <><div className="actions"><button onClick={() => setAll(true)}>Выбрать всё</button><button onClick={() => setAll(false)}>Очистить</button></div><p>Игры и дополнения переключаются независимо. 🟨 означает, что дополнение выбрано без базовой игры — это допустимо.</p>{bases.map(base => <fieldset key={base.bggId}>{row(base)}{items.filter(value => value.itemType === 1 && value.parentBggId === base.bggId).map(value => row(value, true))}</fieldset>)}{orphans.length > 0 && <fieldset><legend>Дополнения без найденной базовой игры</legend>{orphans.map(value => row(value, true))}</fieldset>}<button className="primary wide" onClick={save}>Подтвердить выбранное</button></>}<hr /><h2>Добавить одну игру вручную</h2><div className="actions"><input value={manualInput} placeholder="Ссылка BGG" onChange={e => setManualInput(e.target.value)} /><button onClick={loadManual}>Найти</button></div>{manualGame && <fieldset><legend>{manualGame.name}</legend>{manualGame.expansions.map(expansion => <label key={expansion.bggId}><input type="checkbox" checked={manualExpansions.includes(expansion.bggId)} onChange={() => setManualExpansions(values => values.includes(expansion.bggId) ? values.filter(id => id !== expansion.bggId) : [...values, expansion.bggId])} /> {expansion.name}</label>)}<button className="primary" onClick={saveManual}>Добавить в этот кэмп</button></fieldset>}{error && <p className="error">{error}</p>}</section></main>;
}

function CreateView({ games, community, close }: { games: Game[]; community: string; close: () => void }) {
  const [source, setSource] = useState<"catalog" | "bgg">("catalog");
  const [bggId, setBggId] = useState(games[0]?.bggId ?? 0); const [bggInput, setBggInput] = useState(""); const [external, setExternal] = useState<BggDetails>();
  const [selectedExpansions, setSelectedExpansions] = useState<number[]>([]); const [starts, setStarts] = useState(""); const [description, setDescription] = useState(""); const [teach, setTeach] = useState(true);
  const [limits, setLimits] = useState({ min: 2, desired: 4, max: 5 }); const [error, setError] = useState("");
  const selectedGame = source === "catalog" ? games.find(game => game.bggId === bggId) : external;
  const loadBgg = async () => { try { const value = await api<BggDetails>(`/api/miniapp/bgg/game?community=${encodeURIComponent(community)}&input=${encodeURIComponent(bggInput)}`); setExternal(value); setBggId(value.bggId); setSelectedExpansions([]); if (value.minPlayers) setLimits(current => ({ ...current, min: value.minPlayers!, desired: Math.max(value.minPlayers!, current.desired), max: Math.max(value.maxPlayers ?? current.max, current.desired) })); } catch (e) { setError((e as Error).message); } };
  const toggleExpansion = (id: number) => setSelectedExpansions(values => values.includes(id) ? values.filter(value => value !== id) : [...values, id]);
  const submit = async () => { try { await api("/api/miniapp/gatherings", { method: "POST", body: JSON.stringify({ communityKey: community, bggId, gameSource: source, selectedExpansionIds: selectedExpansions, startsAtLocal: starts, minimumPlayers: limits.min, desiredPlayers: limits.desired, maximumPlayers: limits.max, description, canTeachRules: teach }) }); close(); } catch (e) { setError((e as Error).message); } };
  return <main><button className="back" onClick={close}>← Назад</button><section className="form"><h1>Новый сбор</h1>
    <div className="actions"><button className={source === "catalog" ? "primary" : ""} onClick={() => { setSource("catalog"); setBggId(games[0]?.bggId ?? 0); setSelectedExpansions([]); }}>Из каталога</button><button className={source === "bgg" ? "primary" : ""} onClick={() => { setSource("bgg"); setBggId(external?.bggId ?? 0); setSelectedExpansions([]); }}>По ссылке BGG</button></div>
    {source === "catalog" ? <label>Игра<select value={bggId} onChange={e => { setBggId(Number(e.target.value)); setSelectedExpansions([]); }}>{games.map(g => <option value={g.bggId} key={g.bggId}>{g.name}{g.contributorCount > 1 ? ` · копий: ${g.contributorCount}` : ""}</option>)}</select></label>
      : <label>Ссылка BGG<div className="actions"><input value={bggInput} placeholder="https://boardgamegeek.com/boardgame/…" onChange={e => setBggInput(e.target.value)} /><button onClick={loadBgg}>Загрузить</button></div></label>}
    {selectedGame && <div><b>{selectedGame.name}</b>{selectedGame.expansions.length > 0 && <fieldset><legend>Какие дополнения будут в партии?</legend>{selectedGame.expansions.map(expansion => <label key={expansion.bggId}><input type="checkbox" checked={selectedExpansions.includes(expansion.bggId)} onChange={() => toggleExpansion(expansion.bggId)} /> {expansion.name}</label>)}</fieldset>}</div>}
    <label>Дата и время клуба<input type="datetime-local" value={starts} onChange={e => setStarts(e.target.value)} /></label><div className="limits">{(["min", "desired", "max"] as const).map((key, i) => <label key={key}>{["Минимум", "Оптимально", "Максимум"][i]}<input type="number" min="1" value={limits[key]} onChange={e => setLimits({ ...limits, [key]: Number(e.target.value) })} /></label>)}</div><label>Короткое описание<textarea maxLength={300} value={description} onChange={e => setDescription(e.target.value)} placeholder="1–2 предложения: формат партии, опыт, дополнения…" /><small>{description.length}/300</small></label><label className="filter"><input type="checkbox" checked={teach} onChange={e => setTeach(e.target.checked)} /> Могу объяснить правила</label>{error && <p className="error">{error}</p>}<button className="primary wide" disabled={!bggId || !starts} onClick={submit}>Создать сбор</button></section></main>;
}

createRoot(document.getElementById("root")!).render(<React.StrictMode><App /></React.StrictMode>);
