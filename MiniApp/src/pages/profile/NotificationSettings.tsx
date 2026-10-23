import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import { Field, Loading, Notice, ErrorState } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";

type Settings = { wishlistGathering: boolean; gatheringFull: boolean; gatheringDetailsChanged: boolean; organizerParticipantLeft: boolean;
  organizerReplacement: boolean; organizerBelowMinimum: boolean; organizerMissingProvider: boolean; importCompleted: boolean; reminderLeadMinutes: number };
const groups: { title: string; labels: [Exclude<keyof Settings, "reminderLeadMinutes">, string][] }[] = [
  { title: "Когда вы участвуете", labels: [
    ["wishlistGathering", "Сборы по хотелкам, предложения и просьбы о коробках"],
    ["gatheringFull", "Все места заняты"], ["gatheringDetailsChanged", "Изменились описание или условия"]
  ] },
  { title: "Когда вы организуете", labels: [
    ["organizerParticipantLeft", "Участник вышел из сбора"], ["organizerReplacement", "Пришёл игрок из листа ожидания"],
    ["organizerBelowMinimum", "Игроков стало меньше минимума"], ["organizerMissingProvider", "Никто не обещал принести коробку"]
  ] },
  { title: "Ваша коллекция", labels: [["importCompleted", "Завершена загрузка из BGG"]] }
];
export function NotificationSettings() {
  const state = useAsync(() => api<Settings>("/profile/notifications"), []);
  const [value, setValue] = useState<Settings>(); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  useEffect(() => { if (state.data) setValue(state.data); }, [state.data]);
  if (state.error) return <ErrorState message={state.error} retry={state.reload} />;
  if (!value) return <Loading />;
  async function save() { if (busy) return; setBusy(true); setError(undefined); try { await api("/profile/notifications", json("PUT", value)); telegram.success("Настройки уведомлений сохранены"); } catch (e) { setError(e instanceof Error ? e.message : "Не удалось сохранить настройки уведомлений."); } finally { setBusy(false); } }
  return <form className="content-section form-grid notification-settings" onSubmit={event => { event.preventDefault(); void save(); }}><h2>Уведомления</h2>
    <div className="notification-essential">Важные изменения приходят всегда. <details><summary>Какие именно?</summary><p>Место из листа ожидания, перенос времени, отмена и несостоявшийся сбор. Организатору также напоминаем подтвердить партию, администраторам сообщаем о недоступной теме публикаций.</p></details></div>
    <fieldset className="notification-reminder" disabled={busy}><Field label="Напомнить перед сбором" hint="Для ваших сборов и подтверждённых записей."><select value={value.reminderLeadMinutes} onChange={e => setValue({ ...value, reminderLeadMinutes: +e.target.value })}>{[[0,"Не напоминать"],[30,"За 30 минут"],[60,"За 1 час"],[120,"За 2 часа"],[360,"За 6 часов"],[720,"За 12 часов"],[1440,"За сутки"]].map(([minutes,label])=><option key={minutes} value={minutes}>{label}</option>)}</select></Field></fieldset>
    {groups.map(group => <fieldset className="notification-group" key={group.title} disabled={busy}><legend>{group.title}</legend>{group.labels.map(([key, label]) => <label className="check" key={key}><span>{label}</span><input type="checkbox" checked={value[key]} onChange={e => setValue({ ...value, [key]: e.target.checked })} /></label>)}</fieldset>)}
    {error && <Notice kind="danger">{error}</Notice>}<button className="primary" type="submit" disabled={busy} aria-busy={busy}>{busy ? "Сохраняем…" : "Сохранить настройки"}</button>
  </form>;
}
