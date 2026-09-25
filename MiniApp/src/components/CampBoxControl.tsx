import { useEffect, useRef, useState } from "react";
import { api, json } from "../api/client";
import type { Contribution } from "../api/types";
import { Notice } from "./Ui";
import { telegram } from "../telegram/webApp";

export function attendanceLabel(dates: string[]) {
  const sorted = [...new Set(dates)].sort();
  const format = (date: string) => new Date(`${date}T12:00:00Z`).toLocaleDateString("ru-RU", { day: "numeric", month: "long", timeZone: "UTC" });
  const contiguous = sorted.every((d, i) => i === 0 || Date.parse(d) - Date.parse(sorted[i - 1]) === 86400000);
  return sorted.length > 1 && contiguous ? `${format(sorted[0])} — ${format(sorted[sorted.length - 1])}` : sorted.map(format).join(", ");
}

type Box = Pick<Contribution, "commitment" | "allAttendanceDays" | "selectedDates" | "availableDates">;
type Item = { bggId: number; itemType: Contribution["itemType"]; snapshot: { name: string } };
type DateChange = { allAttendanceDays: true } | { availableDates: string[] };

// All entry points use the same actor-bound contribution writer. Only the collection
// supplies attendanceDates to expose the optional date editor; status changes omit dates.
export function CampBoxControl(props: {
  communityKey: string; item: Item; contribution?: Box; declined?: boolean; disabled: boolean;
  attendanceDates?: string[]; changed: () => Promise<boolean>; beforeChange?: () => (() => void) | undefined;
}) {
  return <BoxControl key={`${props.communityKey}:${props.item.itemType}:${props.item.bggId}`} {...props} />;
}

function BoxControl({ communityKey, item, contribution, declined, disabled, attendanceDates, changed, beforeChange }: Parameters<typeof CampBoxControl>[0]) {
  const current = contribution?.commitment ?? (declined ? "declined" : "");
  const alive = useRef(false); const saving = useRef(false);
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const retry = useRef<() => Promise<void>>(async () => {});
  const [allDays, setAllDays] = useState(contribution?.allAttendanceDays !== false);
  const [dates, setDates] = useState(contribution?.selectedDates ?? attendanceDates ?? []);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => { setAllDays(contribution?.allAttendanceDays !== false); setDates(contribution?.selectedDates ?? attendanceDates ?? []); }, [contribution, attendanceDates]);

  async function refresh() {
    if (!alive.current || saving.current) return;
    saving.current = true; setBusy(true); setError(undefined); beforeChange?.()?.();
    const refreshed = await changed();
    if (alive.current) {
      if (!refreshed) setError("Решение сохранено. Не удалось обновить список.");
      saving.current = false; setBusy(false);
    }
  }
  async function save(next: string, dateChange?: DateChange) {
    if (disabled || saving.current || (!dateChange && next === current)) return;
    saving.current = true; setBusy(true); setError(undefined); const armAnchor = beforeChange?.();
    retry.current = () => save(next, dateChange);
    try {
      if (next === "declined" || (!next && item.itemType === "BaseGame")) {
        await api(`/camp-wishlist?community=${encodeURIComponent(communityKey)}`, json("POST", { action: next ? "decline" : "withdraw", bggId: item.bggId }));
      } else if (!next) await api(`/camp/contributions/${item.itemType}/${item.bggId}?community=${encodeURIComponent(communityKey)}`, { method: "DELETE" });
      else await api(`/camp/contributions/${item.itemType}/${item.bggId}/commitment`, json("PUT", {
        communityKey, commitment: next, ...(dateChange ?? (!contribution ? { allAttendanceDays: true } : {}))
      }));
      if (!alive.current) return;
      // A failed reload retries only reads, never replays an already accepted action.
      retry.current = refresh;
      armAnchor?.();
      telegram.success("Решение сохранено");
      const refreshed = await changed();
      if (alive.current && !refreshed) setError("Решение сохранено. Не удалось обновить список.");
    } catch (e) { if (alive.current) setError(e instanceof Error ? e.message : String(e)); }
    finally { if (alive.current) { saving.current = false; setBusy(false); } }
  }
  const locked = disabled || busy;
  return <div className="box-control" aria-busy={busy}>
    <select disabled={locked} aria-label={`Привезёте ${item.snapshot.name}?`} className={current === "Bringing" ? "commitment-select success" : "commitment-select"} value={current} onChange={e => void save(e.target.value)}>
      <option value="">{current ? "Снять решение" : "Предложить свою игру"}</option>
      <option value="Available">Могу привезти</option><option value="Bringing">Точно привезу</option>
      {item.itemType === "BaseGame" && <option value="declined">Не повезу</option>}
    </select>
    {contribution?.allAttendanceDays === false && <small>{contribution.availableDates?.length ? `Только ${attendanceLabel(contribution.availableDates)}` : "Нет доступных дней — измените даты в профиле"}</small>}
    {attendanceDates && contribution && <details className="box-date-options"><summary>Другие дни</summary>
      <label className="check"><input type="radio" disabled={locked} checked={allDays} onChange={() => setAllDays(true)} />Все дни моего участия</label>
      <label className="check"><input type="radio" disabled={locked} checked={!allDays} onChange={() => setAllDays(false)} />Выбранные дни</label>
      {!allDays && <fieldset className="wish-dates" disabled={locked}><legend>Когда будет игра</legend>{attendanceDates.map(date => <label key={date}><input type="checkbox" checked={dates.includes(date)} onChange={e => setDates(e.target.checked ? [...dates, date].sort() : dates.filter(d => d !== date))} />{attendanceLabel([date])}</label>)}</fieldset>}
      {!allDays && dates.some(d => !attendanceDates.includes(d)) && <Notice kind="warning">Часть старых дат больше не входит в регистрацию. Выберите дни заново или верните все дни участия.</Notice>}
      <button disabled={locked || (!allDays && (!dates.length || dates.some(d => !attendanceDates.includes(d))))} onClick={() => void save(contribution.commitment, allDays ? { allAttendanceDays: true } : { availableDates: dates })}>Сохранить дни</button>
      {!allDays && dates.some(d => !attendanceDates.includes(d)) && <button disabled={locked} onClick={() => setDates(dates.filter(d => attendanceDates.includes(d)))}>Оставить дни регистрации</button>}
    </details>}
    {busy && <small role="status">Сохраняем…</small>}
    {error && <Notice kind="danger">{error} <button disabled={busy} onClick={() => void retry.current()}>Повторить</button></Notice>}
  </div>;
}
