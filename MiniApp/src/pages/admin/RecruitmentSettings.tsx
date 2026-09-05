import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import { Card, ErrorState, Field, Loading, Notice } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";

export function RecruitmentSettings({ communityKey }: { communityKey: string }) {
  const url = `/admin/communities/${encodeURIComponent(communityKey)}/recruitment`;
  const state = useAsync(() => api<{ hours: number }>(url), [url]);
  const [hours, setHours] = useState(4); const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  useEffect(() => { if (state.data) setHours(state.data.hours); }, [state.data]);
  async function save() {
    if (busy) return;
    setBusy(true); setError(undefined);
    try { await api(url, json("PUT", { hours })); telegram.success("Интервал напоминаний сохранён"); state.reload(); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }
  return <Card><h2>Напоминания о сборах</h2>
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} /> : <>
      <Field label="Интервал между напоминаниями о сборах" hint="Общий для всех организаторов этого сообщества. Напоминание отправляется только по запросу организатора.">
        <select value={hours} onChange={e => setHours(+e.target.value)}>{Array.from({ length: 24 }, (_, i) => i + 1).map(value =>
          <option value={value} key={value}>{value} {value === 1 || value === 21 ? "час" : value < 5 || value > 21 ? "часа" : "часов"}</option>)}</select>
      </Field><button className="primary" disabled={busy} aria-busy={busy} onClick={() => void save()}>{busy ? "Сохраняем…" : "Сохранить интервал"}</button></>}
    {error && <Notice kind="danger">{error}</Notice>}
  </Card>;
}
