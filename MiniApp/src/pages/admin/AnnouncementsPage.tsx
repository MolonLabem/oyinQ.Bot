import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import { useAsync } from "../../hooks/useAsync";
import { Card, Empty, ErrorState, Field, Loading, Notice, Page, Tabs } from "../../components/Ui";
import { MessageDeliveryPage, ReleaseAnnouncementPage } from "./ReleaseAnnouncementPage";

const draftKey = "oyinq-announcement-draft";
const maxLength = 3500;
type Draft = { requestId: string; text: string; submitted: boolean };
type History = { items: { id: string; text: string; createdAt: string }[]; hasNext: boolean };
function newDraft(): Draft { return { requestId: crypto.randomUUID(), text: "", submitted: false }; }
function readDraft(): Draft {
  try {
    const value = JSON.parse(localStorage.getItem(draftKey) ?? "null") as Draft | null;
    if (value && typeof value.requestId === "string" && typeof value.text === "string" && typeof value.submitted === "boolean") return value;
  } catch { /* An unavailable or outdated local draft does not block the editor. */ }
  return newDraft();
}

export function AnnouncementsPage() {
  const [tab, setTab] = useState("custom");
  const [messageId, setMessageId] = useState<string>();
  return <Page title="Оповещения" subtitle="Сообщения в Telegram-группы выбранных сообществ">
    <Tabs label="Раздел оповещений" className="announcement-tabs" active={tab} onChange={setTab} items={[
      { id: "custom", label: "Текст" }, { id: "release", label: "Выпуск" }, { id: "history", label: "История" },
    ]} />
    {tab === "release" ? <ReleaseAnnouncementPage /> : tab === "history"
      ? <AnnouncementHistory open={id => { setMessageId(id); setTab("custom"); }} />
      : messageId ? <>
        <button onClick={() => setMessageId(undefined)}>Написать новое сообщение</button>
        <MessageDeliveryPage key={messageId} messageId={messageId} />
      </> : <MessageEditor saved={setMessageId} />}
  </Page>;
}

function MessageEditor({ saved }: { saved: (id: string) => void }) {
  const [draft, setDraft] = useState(readDraft);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  useEffect(() => { try { localStorage.setItem(draftKey, JSON.stringify(draft)); } catch { /* Server saving remains available. */ } }, [draft]);
  async function save() {
    if (busy || !draft.text.trim() || draft.text.length > maxLength) return;
    setBusy(true); setError(undefined);
    const submitted = { ...draft, submitted: true };
    setDraft(submitted);
    try {
      const result = await api<{ id: string }>("/admin/announcements", json("POST", { requestId: submitted.requestId, text: submitted.text }));
      try { localStorage.removeItem(draftKey); } catch { /* The same request remains safe to repeat. */ }
      saved(result.id);
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); }
  }
  return <Card>
    <Field label="Текст сообщения" hint={`Обычный текст, без HTML и Markdown. ${draft.text.length} / ${maxLength} символов.`}>
      <textarea rows={9} maxLength={maxLength} value={draft.text} disabled={busy || draft.submitted}
        onChange={e => setDraft({ ...draft, text: e.target.value })} placeholder="Напишите объявление для участников…" />
    </Field>
    <Notice>Сохранение не отправляет сообщение. На следующем шаге можно выбрать сообщества и проверить текст. После сохранения текст не изменяется.</Notice>
    <button className="primary" disabled={busy || !draft.text.trim() || draft.text.length > maxLength} onClick={save}>
      {busy ? "Сохраняем…" : draft.submitted ? "Повторить сохранение" : "Сохранить и выбрать получателей"}
    </button>
    {draft.submitted && !busy && <button onClick={() => { setDraft(newDraft()); setError(undefined); }}>Написать другое сообщение</button>}
    {error && <Notice kind="danger">{error} Сохранённое сообщение можно найти в истории.</Notice>}
  </Card>;
}

function AnnouncementHistory({ open }: { open: (id: string) => void }) {
  const [page, setPage] = useState(1);
  const state = useAsync(() => api<History>(`/admin/announcements?page=${page}`), [page]);
  return <>
    <p className="muted">Сохранённые сообщения. Откройте сообщение, чтобы выбрать получателей или проверить доставку.</p>
    {state.loading ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} />
      : !state.data?.items.length ? <Empty>Сообщений пока нет.</Empty>
      : <div className="stack">{state.data.items.map(item => <button className="card announcement-history-item" key={item.id} onClick={() => open(item.id)}>
        <small>{new Date(item.createdAt).toLocaleString("ru-RU")}</small><p>{item.text}</p><span>Открыть сообщение →</span>
      </button>)}</div>}
    <div className="row page-section">
      <button disabled={state.loading || page === 1} onClick={() => setPage(page - 1)}>Назад</button>
      <span>Страница {page}</span>
      <button disabled={state.loading || !state.data?.hasNext} onClick={() => setPage(page + 1)}>Далее</button>
    </div>
  </>;
}
