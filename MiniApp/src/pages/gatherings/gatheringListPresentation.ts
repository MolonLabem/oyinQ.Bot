import type { GatheringListItem } from "../../api/types";
import { currentLocalMinute } from "../../app/format";

export type GatheringDay = { date?: string; label?: string; items: { item: GatheringListItem; time?: string }[] };

export function gatheringDays(items: GatheringListItem[], timeZoneId: string, now = new Date()): GatheringDay[] {
  const today = currentLocalMinute(timeZoneId, now).slice(0, 10);
  const tomorrow = new Date(`${today}T12:00:00Z`);
  tomorrow.setUTCDate(tomorrow.getUTCDate() + 1);
  const tomorrowDate = tomorrow.toISOString().slice(0, 10);
  const groups: GatheringDay[] = [];
  for (const item of items) {
    const instant = item.startsAtUtc ? new Date(item.startsAtUtc) : undefined;
    const local = instant && Number.isFinite(instant.getTime()) ? currentLocalMinute(timeZoneId, instant) : undefined;
    const date = local?.slice(0, 10);
    let group = groups.at(-1);
    // Keep the backend's order and page boundaries, including older responses without the new timestamp.
    if (!group || group.date !== date) {
      const label = date === today ? "Сегодня" : date === tomorrowDate ? "Завтра"
        : instant && date ? new Intl.DateTimeFormat("ru-RU", {
          timeZone: timeZoneId, weekday: "short", day: "numeric", month: "long",
          ...(date.slice(0, 4) !== today.slice(0, 4) ? { year: "numeric" as const } : {})
        }).format(instant) : undefined;
      group = { date, label, items: [] };
      groups.push(group);
    }
    group.items.push({ item, time: local?.slice(11) });
  }
  return groups;
}
