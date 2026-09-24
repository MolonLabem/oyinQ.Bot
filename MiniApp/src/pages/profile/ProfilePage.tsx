import { CampPrivacy, CampProfileProvider } from "./CampProfileContext";
import { WishlistPanel } from "../../components/Wishlist";
import { PlayedHistory } from "./PlayedHistory";
import { CampPersonalWishes } from "../games/CampWishlist";
import { GatheringDashboard } from "../../components/GatheringDashboard";
import { ChangelogPage } from "./ChangelogPage";
import { NotificationSettings } from "./NotificationSettings";
import { useEffect, useState } from "react";
import { api, json, download } from "../../api/client";
import type { Community, Profile, ProfileGathering } from "../../api/types";
import { BackButton, Empty, ErrorState, Field, Loading, Notice, Page, ProductFooter, SaveButton, Tabs } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { BotStartNotice } from "../../components/BotStartNotice";
import { ProfileCollectionPage, CampRegistrationSettings } from "./ProfileCollectionPage";
import { ProfileScheduleList, profileScheduleEmptyText } from "./ProfileScheduleList";

type ProfileProps = { initialTab?: string; initialGameId?: number; back?: () => void; community?: Community; communities: Community[]; bggAvailable: boolean; editRequest?: number; onEditRequestConsumed?: () => void; openGathering: (communityKey: string, gatheringId: string) => void };
export function ProfilePage(props: ProfileProps) {
  return props.community?.mode === "Camp" ? <CampProfileProvider key={props.community.key} community={props.community}><ProfileContent {...props} /></CampProfileProvider> : <ProfileContent key={props.community?.key ?? "global"} {...props} />;
}
function ProfileContent({ community, communities, openGathering, bggAvailable, editRequest = 0, onEditRequestConsumed, initialTab, initialGameId, back }: ProfileProps) {
  const profile = useAsync(() => api<Profile>("/profile"), [community?.key]);
  const schedule = useAsync(() => profile.data ? api<ProfileGathering[]>("/profile/gatherings", { cache: "no-store" }) : Promise.resolve([]), [community?.key, Boolean(profile.data)]);
  const [tab, setTab] = useState(() => initialTab ?? new URLSearchParams(location.search).get("profileTab") ?? sessionStorage.getItem(`oyinq-profile-tab:${community?.key ?? "global"}`) ?? "collection");
  useEffect(() => telegram.back(Boolean(back), () => back?.()), [back]);
  const [showChangelog, setShowChangelog] = useState(false);
  useEffect(() => { sessionStorage.setItem(`oyinq-profile-tab:${community?.key ?? "global"}`, tab); }, [tab, community?.key]);
  useEffect(() => { if (editRequest > 0) { setTab("settings"); onEditRequestConsumed?.(); } }, [editRequest, onEditRequestConsumed]);
  const [name, setName] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  useEffect(() => { if (profile.data) setName(profile.data.preferredDisplayName ?? ""); }, [profile.data]);

  async function save() {
    if (busy) return;
    setBusy(true); setError(undefined);
    try {
      await api<Profile>("/profile", json("PUT", { displayName: name }));
      telegram.success("Профиль сохранён");
      profile.reload();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }

  if (showChangelog) return <ChangelogPage back={() => setShowChangelog(false)} />;
  if (profile.loading && !profile.data) return <Page title="Профиль"><Loading /></Page>;
  if (profile.error) return <Page title="Профиль"><ErrorState message={profile.error} retry={profile.reload} /></Page>;
  if (!profile.data) return null;
  return <Page title="Профиль">
    {back && <BackButton onClick={back} />}
    <BotStartNotice required={profile.data.botStartRequired} startUrl={profile.data.startUrl} refresh={profile.reload} />
    <ProfileTabs active={tab} select={setTab} />
    <div hidden={tab !== "collection"}><ProfileCollectionPage key={`collection-${community?.key ?? "global"}`} community={community} bggAvailable={bggAvailable} initialGameId={initialGameId} editRegistration={() => setTab("settings")} /></div>
    {tab === "wishes" && (community ? community.mode === "Camp" ? <CampPersonalWishes key={community.key} community={community} bggAvailable={bggAvailable} openGathering={id => openGathering(community.key, id)} /> : <WishlistPanel community={community} bggAvailable={bggAvailable} /> : <Empty>Выберите сообщество, чтобы посмотреть свои хотелки. <a href="?tab=communities">Выбрать сообщество</a></Empty>)}
    {tab === "settings" && <>
    {community?.mode === "Camp" && <CampPrivacy community={community} />}
    {!profile.data.botStartRequired && <Notice kind="success">Личный чат с ботом открыт</Notice>}
    <NotificationSettings />
    <section className="content-section form-grid">
      <Field label="Имя" hint="Так вас будут видеть в сборах, уведомлениях и других сообществах. Если оставить поле пустым, возьмём имя из Telegram.">
        <input maxLength={128} value={name} onChange={event => setName(event.target.value)} placeholder={profile.data.telegramDisplayName} />
      </Field>
      <div><span className="muted">В Telegram</span><p>{profile.data.telegramDisplayName}{profile.data.telegramUsername ? ` · @${profile.data.telegramUsername}` : ""}</p></div>
      {error && <Notice kind="danger">{error}</Notice>}
      <SaveButton busy={busy} label="Сохранить профиль" onClick={save} />
    </section>
    {community?.mode === "Camp" && <CampRegistrationSettings community={community} />}</>}
    {tab === "calendar" && <section className="profile-schedule"><h2>Моё расписание</h2>
      <button disabled={busy} onClick={async () => { setBusy(true); setError(undefined); try { await download("/profile/gatherings.ics", "oyinq-agenda.ics"); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }}>Скачать календарь (.ics)</button>
      <p className="muted">Файл текущего расписания. После изменений скачайте его заново; окончание игр оценочное.</p>
      {error && <Notice kind="danger">{error}</Notice>}
      <GatheringDashboard open={openGathering} />
      {schedule.loading ? <Loading /> : schedule.error ? <ErrorState message={schedule.error} retry={schedule.reload} /> : !schedule.data?.length ? <Empty>{profileScheduleEmptyText}</Empty> : <ProfileScheduleList items={schedule.data} communities={communities} open={openGathering} />}
      <PlayedHistory key={community?.key} communityKey={community?.key} open={openGathering} />
    </section>}
    <button className="ghost" onClick={() => setShowChangelog(true)}>Что нового?</button>
    <ProductFooter />
  </Page>;
}

export function ProfileTabs({ active, select }: { active: string; select: (tab: string) => void }) {
  return <Tabs className="profile-tabs" label="Разделы профиля" active={active} onChange={select} items={[
    { id: "collection", label: "Игры" }, { id: "wishes", label: "Хотелки" }, { id: "calendar", label: "Календарь" },
    { id: "settings", label: "Настройки" }
  ]} />;
}
