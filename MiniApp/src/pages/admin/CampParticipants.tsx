import { useEffect, useRef, useState } from "react";
import { ApiError, download, json } from "../../api/client";
import type { CampAdminParticipants, CampParticipantDmResult } from "../../api/types";
import { BackButton, Card, ContactLink, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { useAsync, useDebouncedValue } from "../../hooks/useAsync";
import { useScreenRequest } from "../../hooks/useScreenRequest";
import { telegram } from "../../telegram/webApp";
import { formatDate, formatShortDate, plural } from "../../app/format";

type Props = { campId: number; campName: string; back: () => void };
export function CampParticipants(props: Props) { return <CampRoster key={props.campId} {...props} />; }

function CampRoster({ campId, campName, back }: Props) {
  const api = useScreenRequest();
  const [search, setSearch] = useState("");
  const [date, setDate] = useState("");
  const [accommodation, setAccommodation] = useState("");
  const debouncedSearch = useDebouncedValue(search, 250);
  const filters = new URLSearchParams();
  if (debouncedSearch.trim()) filters.set("search", debouncedSearch.trim());
  if (date) filters.set("attendanceDate", date);
  if (accommodation) filters.set("accommodation", accommodation);
  const query = filters.toString();
  const path = `/admin/camps/${campId}/participants`;
  const state = useAsync(async () => ({ query, roster: await api<CampAdminParticipants>(`${path}${query ? `?${query}` : ""}`) }), [campId, query]);
  const data = state.data?.roster;
  const current = !state.loading && !state.error && state.data?.query === query && debouncedSearch === search;
  const activeFilters = Boolean(search || date || accommodation);
  const [scope, setScope] = useState("all");
  const [format, setFormat] = useState("xlsx");
  const [busy, setBusy] = useState<string>();
  const busyRef = useRef(false);
  const [error, setError] = useState<string>();
  const [checkDelivery, setCheckDelivery] = useState(false);
  const lifetime = useRef<AbortController>(null);
  useEffect(() => { const controller = new AbortController(); lifetime.current = controller; return () => controller.abort(); }, []);
  const count = scope === "all" ? data?.totalCount : data?.participants.length;
  const exportQuery = new URLSearchParams(scope === "filtered" ? query : "");
  exportQuery.set("scope", scope);

  async function perform(action: "download" | "file" | "message") {
    if (busyRef.current || !current || (action !== "download" && checkDelivery)) return;
    busyRef.current = true; setBusy(action); setError(undefined);
    const signal = lifetime.current?.signal;
    try {
      if (action === "download") {
        await download(`${path}/export/${format}?${exportQuery}`, `Участники.${format}`, signal);
      } else {
        const suffix = action === "file" ? `/export/${format}/send-to-me` : "/send-to-me";
        const result = await api<CampParticipantDmResult>(`${path}${suffix}?${exportQuery}`, { ...json("POST"), signal });
        if (!signal?.aborted) telegram.success(`${action === "file" ? "Файл отправлен" : "Список отправлен"}: ${plural(result.participantCount, "участник", "участника", "участников")}`);
      }
    } catch (failure) {
      if (signal?.aborted) return;
      const uncertain = action !== "download" && (!(failure instanceof ApiError) || failure.status === 0
        || failure.code === "camp_participant_delivery_unknown" || failure.code === "camp_participant_delivery_partial"
        || (failure.status >= 500 && failure.code !== "private_chat_required" && failure.code !== "camp_participant_delivery_failed"));
      if (uncertain) setCheckDelivery(true);
      setError(uncertain && (!(failure instanceof ApiError) || failure.status === 0 || !failure.code)
        ? "Не удалось подтвердить доставку. Проверьте личный чат с ботом перед повторной отправкой: сообщение могло дойти."
        : failure instanceof Error ? failure.message : String(failure));
    } finally {
      busyRef.current = false;
      if (!signal?.aborted) setBusy(undefined);
    }
  }

  return <Page title="Участники кэмпа" subtitle={data?.campName ?? campName} actions={<BackButton onClick={back} />}>
    <Card className="camp-roster-filters">
      <Field label="Поиск участников"><input type="search" placeholder="Имя, Telegram или город" maxLength={200} value={search} onChange={event => setSearch(event.target.value)} /></Field>
      <div className="camp-roster-filter-row">
        <Field label="Дата участия"><select value={date} onChange={event => setDate(event.target.value)}><option value="">Все даты</option>
          {(data?.availableDates ?? []).map(value => <option key={value} value={value}>{formatDate(value)}</option>)}
        </select></Field>
        <Field label="Жильё"><select value={accommodation} onChange={event => setAccommodation(event.target.value)}>
          <option value="">Все варианты</option><option value="needed">Нужно</option><option value="not-needed">Не нужно</option><option value="unanswered">Не указано</option>
        </select></Field>
      </div>
      <div className="row"><span role="status">{current && data ? `Показано ${data.participants.length} из ${data.totalCount}` : "Обновляем список…"}</span>
        {activeFilters && <button className="ghost" onClick={() => { setSearch(""); setDate(""); setAccommodation(""); }}>Сбросить фильтры</button>}
      </div>
    </Card>
    <Card className="camp-roster-export">
      <details><summary>Экспорт и отправка</summary><div className="camp-roster-export-options">
      <Field label="Кого включить"><select value={scope} onChange={event => setScope(event.target.value)} disabled={Boolean(busy)}>
        <option value="all">Все участники кэмпа</option><option value="filtered">Текущий результат фильтра</option>
      </select></Field>
      <p className="muted">{scope === "all" ? "Все участники кэмпа" : data?.filterDescription ?? "Текущий результат фильтра"} · {current ? plural(count ?? 0, "участник", "участника", "участников") : "…"}</p>
      <Field label="Формат файла"><select value={format} onChange={event => setFormat(event.target.value)} disabled={Boolean(busy)}>
        <option value="xlsx">Excel (.xlsx)</option><option value="csv">CSV</option>
      </select></Field>
      <div className="camp-roster-actions">
        <button className="primary" disabled={!current || Boolean(busy)} onClick={() => perform("download")}>{busy === "download" ? "Готовим файл…" : format === "xlsx" ? "Скачать Excel" : "Скачать CSV"}</button>
        <button disabled={!current || Boolean(busy) || checkDelivery} onClick={() => perform("file")}>{busy === "file" ? "Отправляем…" : "Отправить файл мне"}</button>
        <button disabled={!current || Boolean(busy) || checkDelivery} onClick={() => perform("message")}>{busy === "message" ? "Отправляем…" : "Отправить мне"}</button>
      </div>
      <p className="muted">«Отправить мне» — таблица сообщением. Если телефон не сохраняет файл, отправьте его себе в личный чат с ботом.</p>
      {error && <Notice kind="danger">{error}</Notice>}
      {checkDelivery && <button onClick={() => setCheckDelivery(false)}>Проверил личный чат</button>}
      </div></details>
    </Card>
    {!current && !state.error ? <Loading /> : state.error ? <ErrorState message={state.error} retry={state.reload} />
      : !data?.participants.length ? <Empty>{data?.totalCount ? "По выбранным условиям участников нет. Измените или сбросьте фильтры." : "Пока никто не зарегистрировался."}</Empty>
      : <div className="camp-roster-scroll" role="region" aria-label="Список участников" tabIndex={0}>
        <table className="camp-roster-table">
          <caption>Участники кэмпа · {data.campName}</caption>
          <thead><tr><th scope="col">Участник</th><th scope="col" className="camp-roster-wide">Город</th><th scope="col" className="camp-roster-wide">Telegram</th><th scope="col" className="camp-roster-dates">Даты</th><th scope="col" className="camp-roster-housing">Жильё</th></tr></thead>
          <tbody>{data.participants.map(person => <tr key={person.participantId}>
            <th scope="row"><ContactLink url={person.contactUrl}>{person.displayName}</ContactLink>
              <span className="camp-roster-secondary camp-roster-mobile">{person.city || "Город не указан"}<br />{person.telegramUsername ? `@${person.telegramUsername}` : "Telegram не указан"}</span>
            </th>
            <td className="camp-roster-wide">{person.city || "Не указан"}</td>
            <td className="camp-roster-wide"><ContactLink url={person.contactUrl}>{person.telegramUsername ? `@${person.telegramUsername}` : "Открыть профиль"}</ContactLink></td>
            <td>{person.selectedDates.length ? person.selectedDates.map(value => <time className="camp-roster-date" dateTime={value} title={formatDate(value)} key={value}>{formatShortDate(value)}</time>) : "Не указаны"}
              <span className="camp-roster-secondary">Дней: {person.dayCount}</span></td>
            <td><span className={person.needsAccommodation === true ? "camp-roster-needed" : undefined}>{person.accommodationText}</span></td>
          </tr>)}</tbody>
        </table>
      </div>}
  </Page>;
}
