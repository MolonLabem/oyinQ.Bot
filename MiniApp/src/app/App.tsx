import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { Bootstrap, Capabilities, Community } from "../api/types";
import { Navigation } from "../components/Navigation";
import { Card, ErrorState, Loading, Notice, Page } from "../components/Ui";
import { AdminPage } from "../pages/admin/AdminPage";
import { CampRegistrationGate, MyGamesPage } from "../pages/camp/MyGamesPage";
import { GamesPage } from "../pages/games/GamesPage";
import { GatheringsPage } from "../pages/gatherings/GatheringsPage";

export function App() {
  const [bootstrap, setBootstrap] = useState<Bootstrap>(); const [capabilities, setCapabilities] = useState<Capabilities>(); const [error, setError] = useState<string>();
  const [communityKey, setCommunityKey] = useState(() => initialCommunity()); const [tab, setTab] = useState("gatherings");
  const adminMode = new URLSearchParams(location.search).get("admin") === "1";
  const initialGatheringId = new URLSearchParams(location.search).get("gathering") ?? undefined;
  useEffect(() => { Promise.all([api<Bootstrap>("/communities"), api<Capabilities>("/capabilities")]).then(([b, c]) => { setBootstrap(b); setCapabilities(c); if (!communityKey && b.communities.length === 1) setCommunityKey(b.communities[0].key); }).catch(e => setError(e instanceof Error ? e.message : String(e))); }, []);
  useEffect(() => { if (communityKey) localStorage.setItem("oyinq-community", communityKey); }, [communityKey]);
  const community = useMemo(() => bootstrap?.communities.find(x => x.key === communityKey), [bootstrap, communityKey]);
  if (error) return <Page title="OyinQ"><ErrorState message={error} retry={() => location.reload()} /></Page>;
  if (!bootstrap || !capabilities) return <Page title="OyinQ"><Loading /></Page>;
  if (adminMode) return bootstrap.isAdministrator ? <AdminPage /> : <Page title="Нет доступа"><Notice kind="danger">Эта область доступна только администраторам OyinQ.</Notice></Page>;
  if (!community) return <CommunityPicker communities={bootstrap.communities} choose={setCommunityKey} admin={bootstrap.isAdministrator} />;
  const tabs = community.mode === "Camp" ? [{ id: "gatherings", label: "Сборы", icon: "🎲" }, { id: "games", label: "Игры", icon: "📚" }, { id: "mine", label: "Мои игры", icon: "🧳" }] : [{ id: "gatherings", label: "Сборы", icon: "🎲" }, { id: "games", label: "Игры", icon: "📚" }];
  const content = tab === "gatherings" ? <GatheringsPage community={community} initialGatheringId={initialGatheringId} /> : tab === "games" ? <GamesPage community={community} /> : <MyGamesPage community={community} />;
  return <div className="app-shell"><header className="context-bar"><button className="context-button" onClick={() => setCommunityKey("")}><span className={`mode-dot ${community.mode.toLowerCase()}`} />{community.name}<span aria-hidden>⌄</span></button>{!capabilities.boardGameGeekAvailable && <span className="bgg-off" title={capabilities.boardGameGeekUnavailableReason}>BGG недоступен</span>}</header><div className="content">{community.mode === "Camp" ? <CampRegistrationGate community={community}>{content}</CampRegistrationGate> : content}</div><Navigation tabs={tabs} active={tab} onChange={setTab} /></div>;
}

function CommunityPicker({ communities, choose, admin }: { communities: Community[]; choose: (key: string) => void; admin: boolean }) {
  return <Page title="Выберите сообщество" subtitle="Ваш контекст сохраняется при переходах">{communities.length === 0 ? <Notice kind="warning">Не удалось подтвердить членство ни в одном активном сообществе.{admin && <> Откройте <a href="?admin=1">администрирование</a>.</>}</Notice> : <div className="stack">{communities.map(c => <button className="card community-option" key={c.key} onClick={() => choose(c.key)}><span className={`mode-icon ${c.mode.toLowerCase()}`}>{c.mode === "Club" ? "♣" : "⛺"}</span><span><strong>{c.name}</strong><small>{c.mode === "Club" ? "Клуб" : "Кэмп"}</small></span></button>)}</div>}</Page>;
}

function initialCommunity() {
  const query = new URLSearchParams(location.search).get("community");
  const start = window.Telegram?.WebApp.initDataUnsafe.start_param;
  return query ?? (start?.startsWith("community-") ? start.slice(10) : null) ?? localStorage.getItem("oyinq-community") ?? "";
}
