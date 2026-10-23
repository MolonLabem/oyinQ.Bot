import { useBackButton, BackButtonScope } from "../../hooks/useBackButton";
import { CampProfileProvider } from "./CampProfileContext";
import { WishlistPanel } from "../../components/Wishlist";
import { PlayedHistory } from "./PlayedHistory";
import { CampPersonalWishes } from "../games/CampWishlist";
import { ChangelogPage } from "./ChangelogPage";
import { ProfileSettings, type ProfileSettingSection } from "./ProfileSettings";
import { lazy, Suspense, useEffect, useState } from "react";
import { api } from "../../api/client";
import type { Community, Profile } from "../../api/types";
import { BackButton, Empty, ErrorState, Loading, Page, ProductFooter, Tabs } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { BotStartNotice } from "../../components/BotStartNotice";
import { ProfileCollectionPage } from "./ProfileCollectionPage";

const ProfileCalendar = lazy(() => import("./ProfileCalendar").then(module => ({ default: module.ProfileCalendar })));

type ProfileProps = { initialTab?: string; initialGameId?: number; back?: () => void; community?: Community; communities: Community[]; bggAvailable: boolean; editRequest?: number; onEditRequestConsumed?: () => void; openGathering: (communityKey: string, gatheringId: string) => void };
export function ProfilePage(props: ProfileProps) {
  return props.community?.mode === "Camp" ? <CampProfileProvider key={props.community.key} community={props.community}><ProfileContent {...props} /></CampProfileProvider> : <ProfileContent key={props.community?.key ?? "global"} {...props} />;
}
function ProfileContent({ community, communities, openGathering, bggAvailable, editRequest = 0, onEditRequestConsumed, initialTab, initialGameId, back }: ProfileProps) {
  const profile = useAsync(() => api<Profile>("/profile"), [community?.key]);
  const [tab, setTab] = useState(() => initialTab ?? new URLSearchParams(location.search).get("profileTab") ?? sessionStorage.getItem(`oyinq-profile-tab:${community?.key ?? "global"}`) ?? "collection");
  useBackButton(Boolean(back), () => back?.());
  const [showChangelog, setShowChangelog] = useState(false);
  const [setting, setSetting] = useState<ProfileSettingSection>();
  useEffect(() => { sessionStorage.setItem(`oyinq-profile-tab:${community?.key ?? "global"}`, tab); }, [tab, community?.key]);
  useEffect(() => { if (editRequest > 0) { setTab("settings"); setSetting("registration"); onEditRequestConsumed?.(); } }, [editRequest, onEditRequestConsumed]);

  if (showChangelog) return <BackButtonScope><ChangelogPage back={() => setShowChangelog(false)} /></BackButtonScope>;
  if (profile.loading && !profile.data) return <Page title="Профиль" hideTitle><Loading /></Page>;
  if (profile.error) return <Page title="Профиль" hideTitle><ErrorState message={profile.error} retry={profile.reload} /></Page>;
  if (!profile.data) return null;
  return <BackButtonScope><div className="profile-screen"><Page title="Профиль" hideTitle subtitle={profile.data.preferredDisplayName || profile.data.telegramDisplayName}
    actions={<button type="button" className="ghost profile-changelog" onClick={() => setShowChangelog(true)}>Что нового?</button>}>
    {back && <BackButton onClick={back} />}
    <BotStartNotice required={profile.data.botStartRequired} startUrl={profile.data.startUrl} refresh={profile.reload} />
    <ProfileTabs active={tab} select={next => { setSetting(undefined); setTab(next); }} />
    <div hidden={tab !== "collection"}><BackButtonScope active={tab === "collection"}><ProfileCollectionPage key={`collection-${community?.key ?? "global"}`} community={community} bggAvailable={bggAvailable} initialGameId={initialGameId} editRegistration={() => { setSetting("registration"); setTab("settings"); }} /></BackButtonScope></div>
    {tab === "wishes" && (community ? community.mode === "Camp" ? <CampPersonalWishes key={community.key} community={community} bggAvailable={bggAvailable} openGathering={id => openGathering(community.key, id)} /> : <WishlistPanel community={community} bggAvailable={bggAvailable} /> : <Empty>Выберите сообщество, чтобы посмотреть свои хотелки. <a href="?tab=communities">Выбрать сообщество</a></Empty>)}
    {tab === "settings" && <ProfileSettings community={community} profile={profile.data} reloadProfile={profile.reload} section={setting} open={setSetting} />}
    {tab === "calendar" && <Suspense fallback={<Loading label="Загружаем календарь…" />}>
      <ProfileCalendar communities={communities} contextKey={community?.key} timeZoneId={community?.timeZoneId} open={openGathering} />
      <details className="profile-history"><summary>Сыгранные партии</summary><PlayedHistory key={community?.key} communityKey={community?.key} open={openGathering} compact /></details>
    </Suspense>}
    <ProductFooter />
  </Page></div></BackButtonScope>;
}

export function ProfileTabs({ active, select }: { active: string; select: (tab: string) => void }) {
  return <Tabs className="profile-tabs" label="Разделы профиля" active={active} onChange={select} items={[
    { id: "collection", label: "Игры" }, { id: "wishes", label: "Хотелки" }, { id: "calendar", label: "Календарь" },
    { id: "settings", label: "Настройки" }
  ]} />;
}
