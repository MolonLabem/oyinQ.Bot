import { GatheringDashboard } from "../../components/GatheringDashboard";
import { PlayedHistory } from "./PlayedHistory";
import { NotificationSettings } from "./NotificationSettings";
import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import type { Community, Profile, ProfileGathering } from "../../api/types";
import { Card, Empty, ErrorState, Field, Loading, Notice, Page, ProductFooter } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { BotStartNotice } from "../../components/BotStartNotice";
import { ProfileCollectionPage, CampRegistrationSettings } from "./ProfileCollectionPage";
import { ProfileScheduleList, profileScheduleEmptyText } from "./ProfileScheduleList";

export function ProfilePage({ community, communities, openGathering, bggAvailable, editRequest = 0, onEditRequestConsumed }: { community?: Community; communities: Community[]; bggAvailable: boolean; editRequest?: number; onEditRequestConsumed?: () => void; openGathering: (communityKey: string, gatheringId: string) => void }) {
  const profile = useAsync(() => api<Profile>("/profile"), [community?.key]);
  const schedule = useAsync(() => profile.data ? api<ProfileGathering[]>("/profile/gatherings", { cache: "no-store" }) : Promise.resolve([]), [community?.key, Boolean(profile.data)]);
  const [tab, setTab] = useState("collection");
  useEffect(() => { if (editRequest > 0) { setTab("settings"); onEditRequestConsumed?.(); } }, [editRequest, onEditRequestConsumed]);
  const [name, setName] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  useEffect(() => { if (profile.data) setName(profile.data.preferredDisplayName ?? ""); }, [profile.data]);

  async function save() {
    setBusy(true); setError(undefined);
    try {
      await api<Profile>("/profile", json("PUT", { displayName: name }));
      telegram.success("Профиль сохранён");
      profile.reload();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }

  if (profile.loading && !profile.data) return <Page title="Профиль"><Loading /></Page>;
  if (profile.error) return <Page title="Профиль"><ErrorState message={profile.error} retry={profile.reload} /></Page>;
  if (!profile.data) return null;
  return <Page title="Профиль">
    <BotStartNotice required={profile.data.botStartRequired} startUrl={profile.data.startUrl} refresh={profile.reload} />
    {community && <GatheringDashboard communityKey={community.key} open={openGathering} />}
    <ProfileTabs active={tab} select={setTab} />
    {tab === "collection" && <ProfileCollectionPage key={community?.key} community={community} bggAvailable={bggAvailable} />}
    {tab === "settings" && <>
    {!profile.data.botStartRequired && <Notice kind="success">Уведомления Telegram: доступны</Notice>}
    <NotificationSettings />
    <Card className="form-grid">
      <Field label="Имя" hint="Так вас будут видеть в сборах, уведомлениях и других сообществах. Если оставить поле пустым, возьмём имя из Telegram.">
        <input maxLength={128} value={name} onChange={event => setName(event.target.value)} placeholder={profile.data.telegramDisplayName} />
      </Field>
      <div><span className="muted">В Telegram</span><p>{profile.data.telegramDisplayName}{profile.data.telegramUsername ? ` · @${profile.data.telegramUsername}` : ""}</p></div>
      {error && <Notice kind="danger">{error}</Notice>}
      <button className="primary" disabled={busy} onClick={save}>{busy ? "Сохраняем…" : "Сохранить профиль"}</button>
    </Card>
    {community?.mode === "Camp" && <CampRegistrationSettings community={community} />}</>}
    {tab === "calendar" && <section className="profile-schedule"><h2>Моё расписание</h2>
      {schedule.loading ? <Loading /> : schedule.error ? <ErrorState message={schedule.error} retry={schedule.reload} /> : !schedule.data?.length ? <Empty>{profileScheduleEmptyText}</Empty> : <ProfileScheduleList items={schedule.data} communities={communities} open={openGathering} />}
      <PlayedHistory key={community?.key} communityKey={community?.key} open={openGathering} />
    </section>}
    <ProductFooter />
  </Page>;
}

export function ProfileTabs({ active, select }: { active: string; select: (tab: string) => void }) {
  return <div className="segmented profile-tabs" role="tablist" aria-label="Разделы профиля">{[
    { id: "collection", label: "Моя коллекция" }, { id: "calendar", label: "Календарь" },
    { id: "settings", label: "Настройки" }
  ].map(value => <button key={value.id} role="tab" aria-selected={active === value.id}
    className={active === value.id ? "active" : ""} onClick={() => select(value.id)}>{value.label}</button>)}</div>;
}
