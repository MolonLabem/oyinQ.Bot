import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from "react";
import { api, download } from "../../api/client";
import type { Community, ProfileGathering } from "../../api/types";
import { currentLocalMinute, formatDate, plural } from "../../app/format";
import { GatheringDashboard } from "../../components/GatheringDashboard";
import { Empty, ErrorState, Loading, Notice } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { useRefresh } from "../../hooks/useRefreshOnActivity";
import { formatScheduleDay, ProfileScheduleList, profileScheduleEmptyText } from "./ProfileScheduleList";

function civilDate(value: string) { return new Date(`${value}T00:00:00Z`); }
function dayKey(date: Date) { return date.toISOString().slice(0, 10); }
function moveDay(value: string, offset: number) {
  const date = civilDate(value); date.setUTCDate(date.getUTCDate() + offset); return dayKey(date);
}
function moveMonth(value: string, offset: number) {
  const date = civilDate(value);
  const month = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + offset, 1));
  const last = new Date(Date.UTC(month.getUTCFullYear(), month.getUTCMonth() + 1, 0)).getUTCDate();
  month.setUTCDate(Math.min(date.getUTCDate(), last)); return dayKey(month);
}
function initialDate(dates: string[], today: string, storageKey: string) {
  try {
    const saved = sessionStorage.getItem(storageKey);
    if (saved && /^\d{4}-\d{2}-\d{2}$/.test(saved) && !Number.isNaN(civilDate(saved).getTime()) && dayKey(civilDate(saved)) === saved) return saved;
  } catch { /* Calendar navigation also works when storage is unavailable. */ }
  return dates.find(date => date >= today) ?? dates.at(-1) ?? today;
}

export function ProfileCalendar({ communities, timeZoneId, contextKey = "global", open }: {
  communities: Community[]; timeZoneId?: string; contextKey?: string; open: (key: string, id: string) => void;
}) {
  const state = useAsync(() => api<ProfileGathering[]>("/profile/gatherings", { cache: "no-store" }), []);
  useRefresh(state.reload);
  return <section className="profile-calendar" aria-label="Календарь сборов">
    {!state.data ? state.error ? <ErrorState message={state.error} retry={state.reload} /> : <Loading /> : <>
      {state.error && <ErrorState message={state.error} retry={state.reload} />}
      <CalendarMonth items={state.data} communities={communities} timeZoneId={timeZoneId} contextKey={contextKey} open={open} />
    </>}
    <GatheringDashboard open={open} collapsible />
  </section>;
}

