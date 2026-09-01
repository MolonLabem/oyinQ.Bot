import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { Bootstrap, Capabilities, Community } from "../api/types";
import { Navigation } from "../components/Navigation";
import { ErrorState, Loading, Notice, Page } from "../components/Ui";
import { AdminPage } from "../pages/admin/AdminPage";
import { CampRegistrationGate, MyGamesPage } from "../pages/camp/MyGamesPage";
import { GamesPage } from "../pages/games/GamesPage";
import { GatheringsPage } from "../pages/gatherings/GatheringsPage";
import { ProfilePage } from "../pages/profile/ProfilePage";
import { telegram } from "../telegram/webApp";
import { fullscreenLabel } from "../pages/camp/registrationLogic";

export function App() {
  const [bootstrap, setBootstrap] = useState<Bootstrap>(); const [capabilities, setCapabilities] = useState<Capabilities>(); const [error, setError] = useState<string>();
  const [communityKey, setCommunityKey] = useState(() => initialCommunity()); const [tab, setTab] = useState(() => new URLSearchParams(location.search).get("tab") ?? "gatherings");
  const adminMode = new URLSearchParams(location.search).get("admin") === "1";
  const [initialGatheringId, setInitialGatheringId] = useState(() => new URLSearchParams(location.search).get("gathering") ?? undefined);
  const [registrationEditRequest, setRegistrationEditRequest] = useState(0);
  const [fullscreen, setFullscreen] = useState(telegram.isFullscreen);
  useEffect(() => { Promise.all([api<Bootstrap>("/communities"), api<Capabilities>("/capabilities")]).then(([b, c]) => { setBootstrap(b); setCapabilities(c); if (!communityKey && b.communities.length === 1) setCommunityKey(b.communities[0].key); }).catch(e => setError(e instanceof Error ? e.message : String(e))); }, []);
  useEffect(() => { if (communityKey) localStorage.setItem("oyinq-community", communityKey); }, [communityKey]);
  useEffect(() => { if (!new URLSearchParams(location.search).get("tab")) setTab("gatherings"); }, [communityKey]);
  useEffect(() => telegram.onFullscreenChanged(setFullscreen), []);
  const community = useMemo(() => bootstrap?.communities.find(x => x.key === communityKey), [bootstrap, communityKey]);
  if (error) return <Page title="OyinQ"><ErrorState message={error} retry={() => location.reload()} /></Page>;
  if (!bootstrap || !capabilities) return <Page title="OyinQ"><Loading /></Page>;
  if (adminMode) return bootstrap.canOpenAdminPanel ? <AdminPage bggAvailable={capabilities.boardGameGeekAvailable} isSuperAdmin={bootstrap.isSuperAdmin} /> : <Page title="Нет доступа"><Notice kind="danger">Эта область доступна администраторам зарегистрированных чатов OyinQ.</Notice></Page>;
  if (!community) return <CommunityPicker communities={bootstrap.communities} choose={setCommunityKey} admin={bootstrap.canOpenAdminPanel} />;
  const tabs = community.mode === "Camp" ? [{ id: "gatherings", label: "Сборы", icon: "🎲" }, { id: "games", label: "Игры", icon: "📚" }, { id: "mine", label: "Мои игры", icon: "🧳" }, { id: "profile", label: "Профиль", icon: "👤" }] : [{ id: "gatherings", label: "Сборы", icon: "🎲" }, { id: "games", label: "Игры", icon: "📚" }, { id: "profile", label: "Профиль", icon: "👤" }];
  const activeTab = tabs.some(item => item.id === tab) ? tab : "gatherings";
  const fullscreenActionLabel = fullscreenLabel(fullscreen);
  const editRegistration = () => { setRegistrationEditRequest(value => value + 1); setTab("mine"); };
  const content = activeTab === "gatherings" ? <GatheringsPage community={community} bggAvailable={capabilities.boardGameGeekAvailable} initialGatheringId={initialGatheringId} onInitialConsumed={() => setInitialGatheringId(undefined)} editRegistration={editRegistration} /> : activeTab === "games" ? <GamesPage community={community} /> : activeTab === "profile" ? <ProfilePage community={community} /> : <MyGamesPage community={community} bggAvailable={capabilities.boardGameGeekAvailable} editRequest={registrationEditRequest} onEditRequestConsumed={() => setRegistrationEditRequest(0)} />;
  const gatedContent = community.mode === "Camp" && activeTab !== "profile" ? <CampRegistrationGate community={community} canOpenAdminPanel={bootstrap.canOpenAdminPanel}>{content}</CampRegistrationGate> : content;
  return <div className="app-shell"><header className="context-bar"><button className="context-button" onClick={() => setCommunityKey("")}><span className={`mode-dot ${community.mode.toLowerCase()}`} />{community.name}<span aria-hidden>⌄</span></button><div className="context-actions">{!capabilities.boardGameGeekAvailable && <span className="bgg-off" title={capabilities.boardGameGeekUnavailableReason}>BGG недоступен</span>}</div></header>{telegram.canFullscreen && <div className="display-tools"><button className="fullscreen-action" aria-pressed={fullscreen} onClick={() => fullscreen ? telegram.exitFullscreen() : void telegram.requestFullscreen()}>{fullscreenActionLabel}</button></div>}<div className="content">{gatedContent}<PrivacyLink /></div><Navigation tabs={tabs} active={activeTab} onChange={setTab} /></div>;
}

function CommunityPicker({ communities, choose, admin }: { communities: Community[]; choose: (key: string) => void; admin: boolean }) {
  return <Page title="Выберите сообщество" subtitle="Переключиться можно в любой момент">{communities.length === 0 ? <Notice kind="warning">У вас пока нет доступа ни к одному активному сообществу.{admin && <> Откройте <a href="?admin=1">раздел управления</a>.</>}</Notice> : <div className="stack">{communities.map(c => <button className="card community-option" key={c.key} onClick={() => choose(c.key)}><span className={`mode-icon ${c.mode.toLowerCase()}`}>{c.mode === "Club" ? "♣" : "⛺"}</span><span><strong>{c.name}</strong><small>{c.mode === "Club" ? "Клуб" : "Кэмп"}</small></span></button>)}</div>}<PrivacyLink /></Page>;
}

function PrivacyLink() {
  return <button className="privacy-link" onClick={() => telegram.openLink(`${location.origin}/privacy`)}>Политика конфиденциальности</button>;
}

function initialCommunity() {
  const query = new URLSearchParams(location.search).get("community");
  const start = window.Telegram?.WebApp.initDataUnsafe.start_param;
  return query ?? (start?.startsWith("community-") ? start.slice(10) : null) ?? localStorage.getItem("oyinq-community") ?? "";
}
