import { useState } from "react";
import { api, json } from "../../api/client";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { Page, Card, ErrorState, Loading, Notice } from "../../components/Ui";

type Target = { key: string; name: string; canPost: boolean; canQueue: boolean; canRetry: boolean; state?: string; error?: string };
type Preview = { releaseId: string; text: string; targets: Target[] };
const labels: Record<string, string> = { Pending: "В очереди", Preparing: "Подготовка", Delivering: "Отправляется", Delivered: "Отправлено", Failed: "Ошибка", DeliveryUnknown: "Проверьте чат вручную" };
export function ReleaseAnnouncementPage() {
  const state = useAsync(() => api<Preview>("/admin/release"), []);
  const [selected, setSelected] = useState<string[]>([]); const [preview, setPreview] = useState(false);
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  async function publish(retry: boolean) {
    if (!state.data) return;
    const keys = retry ? state.data.targets.filter(x => x.canRetry).map(x => x.key) : selected;
    if (!keys.length || !(await telegram.confirm(`Сообщение будет отправлено в ${keys.length} чата. Опубликовать?`))) return;
    setBusy(true); setError(undefined);
    try { await api("/admin/release", json("POST", { releaseId: state.data.releaseId, communityKeys: keys, confirmed: true, retryFailed: retry })); setSelected([]); setPreview(false); state.reload(); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); }
  }
  if (state.loading) return <Loading />;
  if (state.error || !state.data) return <ErrorState message={state.error ?? "Выпуск недоступен"} retry={state.reload} />;
  const data = state.data;
  return <Page title="Обновление OyinQ" subtitle={data.releaseId}><Card>
    <p>Выберите управляемые сообщества. Успешные публикации повторно не отправляются.</p>
    {data.targets.map(t => <label className="check" key={t.key}><input type="checkbox" disabled={!t.canQueue || busy} checked={selected.includes(t.key)} onChange={e => { setPreview(false); setSelected(old => e.target.checked ? [...old, t.key] : old.filter(x => x !== t.key)); }} />{t.name} · {t.state ? labels[t.state] : t.canPost ? "Готово к отправке" : "Нет доступа для публикации"}{t.error && <small>{t.error}</small>}</label>)}
    <p>Отправлено: {data.targets.filter(x => x.state === "Delivered").length} · Ошибка: {data.targets.filter(x => x.state === "Failed").length}</p>
    <button disabled={busy} onClick={state.reload}>Обновить результат доставки</button>
    <button disabled={!selected.length || busy} onClick={() => setPreview(true)}>Предпросмотр</button>
    {preview && <><pre className="release-preview">{data.text}</pre><p>Кнопка под сообщением: «Открыть OyinQ»</p><p>Получатели: {data.targets.filter(x => selected.includes(x.key)).map(x => x.name).join(", ")}</p><button className="primary" disabled={busy} onClick={() => publish(false)}>Опубликовать</button><button disabled={busy} onClick={() => setPreview(false)}>Отмена</button></>}
    {data.targets.some(x => x.canRetry) && <button disabled={busy} onClick={() => publish(true)}>Повторить ошибочные</button>}
    {data.targets.some(x => x.state === "DeliveryUnknown") && <Notice kind="warning">Результат части отправок неизвестен. Проверьте эти чаты вручную: автоматического повтора не будет.</Notice>}
    {error && <Notice kind="danger">{error}</Notice>}
  </Card></Page>;
}
