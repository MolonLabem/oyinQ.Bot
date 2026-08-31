import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import type { Community, Profile } from "../../api/types";
import { Card, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";

export function ProfilePage({ community }: { community: Community }) {
  const profile = useAsync(() => api<Profile>(`/profile?community=${encodeURIComponent(community.key)}`), [community.key]);
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
  return <Page title="Профиль" subtitle="Единый профиль OyinQ">
    <Card className="form-grid">
      <Field label="Имя в OyinQ" hint="Используется в сборах, уведомлениях и во всех сообществах. Оставьте пустым, чтобы использовать имя из Telegram.">
        <input maxLength={128} value={name} onChange={event => setName(event.target.value)} placeholder={profile.data.telegramDisplayName} />
      </Field>
      <div><span className="muted">Имя в Telegram</span><p>{profile.data.telegramDisplayName}{profile.data.telegramUsername ? ` · @${profile.data.telegramUsername}` : ""}</p></div>
      {error && <Notice kind="danger">{error}</Notice>}
      <button className="primary" disabled={busy} onClick={save}>{busy ? "Сохраняем…" : "Сохранить профиль"}</button>
    </Card>
  </Page>;
}
