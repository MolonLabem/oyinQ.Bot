import type { ClubGame } from "../../api/types";
import { profileGeneration } from "../../app/profileLifecycle";
import type { ExpansionLookup } from "../../components/gamePickerModel";
import type { ExpansionOwnership } from "./GatheringExpansionPicker";

export type GatheringDraft = {
  source: "catalog" | "bgg" | "demand";
  chosen?: ClubGame;
  initialExpansionLookup?: ExpansionLookup;
  expansions: number[];
  expansionOwnership: ExpansionOwnership;
  addToCollection: boolean;
  bringToCamp: boolean;
  starts: string;
  minimum: number;
  desired: number;
  maximum: number;
  description: string;
  teach: boolean;
  copySourceId?: string;
};

const fallback = new Map<string, GatheringDraft | undefined>();
let generation = profileGeneration();
function storageKey(communityKey: string) {
  if (generation !== profileGeneration()) { fallback.clear(); generation = profileGeneration(); }
  return `oyinq-gathering-draft:${communityKey}`;
}
const ids = (value: unknown): value is number[] => Array.isArray(value) && value.every(id => Number.isSafeInteger(id) && id > 0);

export function readGatheringDraft(communityKey: string): GatheringDraft | undefined {
  const key = storageKey(communityKey);
  if (fallback.has(key)) return fallback.get(key);
  let raw: string | null;
  try { raw = sessionStorage.getItem(key); } catch { return fallback.get(key); }
  try {
    const saved = JSON.parse(raw ?? "null");
    const value = saved?.draft;
    if (saved?.version !== 1 || !value || !["catalog", "bgg", "demand"].includes(value.source)
      || !ids(value.expansions) || !ids(value.expansionOwnership?.add) || !ids(value.expansionOwnership?.bring)
      || ![value.minimum, value.desired, value.maximum].every(Number.isSafeInteger)
      || typeof value.starts !== "string" || typeof value.description !== "string"
      || ![value.teach, value.addToCollection, value.bringToCamp].every(item => typeof item === "boolean")
      || value.copySourceId !== undefined && typeof value.copySourceId !== "string"
      || value.initialExpansionLookup !== undefined && !["complete", "incomplete", "failed"].includes(value.initialExpansionLookup?.status)
      || value.chosen && (!Number.isSafeInteger(value.chosen.bggId) || value.chosen.bggId <= 0
        || typeof value.chosen.name !== "string" || !Array.isArray(value.chosen.expansions))) return undefined;
    return value as GatheringDraft;
  } catch { return undefined; }
}

export function saveGatheringDraft(communityKey: string, draft: GatheringDraft) {
  const key = storageKey(communityKey);
  try { sessionStorage.setItem(key, JSON.stringify({ version: 1, draft })); fallback.delete(key); }
  catch { fallback.set(key, draft); }
}

export function clearGatheringDraft(communityKey: string, expected?: GatheringDraft) {
  const key = storageKey(communityKey);
  // A response from a form left during submission must not erase newer edits.
  if (expected && JSON.stringify(readGatheringDraft(communityKey)) !== JSON.stringify(expected)) return;
  try { sessionStorage.removeItem(key); fallback.delete(key); }
  catch { fallback.set(key, undefined); }
}
