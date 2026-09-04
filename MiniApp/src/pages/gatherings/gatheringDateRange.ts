import { currentLocalMinute as localInput } from "../../app/format";
import type { Community } from "../../api/types";

export type GatheringDateTimeBounds = { min: string; max?: string };

export function gatheringDateTimeBounds(
  community: Community,
  currentLocalMinute: string,
): GatheringDateTimeBounds {
  if (community.mode !== "Camp" || !community.startsAtUtc || !community.endsAtUtc) {
    return { min: currentLocalMinute };
  }
  const campMinimum = localInput(community.timeZoneId, new Date(community.startsAtUtc));
  return {
    min: currentLocalMinute > campMinimum ? currentLocalMinute : campMinimum,
    max: localInput(community.timeZoneId, new Date(Date.parse(community.endsAtUtc) - 1)),
  };
}

export function isWithinCampDateRange(value: string, community: Community) {
  if (community.mode !== "Camp") return true;
  if (!community.startsAtUtc || !community.endsAtUtc) return false;
  return value >= localInput(community.timeZoneId, new Date(community.startsAtUtc))
    && value < localInput(community.timeZoneId, new Date(community.endsAtUtc));
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