function CalendarMonth({ items, communities, timeZoneId, contextKey, open }: {
  items: ProfileGathering[]; communities: Community[]; timeZoneId?: string; contextKey: string; open: (key: string, id: string) => void;
}) {
  const dates = [...new Set(items.map(item => item.localDate))].sort();
  const today = currentLocalMinute(timeZoneId ?? Intl.DateTimeFormat().resolvedOptions().timeZone).slice(0, 10);
  const storageKey = `oyinq-profile-calendar:${contextKey}`;
  const [selected, setSelected] = useState(() => initialDate(dates, today, storageKey));
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState<string>();
  const grid = useRef<HTMLTableElement>(null);
  const focusPending = useRef(false);
  const monthId = useId();
  useEffect(() => { try { sessionStorage.setItem(storageKey, selected); } catch { /* Storage is optional. */ } }, [storageKey, selected]);
  useLayoutEffect(() => {
    if (focusPending.current) {
      focusPending.current = false;
      grid.current?.querySelector<HTMLButtonElement>(`[data-calendar-date="${selected}"]`)?.focus({ preventScroll: true });
    }
  }, [selected]);
  const date = civilDate(selected);
  const start = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1));
  const leading = (start.getUTCDay() + 6) % 7;
  const length = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0)).getUTCDate();
  const cells = Array.from({ length: Math.ceil((leading + length) / 7) * 7 }, (_, index) =>
    index < leading || index >= leading + length ? undefined : moveDay(dayKey(start), index - leading));
  const rows = Array.from({ length: cells.length / 7 }, (_, index) => cells.slice(index * 7, index * 7 + 7));
  const monthLabel = new Intl.DateTimeFormat("ru-RU", { month: "long", year: "numeric", timeZone: "UTC" }).format(start);
  const selectedItems = items.filter(item => item.localDate === selected);
  const nearest = dates.find(value => value >= today) ?? dates.at(-1);
  function choose(value: string, focus = false) { focusPending.current = focus; setSelected(value); }
  function month(offset: number) {
    const first = `${moveMonth(selected, offset).slice(0, 7)}-01`;
    choose(dates.find(value => value.startsWith(first.slice(0, 7))) ?? first);
  }
  function keyDown(event: KeyboardEvent<HTMLButtonElement>, value: string) {
    const weekday = (civilDate(value).getUTCDay() + 6) % 7;
    const next = event.key === "ArrowLeft" ? moveDay(value, -1) : event.key === "ArrowRight" ? moveDay(value, 1)
      : event.key === "ArrowUp" ? moveDay(value, -7) : event.key === "ArrowDown" ? moveDay(value, 7)
      : event.key === "Home" ? moveDay(value, -weekday) : event.key === "End" ? moveDay(value, 6 - weekday)
      : event.key === "PageUp" ? moveMonth(value, -1) : event.key === "PageDown" ? moveMonth(value, 1) : undefined;
    if (next) { event.preventDefault(); choose(next, true); }
  }
  async function exportCalendar() {
    if (exporting) return;
    setExporting(true); setError(undefined);
    try { await download("/profile/gatherings.ics", "oyinq-agenda.ics"); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setExporting(false); }
  }
  return <>
    <div className="calendar-month-toolbar">
      <button type="button" aria-label="Предыдущий месяц" onClick={() => month(-1)}><svg viewBox="0 0 16 16" aria-hidden="true"><path d="m10 4-4 4 4 4" /></svg></button>
      <h2 id={monthId} aria-live="polite">{monthLabel}</h2>
      <button type="button" aria-label="Следующий месяц" onClick={() => month(1)}><svg viewBox="0 0 16 16" aria-hidden="true"><path d="m6 4 4 4-4 4" /></svg></button>
      <button type="button" disabled={exporting} aria-busy={exporting} aria-label="Скачать календарь" title="Скачать календарь (.ics). После изменений скачайте заново; окончание игр оценочное." onClick={() => void exportCalendar()}><svg viewBox="0 0 20 20" aria-hidden="true"><path d="M10 3v9m-4-4 4 4 4-4M4 13v4h12v-4" /></svg></button>
    </div>
    {error && <Notice kind="danger">{error}</Notice>}
    <table ref={grid} className="calendar-grid" role="grid" aria-labelledby={monthId}>
      <thead><tr>{["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"].map(day => <th scope="col" key={day}>{day}</th>)}</tr></thead>
      <tbody>{rows.map((row, index) => <tr key={index}>{row.map((value, column) => {
        const count = value ? items.filter(item => item.localDate === value).length : 0;
        return <td key={value ?? column} aria-selected={value ? value === selected : undefined}>{value && <button type="button" data-calendar-date={value}
          tabIndex={value === selected ? 0 : -1} aria-current={value === today ? "date" : undefined}
          aria-label={`${formatDate(value)}, ${plural(count, "сбор", "сбора", "сборов")}`} onClick={() => choose(value)} onKeyDown={event => keyDown(event, value)}>
          <span>{Number(value.slice(-2))}</span><span className="calendar-day-markers" aria-hidden="true">{Array.from({ length: Math.min(count, 3) }, (_, dot) => <i key={dot} />)}</span>
        </button>}</td>;
      })}</tr>)}</tbody>
    </table>
    <section className="calendar-selected-day" aria-label="Сборы выбранного дня">
      <header><div><h3><time dateTime={selected}>{formatScheduleDay(selected)}</time></h3><small>Местное время сообществ</small></div><button type="button" className="ghost" onClick={() => choose(today)}>Сегодня</button></header>
      {selectedItems.length ? <ProfileScheduleList compact items={selectedItems} communities={communities} open={open} />
        : !items.length ? <Empty>{profileScheduleEmptyText}</Empty> : <div className="calendar-empty"><p>На этот день сборов нет.</p>{nearest && <button type="button" className="ghost" onClick={() => choose(nearest)}>К ближайшему сбору</button>}</div>}
    </section>
  </>;
}
