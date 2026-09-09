export type PlayerCountRange = {
  minimum: number;
  maximum: number;
  wasDefaulted: boolean;
};

export function normalizePlayerCountRange(minimum?: number, maximum?: number): PlayerCountRange {
  if (Number.isInteger(minimum) && Number.isInteger(maximum)
      && minimum! >= 1 && maximum! >= minimum!) {
    return { minimum: minimum!, maximum: maximum!, wasDefaulted: false };
  }

  return { minimum: 1, maximum: 12, wasDefaulted: true };
}

export function resolvePlayerCountRange(minimum: number | undefined, maximum: number | undefined,
  expansions: Expansion[], selected: number[]): PlayerCountRange {
  const range = normalizePlayerCountRange(minimum, maximum);
  for (const expansion of expansions) {
    if (!selected.includes(expansion.bggId)) continue;
    const extra = normalizePlayerCountRange(expansion.minPlayers, expansion.maxPlayers);
    if (extra.wasDefaulted) continue;
    range.minimum = Math.min(range.minimum, extra.minimum);
    range.maximum = Math.max(range.maximum, extra.maximum);
  }
  return range;
}

export type PlayerLimits = { minimum: number; desired: number; maximum: number };

export function fitPlayerLimits(limits: PlayerLimits, before: PlayerCountRange, after: PlayerCountRange): PlayerLimits {
  const clamp = (value: number) => Math.max(after.minimum, Math.min(after.maximum, value));
  const minimum = clamp(limits.minimum);
  const maximum = limits.maximum === before.maximum ? after.maximum : clamp(limits.maximum);
  return { minimum, maximum, desired: Math.max(minimum, Math.min(maximum, limits.desired)) };
}
import type { Expansion } from "../../api/types";
