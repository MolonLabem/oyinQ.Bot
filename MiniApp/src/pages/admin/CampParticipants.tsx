import { useState } from "react";
import { json } from "../../api/client";
import type { CampAdminParticipants, CampParticipantDmResult } from "../../api/types";
import { BackButton, Badge, Card, ContactLink, Empty, ErrorState, Loading, Notice, Page } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { useScreenRequest } from "../../hooks/useScreenRequest";
import { telegram } from "../../telegram/webApp";
import { formatDate, plural } from "../../app/format";

export function CampParticipants({ campId, campName, back }: { campId: number; campName: string; back: () => void }) {
  const api = useScreenRequest();
  const state = useAsync(() => api<CampAdminParticipants>(`/admin/camps/${campId}/participants`), [campId]);
  const [sending, setSending] = useState(false);
  const [sendError, setSendError] = useState<string>();
  async function sendToMe() {
    if (sending) return;
    setSending(true);
    setSendError(undefined);
    try {
      const result = await api<CampParticipantDmResult>(`/admin/camps/${campId}/participants/send-to-me`, json("POST"));
      telegram.success(result.participantCount
        ? `Список отправлен: ${plural(result.participantCount, "участник", "участника", "участников")}`
        : "Пустой список отправлен");
    } catch (error) {
      setSendError(error instanceof Error ? error.message : String(error));
    } finally {
      setSending(false);
    }
  }
  return (
    <Page
      title="Участники кэмпа"
      subtitle={state.data?.campName ?? campName}
      actions={<BackButton onClick={back} />}
    >
      <Card>
        <div className="row">
          <div>
            <strong>Список в личный чат</strong>
            <p className="muted">Бот отправит эти данные вам в Telegram.</p>
          </div>
          <button className="primary" disabled={sending || state.loading || Boolean(state.error)} onClick={sendToMe}>
            {sending ? "Отправляем…" : "Отправить мне"}
          </button>
        </div>
        {sendError && <Notice kind="danger">{sendError}</Notice>}
      </Card>
      {state.loading ? (
        <Loading />
      ) : state.error ? (
        <ErrorState message={state.error} retry={state.reload} />
      ) : !state.data?.participants.length ? (
        <Empty>Пока никто не зарегистрировался.</Empty>
      ) : (
        <div className="admin-entity-grid">
          {state.data.participants.map((participant) => (
            <Card key={participant.participantId}>
              <div className="row">
                <h3><ContactLink url={participant.contactUrl}>{participant.displayName}</ContactLink></h3>
                <Badge tone={participant.needsAccommodation ? "attention" : "neutral"}>
                  {participant.needsAccommodation ? "Нужно жильё" : "Жильё не нужно"}
                </Badge>
              </div>
              {participant.telegramUsername && <p className="muted">@{participant.telegramUsername}</p>}
              <p><strong>Город:</strong> {participant.city || "не указан"}</p>
              <p><strong>Даты:</strong> {participant.selectedDates.length ? participant.selectedDates.map(formatDate).join(", ") : "не указаны"}</p>
            </Card>
          ))}
        </div>
      )}
    </Page>
  );
}
