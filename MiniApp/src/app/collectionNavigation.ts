import type { Community } from "../api/types";

export type CollectionVisit = { bggId: number; returnGatheringId: string };

export function positiveGameId(value: string | null) {
  if (!value || !/^\d+$/.test(value)) return undefined;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : undefined;
}

export function collectionVisitFromGathering(bggId: number, gatheringId: string): CollectionVisit {
  return { bggId, returnGatheringId: gatheringId };
}

export function collectionMissingMessage(community: Community) {
  return community.mode === "Camp"
    ? "Этой игры нет в коллекции этого кэмпа."
    : `Этой игры нет в коллекции «${community.name}».`;
}
