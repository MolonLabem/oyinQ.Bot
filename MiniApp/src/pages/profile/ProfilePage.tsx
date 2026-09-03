import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import type { Community, Profile, ProfileGathering } from "../../api/types";
import { Card, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { ProfileScheduleList, profileScheduleEmptyText } from "./ProfileScheduleList";

export function ProfilePage({ community, communities, openGathering }: { community: Community; communities: Community[]; openGathering: (communityKey: string, gatheringId: string) => void }) {
  const profile = useAsync(() => api<Profile>(`/profile?community=${encodeURIComponent(community.key)}`), [community.key]);
  const schedule = useAsync(() => profile.data ? api<ProfileGathering[]>(`/profile/gatherings?community=${encodeURIComponent(community.key)}`, { cache: "no-store" }) : Promise.resolve([]), [community.key, Boolean(profile.data)]);
  const [name, setName] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  useEffect(() => { if (profile.data) setName(profile.data.preferredDisplayName ?? ""); }, [profile.data]);

  async function save() {
    setBusy(true); setError(undefined);
    try {
      await api<Profile>("/profile", json("PUT", { communityKey: community.key, displayName: name }));
      telegram.success("Профиль сохранён");
      profile.reload();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }

  if (profile.loading && !profile.data) return <Page title="Профиль"><Loading /></Page>;
  if (profile.error) return <Page title="Профиль"><ErrorState message={profile.error} retry={profile.reload} /></Page>;
  if (!profile.data) return null;
  return <Page title="Профиль" subtitle="Ваше имя для всех сообществ">
    <Card className="form-grid">
      <Field label="Имя" hint="Так вас будут видеть в сборах, уведомлениях и других сообществах. Если оставить поле пустым, возьмём имя из Telegram.">
        <input maxLength={128} value={name} onChange={event => setName(event.target.value)} placeholder={profile.data.telegramDisplayName} />
      </Field>
      <div><span className="muted">В Telegram</span><p>{profile.data.telegramDisplayName}{profile.data.telegramUsername ? ` · @${profile.data.telegramUsername}` : ""}</p></div>
      {error && <Notice kind="danger">{error}</Notice>}
      <button className="primary" disabled={busy} onClick={save}>{busy ? "Сохраняем…" : "Сохранить профиль"}</button>
    </Card>
    <section className="profile-schedule"><h2>Моё расписание</h2>
      {schedule.loading ? <Loading /> : schedule.error ? <ErrorState message={schedule.error} retry={schedule.reload} /> : !schedule.data?.length ? <Empty>{profileScheduleEmptyText}</Empty> : <ProfileScheduleList items={schedule.data} communities={communities} open={openGathering} />}
    </section>
  </Page>;
}
