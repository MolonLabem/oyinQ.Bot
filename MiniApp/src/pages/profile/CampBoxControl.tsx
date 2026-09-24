import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import type { Contribution, PersonalCollectionItem } from "../../api/types";
import { Notice } from "../../components/Ui";
import { telegram } from "../../telegram/webApp";
import { useCampProfile } from "./CampProfileContext";

export function attendanceLabel(dates: string[]) {
  const sorted = [...new Set(dates)].sort();
  const format = (date: string) => new Date(`${date}T12:00:00Z`).toLocaleDateString("ru-RU", { day: "numeric", month: "long", timeZone: "UTC" });
  const contiguous = sorted.every((d, i) => i === 0 || Date.parse(d) - Date.parse(sorted[i - 1]) === 86400000);
  return sorted.length > 1 && contiguous ? `${format(sorted[0])} — ${format(sorted[sorted.length - 1])}` : sorted.map(format).join(", ");
}

export function CampBoxControl({ communityKey, item, contribution, disabled, changed }: {
  communityKey: string; item: PersonalCollectionItem; contribution?: Contribution; disabled: boolean; changed: () => void;
}) {
  const profile = useCampProfile(); const settings = profile?.data;
  const declined = item.itemType === "BaseGame" && settings?.declinedGameIds.includes(item.bggId);
  const current = contribution?.commitment ?? (declined ? "declined" : "");
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>();
  const [allDays, setAllDays] = useState(contribution?.allAttendanceDays !== false);
  const [dates, setDates] = useState(contribution?.selectedDates ?? settings?.myDates ?? []);
  useEffect(() => { setAllDays(contribution?.allAttendanceDays !== false); setDates(contribution?.selectedDates ?? settings?.myDates ?? []); }, [contribution, settings?.myDates]);
  async function save(next: string, changeDates = false) {
    if (busy) return; setBusy(true); setError(undefined);
    try {
      if (next === "declined" || (!next && declined)) {
        await api(`/camp-wishlist?community=${encodeURIComponent(communityKey)}`, json("POST", { action: next ? "decline" : "withdraw", bggId: item.bggId }));
      } else if (!next) await api(`/camp/contributions/${item.itemType}/${item.bggId}?community=${encodeURIComponent(communityKey)}`, { method: "DELETE" });
      else await api(`/camp/contributions/${item.itemType}/${item.bggId}/commitment`, json("PUT", {
        communityKey, commitment: next,
        ...(changeDates ? allDays ? { allAttendanceDays: true } : { availableDates: dates } : !contribution ? { allAttendanceDays: true } : {})
      }));
      changed(); profile?.reload(); telegram.success("Решение сохранено");
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }
  const locked = disabled || !settings?.canAct || busy;
  return <div className="box-control">
    <select disabled={locked} aria-label={`Привезёте ${item.snapshot.name}?`} className={current === "Bringing" ? "commitment-select success" : "commitment-select"} value={current} onChange={e => void save(e.target.value)}>
      <option value="">Не предлагаю для кэмпа</option><option value="Available">Могу привезти</option><option value="Bringing">Точно привезу</option>
      {item.itemType === "BaseGame" && <option value="declined">Не повезу</option>}
    </select>
    {contribution?.allAttendanceDays === false && <small>{contribution.availableDates?.length ? `Только ${attendanceLabel(contribution.availableDates)}` : "Нет доступных дней — измените даты"}</small>}
    {contribution && <details className="box-date-options"><summary>Другие дни</summary>
      <label className="check"><input type="radio" checked={allDays} onChange={() => setAllDays(true)} />Все дни моего участия</label>
      <label className="check"><input type="radio" checked={!allDays} onChange={() => setAllDays(false)} />Выбранные дни</label>
      {!allDays && <fieldset className="wish-dates"><legend>Когда будет игра</legend>{settings?.myDates.map(date => <label key={date}><input type="checkbox" checked={dates.includes(date)} onChange={e => setDates(e.target.checked ? [...dates, date].sort() : dates.filter(d => d !== date))} />{attendanceLabel([date])}</label>)}</fieldset>}
      {!allDays && dates.some(d => !settings?.myDates.includes(d)) && <Notice kind="warning">Часть старых дат больше не входит в регистрацию. Выберите дни заново или верните все дни участия.</Notice>}
      <button disabled={locked || (!allDays && (!dates.length || dates.some(d => !settings?.myDates.includes(d))))} onClick={() => void save(contribution.commitment, true)}>Сохранить дни</button>
      {!allDays && dates.some(d => !settings?.myDates.includes(d)) && <button onClick={() => setDates(dates.filter(d => settings?.myDates.includes(d)))}>Оставить дни регистрации</button>}
    </details>}
    {busy && <small role="status">Сохраняем…</small>}{error && <Notice kind="danger">{error}</Notice>}
  </div>;
}
