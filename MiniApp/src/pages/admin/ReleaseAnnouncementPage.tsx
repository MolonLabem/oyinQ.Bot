import { useState } from "react";
import { api, json } from "../../api/client";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { plural } from "../../app/format";
import { Page, Card, ErrorState, Loading, Notice } from "../../components/Ui";

type Target = { key: string; name: string; canPost: boolean; canQueue: boolean; canRetry: boolean; state?: string; error?: string };
type Preview = { releaseId: string; text: string; targets: Target[] };
const labels: Record<string, string> = { Pending: "В очереди", Preparing: "Подготовка", Delivering: "Отправляется", Delivered: "Отправлено", Failed: "Ошибка", DeliveryUnknown: "Проверьте чат вручную" };
export function ReleaseAnnouncementPage() { return <MessageDeliveryPage />; }

export function MessageDeliveryPage({ messageId }: { messageId?: string }) {
  const path = messageId ? `/admin/announcements/${messageId.replace(/^custom-/, "")}` : "/admin/release";
  const state = useAsync(() => api<Preview>(path), [path]);
  const [selected, setSelected] = useState<string[]>([]);
  const [review, setReview] = useState<{ keys: string[]; retry: boolean }>();
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  async function publish() {
    if (busy || !state.data || !review) return;
    setBusy(true); setError(undefined);
    try {
      if (!(await telegram.confirm(`Сообщение будет отправлено в ${plural(review.keys.length, "чат", "чата", "чатов")}. Опубликовать?`))) return;
      await api(messageId ? `${path}/queue` : path, json("POST", {
        releaseId: state.data.releaseId, communityKeys: review.keys, confirmed: true, retryFailed: review.retry,
      }));
      telegram.success("Сообщение добавлено в очередь отправки");
      setSelected([]); setReview(undefined); state.reload();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); }
  }
  if (state.loading) return <Loading />;
  if (state.error || !state.data) return <ErrorState message={state.error ?? "Сообщение недоступно"} retry={state.reload} />;
  const data = state.data;
  const queueable = selected.filter(key => data.targets.some(t => t.key === key && t.canQueue));
  return <Page as="section" title={messageId ? "Своё сообщение" : "Обновление OyinQ"} subtitle={messageId ? "Текст сохранён. Выберите получателей для отправки." : data.releaseId}><Card>
    {messageId && <pre className="release-preview">{data.text}</pre>}
    <p>Выберите управляемые сообщества. Успешные публикации повторно не отправляются.</p>
    {data.targets.map(t => <label className="check" key={t.key}><input type="checkbox" disabled={!t.canQueue || busy} checked={queueable.includes(t.key)} onChange={e => { setReview(undefined); setSelected(old => e.target.checked ? [...old, t.key] : old.filter(x => x !== t.key)); }} /><span>{t.name} · {t.state ? labels[t.state] : t.canPost ? "Готово к отправке" : "Нет доступа для публикации"}{t.error && <small>{t.error}</small>}</span></label>)}
    {!data.targets.length && <Notice>Нет сообществ для отправки.</Notice>}
    <p>Отправлено: {data.targets.filter(x => x.state === "Delivered").length} · Ошибка: {data.targets.filter(x => x.state === "Failed").length}</p>
    <div className="row">
      <button disabled={busy} onClick={() => { setReview(undefined); state.reload(); }}>Обновить результат доставки</button>
      <button disabled={!queueable.length || busy} onClick={() => setReview({ keys: queueable, retry: false })}>Предпросмотр</button>
      {data.targets.some(x => x.canRetry) && <button disabled={busy} onClick={() => setReview({ keys: data.targets.filter(x => x.canRetry).map(x => x.key), retry: true })}>Повторить ошибочные</button>}
    </div>
    {review && <section className="page-section" aria-label="Предпросмотр сообщения"><h2>{review.retry ? "Повторная отправка" : "Перед отправкой"}</h2><pre className="release-preview">{data.text}</pre><p>Кнопка под сообщением: «Открыть OyinQ»</p><p>Получатели: {data.targets.filter(x => review.keys.includes(x.key)).map(x => x.name).join(", ")}</p><div className="row"><button className="primary" disabled={busy} onClick={publish}>{busy ? "Добавляем в очередь…" : "Опубликовать"}</button><button disabled={busy} onClick={() => setReview(undefined)}>Отмена</button></div></section>}
    {data.targets.some(x => x.state === "DeliveryUnknown") && <Notice kind="warning">Результат части отправок неизвестен. Проверьте эти чаты вручную: автоматического повтора не будет.</Notice>}
    {error && <Notice kind="danger">{error}</Notice>}
  </Card></Page>;
}
