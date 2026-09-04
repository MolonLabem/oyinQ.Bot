import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { Bootstrap, Capabilities, Community } from "../api/types";
import { Navigation } from "../components/Navigation";
import { ErrorState, Loading, Notice, Page } from "../components/Ui";
import { AdminPage } from "../pages/admin/AdminPage";
import { CampRegistrationGate } from "../pages/profile/ProfileCollectionPage";
import { GamesPage } from "../pages/games/GamesPage";
import { GatheringsPage } from "../pages/gatherings/GatheringsPage";
import { ProfilePage } from "../pages/profile/ProfilePage";
import { telegram } from "../telegram/webApp";
import { fullscreenLabel } from "../pages/camp/registrationLogic";
import { collectionVisitFromGathering, positiveGameId } from "./collectionNavigation";
import { CommunityAvatar } from "../components/CommunityAvatar";
import { mainTab, miniAppLaunchContext } from "./launchContext";

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
  if (!community) return <GlobalProfileShell profile={mainTab(tab) === "profile"}
    select={setTab} communities={<CommunityPicker communities={bootstrap.communities} choose={key => { setCommunityKey(key); setTab("gatherings"); }} admin={bootstrap.canOpenAdminPanel} />}>
    <ProfilePage communities={bootstrap.communities} bggAvailable={capabilities.boardGameGeekAvailable}
      openGathering={(key, id) => { setProfileReturnCommunityKey(""); setCommunityKey(key); setInitialGatheringId(id); setTab("gatherings"); }} />
  </GlobalProfileShell>;
  const tabs = [{ id: "gatherings", label: "Сборы", icon: "🎲" }, { id: "games", label: "Игры", icon: "📚" }, { id: "profile", label: "Профиль", icon: "👤" }];
  const activeTab = mainTab(tab);
  const fullscreenActionLabel = fullscreenLabel(fullscreen);
  const editRegistration = () => { setRegistrationEditRequest(value => value + 1); setTab("profile"); };
  const openCollection = (bggId: number, gatheringId: string) => { const visit = collectionVisitFromGathering(bggId, gatheringId); setInitialCollectionGameId(visit.bggId); setCollectionReturnGatheringId(visit.returnGatheringId); setTab("games"); };
  const backToGathering = collectionReturnGatheringId ? () => { setInitialGatheringId(collectionReturnGatheringId); setCollectionReturnGatheringId(undefined); setTab("gatherings"); } : undefined;
  const openProfileGathering = (targetCommunityKey: string, gatheringId: string) => { setProfileReturnCommunityKey(community.key); setCommunityKey(targetCommunityKey); setInitialGatheringId(gatheringId); setTab("gatherings"); };
  const backToProfile = profileReturnCommunityKey !== undefined ? () => { setCommunityKey(profileReturnCommunityKey); setProfileReturnCommunityKey(undefined); setTab("profile"); } : undefined;
  const content = activeTab === "gatherings" ? <GatheringsPage key={community.key} community={community} bggAvailable={capabilities.boardGameGeekAvailable} initialGatheringId={initialGatheringId} onInitialConsumed={() => setInitialGatheringId(undefined)} editRegistration={editRegistration} openCollection={openCollection} backFromInitial={backToProfile} /> : activeTab === "games" ? <GamesPage community={community} initialGameId={initialCollectionGameId} onInitialConsumed={() => setInitialCollectionGameId(undefined)} backToGathering={backToGathering} /> : <ProfilePage community={community} communities={bootstrap.communities} openGathering={openProfileGathering} bggAvailable={capabilities.boardGameGeekAvailable} editRequest={registrationEditRequest} onEditRequestConsumed={() => setRegistrationEditRequest(0)} />;
  const gatedContent = community.mode === "Camp" && activeTab !== "profile" ? <CampRegistrationGate community={community} canOpenAdminPanel={bootstrap.canOpenAdminPanel}>{content}</CampRegistrationGate> : content;
  return <div className="app-shell"><header className="context-bar"><button className="context-button" onClick={() => setCommunityKey("")}><span className={`mode-dot ${community.mode.toLowerCase()}`} /><span className="context-name">{community.name}</span><span aria-hidden>⌄</span></button><div className="context-actions">{!capabilities.boardGameGeekAvailable && <span className="bgg-off" title={capabilities.boardGameGeekUnavailableReason}>BGG недоступен</span>}{telegram.canFullscreen && <button type="button" className="fullscreen-action" aria-label={fullscreenActionLabel} title={fullscreenActionLabel} aria-pressed={fullscreen} onClick={() => fullscreen ? telegram.exitFullscreen() : void telegram.requestFullscreen()}><FullscreenIcon fullscreen={fullscreen} /></button>}</div></header><div className="content">{gatedContent}</div><Navigation tabs={tabs} active={activeTab} onChange={setTab} /></div>;
}

export function GlobalProfileShell({ profile, select, communities, children }: {
  profile: boolean; select: (tab: string) => void; communities: React.ReactNode; children: React.ReactNode;
}) {
  return <div className="app-shell"><div className="content">{profile ? children : communities}</div>
    <Navigation tabs={[{ id: "communities", label: "Сообщества", icon: "👥" }, { id: "profile", label: "Профиль", icon: "👤" }]}
      active={profile ? "profile" : "communities"} onChange={select} /></div>;
}

function FullscreenIcon({ fullscreen }: { fullscreen: boolean }) {
  return <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <path d={fullscreen
      ? "M9 4v5H4m11-5v5h5M9 20v-5H4m11 5v-5h5"
      : "M8 3H3v5m13-5h5v5M8 21H3v-5m13 5h5v-5"} />
  </svg>;
}

function CommunityPicker({ communities, choose, admin }: { communities: Community[]; choose: (key: string) => void; admin: boolean }) {
  return <Page title="Выберите сообщество" subtitle="Переключиться можно в любой момент">{communities.length === 0 ? <Notice kind="warning">У вас пока нет доступа ни к одному активному сообществу.{admin && <> Откройте <a href="?admin=1">раздел управления</a>.</>}</Notice> : <div className="stack">{communities.map(c => <button className="card community-option" key={c.key} onClick={() => choose(c.key)}><CommunityAvatar community={c} /><span><strong>{c.name}</strong><small>{c.mode === "Club" ? "Клуб" : "Кэмп"}</small></span></button>)}</div>}</Page>;
}
