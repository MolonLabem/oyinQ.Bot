import { useEffect, useRef, useState } from "react";
import { api, json } from "../../api/client";
import type { Community, Profile } from "../../api/types";
import { attendanceLabel } from "../../components/CampBoxControl";
import { BackButton, ErrorState, Field, Loading, Notice, SaveButton } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { useBackButton } from "../../hooks/useBackButton";
import { telegram } from "../../telegram/webApp";
import { CampRegistrationForm, type RegistrationState } from "../camp/CampRegistration";
import { CampRegistrationCancellation } from "../camp/CampRegistrationCancellation";
import { CampPrivacy, useCampProfile } from "./CampProfileContext";
import { NotificationSettings } from "./NotificationSettings";
import { ProfileDeletion } from "./ProfileDeletion";

export type ProfileSettingSection = "identity" | "registration" | "privacy" | "notifications" | "cancel-registration" | "deletion";

export function ProfileSettings({ community, profile, reloadProfile, section, open }: {
  community?: Community; profile: Profile; reloadProfile: () => Promise<boolean>;
  section?: ProfileSettingSection; open: (section?: ProfileSettingSection) => void;
}) {
  const camp = community?.mode === "Camp" ? community : undefined;
  const campProfile = useCampProfile();
  const registration = useAsync(() => camp
    ? api<RegistrationState>(`/camp/registration?community=${encodeURIComponent(camp.key)}`)
    : Promise.resolve(undefined), [camp?.key]);
  const overview = useRef<HTMLElement>(null);
  const lastSection = useRef(section);
  useBackButton(Boolean(section), () => open());
  useEffect(() => {
    if (section) window.scrollTo({ top: 0, behavior: "auto" });
    else if (lastSection.current) (overview.current?.querySelector<HTMLButtonElement>(`[data-setting="${lastSection.current}"]`)
      ?? overview.current?.querySelector<HTMLButtonElement>("[data-setting]"))?.focus({ preventScroll: true });
    lastSection.current = section;
  }, [section]);

  if (section === "registration" && camp) return <div className="profile-settings-editor">
    {registration.loading ? <><BackButton onClick={() => open()} /><Loading /></>
      : registration.error || !registration.data ? <><BackButton onClick={() => open()} /><ErrorState message={registration.error ?? "Регистрация недоступна"} retry={registration.reload} /></>
      : <CampRegistrationForm community={camp} state={registration.data} cancel={() => open()}
          done={() => { void registration.reload(); void campProfile?.reload(); open(); }} />}
  </div>;
  if (section) return <div className="profile-settings-editor">
    <BackButton onClick={() => open()} />
    {section === "identity" && <IdentitySettings profile={profile} reload={reloadProfile} />}
    {section === "privacy" && camp && <CampPrivacy community={camp} />}
    {section === "notifications" && <NotificationSettings />}
    {section === "cancel-registration" && camp && <CampRegistrationCancellation community={camp}
      registered={Boolean(registration.data?.registration?.registered)} done={() => { void registration.reload(); void campProfile?.reload(); }} />}
    {section === "deletion" && <ProfileDeletion />}
  </div>;

  const saved = registration.data?.registration;
  const registrationSummary = registration.loading ? "Загружаем регистрацию…" : registration.error ? "Не удалось загрузить · откройте для повтора"
    : saved?.registered ? `${saved.selectedDates?.length ? attendanceLabel(saved.selectedDates) : "Регистрация сохранена"} · ${saved.needsAccommodation ? "с жильём" : "без жилья"}` : "Вы ещё не зарегистрированы";
  const privacy = campProfile?.data;
  const privacySummary = campProfile?.error ? "Не удалось загрузить · откройте настройки"
    : privacy ? `${privacy.shareCollection ? "Коллекция видна" : "Коллекция скрыта"} · ${privacy.shareWishes ? "Авторство хотелок видно" : "Хотелки без имени"}` : "Коллекция и авторство хотелок";
  return <section ref={overview} className="profile-settings-overview" aria-label="Разделы настроек">
    <SettingsRow id="identity" title="Имя в OyinQ" summary={profile.preferredDisplayName || profile.telegramDisplayName} open={open} />
    {camp && <>
      <SettingsRow id="registration" title="Регистрация на кэмп" summary={registrationSummary} open={open} />
      <SettingsRow id="privacy" title="Видимость в этом кэмпе" summary={privacySummary} open={open} />
    </>}
    <SettingsRow id="notifications" title="Уведомления" summary={profile.botStartRequired ? "Для личных уведомлений запустите бота" : "Личный чат с ботом открыт"} open={open} />
    {camp && saved?.registered && <SettingsRow id="cancel-registration" title="Отменить регистрацию" summary="Выход из кэмпа и отмена ваших будущих сборов" open={open} danger />}
    <SettingsRow id="deletion" title="Удалить профиль" summary="Личные данные во всех сообществах" open={open} danger />
  </section>;
}

function SettingsRow({ id, title, summary, open, danger = false }: {
  id: ProfileSettingSection; title: string; summary: string; open: (section: ProfileSettingSection) => void; danger?: boolean;
}) {
  return <button type="button" className={`settings-row${danger ? " danger" : ""}`} data-setting={id} onClick={() => open(id)}>
    <span><strong>{title}</strong><small>{summary}</small></span><span className="settings-chevron" aria-hidden>›</span>
  </button>;
}

function IdentitySettings({ profile, reload }: { profile: Profile; reload: () => Promise<boolean> }) {
  const [name, setName] = useState(profile.preferredDisplayName ?? "");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  async function save() {
    if (busy) return;
    setBusy(true); setError(undefined);
    try { await api<Profile>("/profile", json("PUT", { displayName: name })); telegram.success("Профиль сохранён"); await reload(); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }
  return <section className="content-section form-grid profile-identity">
    <h2>Как вас показывать</h2>
    <Field label="Имя" hint="Так вас видят в сборах и сообществах. Пустое поле вернёт имя из Telegram.">
      <input maxLength={128} value={name} onChange={event => setName(event.target.value)} placeholder={profile.telegramDisplayName} />
    </Field>
    <p><span className="muted">В Telegram</span><br />{profile.telegramDisplayName}{profile.telegramUsername ? ` · @${profile.telegramUsername}` : ""}</p>
    {error && <Notice kind="danger">{error}</Notice>}
    <SaveButton busy={busy} label="Сохранить профиль" onClick={save} />
  </section>;
}
