import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import { Card, Field, Loading, Notice, ErrorState } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";

type Settings = { gatheringFull: boolean; gatheringDetailsChanged: boolean; organizerParticipantLeft: boolean;
  organizerReplacement: boolean; organizerBelowMinimum: boolean; organizerMissingProvider: boolean; importCompleted: boolean; reminderLeadMinutes: number };
const labels: [Exclude<keyof Settings, "reminderLeadMinutes">, string][] = [
  ["gatheringFull", "Сбор полностью набран"], ["gatheringDetailsChanged", "Изменились описание или условия сбора"],
  ["organizerParticipantLeft", "Участник вышел из моего сбора"], ["organizerReplacement", "Освободившееся место занял человек из листа ожидания"],
  ["organizerBelowMinimum", "В моём сборе стало меньше игроков, чем нужно"], ["organizerMissingProvider", "Для моего сбора не подтверждена коробка"],
  ["importCompleted", "Завершена загрузка коллекции BGG"]
];
export function NotificationSettings() {
  const state = useAsync(() => api<Settings>("/profile/notifications"), []);
  const [value, setValue] = useState<Settings>(); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  useEffect(() => { if (state.data) setValue(state.data); }, [state.data]);
  if (state.error) return <ErrorState message={state.error} retry={state.reload} />;
  if (!value) return <Loading />;
  async function save() { setBusy(true); setError(undefined); try { await api("/profile/notifications", json("PUT", value)); telegram.success("Настройки уведомлений сохранены"); } catch (e) { setError(String(e)); } finally { setBusy(false); } }
  return <Card className="form-grid"><h2>Уведомления</h2>
    <Notice>Освобождение места в листе ожидания, перенос времени, отмена и несостоявшийся сбор — обязательные сообщения. Их нельзя отключить. Администраторам также сообщаем о недоступной теме публикаций.</Notice>
    {labels.map(([key, label]) => <label className="check" key={key}><input type="checkbox" checked={value[key]} onChange={e => setValue({ ...value, [key]: e.target.checked })} />{label}</label>)}
    <Field label="Напомнить перед сбором" hint="Для сборов, которые вы организуете или в которые записаны. В листе ожидания напоминаний нет."><select value={value.reminderLeadMinutes} onChange={e => setValue({ ...value, reminderLeadMinutes: +e.target.value })}>{[[0,"Не напоминать"],[30,"За 30 минут"],[60,"За 1 час"],[120,"За 2 часа"],[360,"За 6 часов"],[720,"За 12 часов"],[1440,"За сутки"]].map(([minutes,label])=><option key={minutes} value={minutes}>{label}</option>)}</select></Field>
    {error && <Notice kind="danger">{error}</Notice>}<button disabled={busy} onClick={save}>Сохранить уведомления</button>
  </Card>;
}
