import type { Community } from "../../api/types";

export type GatheringDateTimeBounds = { min: string; max?: string };

export function gatheringDateTimeBounds(
  community: Community,
  currentLocalMinute: string,
): GatheringDateTimeBounds {
  if (community.mode !== "Camp" || !community.startDate || !community.endDate) {
    return { min: currentLocalMinute };
  }
  const campMinimum = `${community.startDate}T00:00`;
  return {
    min: currentLocalMinute > campMinimum ? currentLocalMinute : campMinimum,
    max: `${community.endDate}T23:59`,
  };
}

export function isWithinCampDateRange(value: string, community: Community) {
  if (community.mode !== "Camp") return true;
  if (!community.startDate || !community.endDate) return false;
  const date = value.slice(0, 10);
  return /^\d{4}-\d{2}-\d{2}$/.test(date)
    && date >= community.startDate
    && date <= community.endDate;
}

export function revalidateGatheringStart(
  value: string,
  community: Community,
  bounds: GatheringDateTimeBounds,
) {
  if (community.mode !== "Camp") return value;
  if (value && isWithinCampDateRange(value, community)) return value;
  return bounds.max && bounds.min <= bounds.max ? bounds.min : "";
}
