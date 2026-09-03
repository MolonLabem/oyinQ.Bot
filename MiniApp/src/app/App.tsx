import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { Bootstrap, Capabilities, Community } from "../api/types";
import { Navigation } from "../components/Navigation";
import { BggAttribution, ErrorState, Loading, Notice, Page } from "../components/Ui";
import { AdminPage } from "../pages/admin/AdminPage";
import { CampRegistrationGate, MyGamesPage } from "../pages/camp/MyGamesPage";
import { GamesPage } from "../pages/games/GamesPage";
import { GatheringsPage } from "../pages/gatherings/GatheringsPage";
import { ProfilePage } from "../pages/profile/ProfilePage";
import { telegram } from "../telegram/webApp";
import { fullscreenLabel } from "../pages/camp/registrationLogic";
import { collectionVisitFromGathering, positiveGameId } from "./collectionNavigation";
import { CommunityAvatar } from "../components/CommunityAvatar";
import { miniAppLaunchContext } from "./launchContext";

export function App() {
  const [launchContext] = useState(() => miniAppLaunchContext(location.search, telegram.startParam));
  const [bootstrap, setBootstrap] = useState<Bootstrap>(); const [capabilities, setCapabilities] = useState<Capabilities>(); const [error, setError] = useState<string>();
  const [communityKey, setCommunityKey] = useState(() => launchContext.communityKey ?? localStorage.getItem("oyinq-community") ?? ""); const [tab, setTab] = useState(() => new URLSearchParams(location.search).get("tab") ?? "gatherings");
  const adminMode = new URLSearchParams(location.search).get("admin") === "1";
  const [initialGatheringId, setInitialGatheringId] = useState(() => launchContext.gatheringId);
  const [initialCollectionGameId, setInitialCollectionGameId] = useState(() => positiveGameId(new URLSearchParams(location.search).get("game")));
  const [collectionReturnGatheringId, setCollectionReturnGatheringId] = useState<string>();
  const [profileReturnCommunityKey, setProfileReturnCommunityKey] = useState<string>();
  const [registrationEditRequest, setRegistrationEditRequest] = useState(0);
  const [fullscreen, setFullscreen] = useState(telegram.isFullscreen);
  useEffect(() => { Promise.all([api<Bootstrap>("/communities"), api<Capabilities>("/capabilities")]).then(([b, c]) => { setBootstrap(b); setCapabilities(c); if (!communityKey && b.communities.length === 1) setCommunityKey(b.communities[0].key); }).catch(e => setError(e instanceof Error ? e.message : String(e))); }, []);
  useEffect(() => { if (communityKey) localStorage.setItem("oyinq-community", communityKey); }, [communityKey]);
  useEffect(() => telegram.onFullscreenChanged(setFullscreen), []);
  const community = useMemo(() => bootstrap?.communities.find(x => x.key === communityKey), [bootstrap, communityKey]);
  if (error) return <Page title="OyinQ"><ErrorState message={error} retry={() => location.reload()} /></Page>;
  if (!bootstrap || !capabilities) return <Page title="OyinQ"><Loading /></Page>;
  if (adminMode) return bootstrap.canOpenAdminPanel ? <AdminPage bggAvailable={capabilities.boardGameGeekAvailable} isSuperAdmin={bootstrap.isSuperAdmin} /> : <Page title="Нет доступа"><Notice kind="danger">Эта область доступна администраторам зарегистрированных чатов OyinQ.</Notice></Page>;
  if (!community) return <CommunityPicker communities={bootstrap.communities} choose={key => { setCommunityKey(key); if (!new URLSearchParams(location.search).get("tab")) setTab("gatherings"); }} admin={bootstrap.canOpenAdminPanel} />;
  const tabs = community.mode === "Camp" ? [{ id: "gatherings", label: "Сборы", icon: "🎲" }, { id: "games", label: "Игры", icon: "📚" }, { id: "mine", label: "Мои игры", icon: "🧳" }, { id: "profile", label: "Профиль", icon: "👤" }] : [{ id: "gatherings", label: "Сборы", icon: "🎲" }, { id: "games", label: "Игры", icon: "📚" }, { id: "profile", label: "Профиль", icon: "👤" }];
  const activeTab = tabs.some(item => item.id === tab) ? tab : "gatherings";
  const fullscreenActionLabel = fullscreenLabel(fullscreen);
  const editRegistration = () => { setRegistrationEditRequest(value => value + 1); setTab("mine"); };
  const openCollection = (bggId: number, gatheringId: string) => { const visit = collectionVisitFromGathering(bggId, gatheringId); setInitialCollectionGameId(visit.bggId); setCollectionReturnGatheringId(visit.returnGatheringId); setTab("games"); };
  const backToGathering = collectionReturnGatheringId ? () => { setInitialGatheringId(collectionReturnGatheringId); setCollectionReturnGatheringId(undefined); setTab("gatherings"); } : undefined;
  const openProfileGathering = (targetCommunityKey: string, gatheringId: string) => { setProfileReturnCommunityKey(community.key); setCommunityKey(targetCommunityKey); setInitialGatheringId(gatheringId); setTab("gatherings"); };
  const backToProfile = profileReturnCommunityKey ? () => { setCommunityKey(profileReturnCommunityKey); setProfileReturnCommunityKey(undefined); setTab("profile"); } : undefined;
  const content = activeTab === "gatherings" ? <GatheringsPage key={community.key} community={community} bggAvailable={capabilities.boardGameGeekAvailable} initialGatheringId={initialGatheringId} onInitialConsumed={() => setInitialGatheringId(undefined)} editRegistration={editRegistration} openCollection={openCollection} backFromInitial={backToProfile} /> : activeTab === "games" ? <GamesPage community={community} initialGameId={initialCollectionGameId} onInitialConsumed={() => setInitialCollectionGameId(undefined)} backToGathering={backToGathering} /> : activeTab === "profile" ? <ProfilePage community={community} communities={bootstrap.communities} openGathering={openProfileGathering} /> : <MyGamesPage community={community} bggAvailable={capabilities.boardGameGeekAvailable} editRequest={registrationEditRequest} onEditRequestConsumed={() => setRegistrationEditRequest(0)} />;
  const gatedContent = community.mode === "Camp" && activeTab !== "profile" ? <CampRegistrationGate community={community} canOpenAdminPanel={bootstrap.canOpenAdminPanel}>{content}</CampRegistrationGate> : content;
  return <div className="app-shell"><header className="context-bar"><button className="context-button" onClick={() => setCommunityKey("")}><span className={`mode-dot ${community.mode.toLowerCase()}`} />{community.name}<span aria-hidden>⌄</span></button><div className="context-actions">{!capabilities.boardGameGeekAvailable && <span className="bgg-off" title={capabilities.boardGameGeekUnavailableReason}>BGG недоступен</span>}</div></header>{telegram.canFullscreen && <div className="display-tools"><button type="button" className="fullscreen-action" aria-label={fullscreenActionLabel} title={fullscreenActionLabel} aria-pressed={fullscreen} onClick={() => fullscreen ? telegram.exitFullscreen() : void telegram.requestFullscreen()}><FullscreenIcon fullscreen={fullscreen} /></button></div>}<div className="content">{gatedContent}<BggAttribution /><PrivacyLink /></div><Navigation tabs={tabs} active={activeTab} onChange={setTab} /></div>;
}

function FullscreenIcon({ fullscreen }: { fullscreen: boolean }) {
  return <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <path d={fullscreen
      ? "M9 4v5H4m11-5v5h5M9 20v-5H4m11 5v-5h5"
      : "M8 3H3v5m13-5h5v5M8 21H3v-5m13 5h5v-5"} />
  </svg>;
}

function CommunityPicker({ communities, choose, admin }: { communities: Community[]; choose: (key: string) => void; admin: boolean }) {
  return <Page title="Выберите сообщество" subtitle="Переключиться можно в любой момент">{communities.length === 0 ? <Notice kind="warning">У вас пока нет доступа ни к одному активному сообществу.{admin && <> Откройте <a href="?admin=1">раздел управления</a>.</>}</Notice> : <div className="stack">{communities.map(c => <button className="card community-option" key={c.key} onClick={() => choose(c.key)}><CommunityAvatar community={c} /><span><strong>{c.name}</strong><small>{c.mode === "Club" ? "Клуб" : "Кэмп"}</small></span></button>)}</div>}<BggAttribution /><PrivacyLink /></Page>;
}

function PrivacyLink() {
  return <button className="privacy-link" onClick={() => telegram.openLink(`${location.origin}/privacy`)}>Политика конфиденциальности</button>;
}
