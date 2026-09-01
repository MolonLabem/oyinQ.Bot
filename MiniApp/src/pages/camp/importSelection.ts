import type { ImportDraftItem } from "../../api/types";

export function importItemKey(item: Pick<ImportDraftItem, "itemType" | "bggId">) {
  return `${item.itemType}-${item.bggId}`;
}

export function isImportItemSelectable(item: ImportDraftItem) {
  return !item.skipReason || item.isOverridable;
}

export function defaultImportSelection(items: ImportDraftItem[]) {
  return new Set(items.filter(item => item.selectedByDefault && isImportItemSelectable(item))
    .map(importItemKey));
}

export function importParentIds(item: ImportDraftItem) {
  return item.parentBggIds?.length ? item.parentBggIds
    : item.parentBggId ? [item.parentBggId] : [];
}

export function expansionBelongsToBase(expansion: ImportDraftItem, baseBggId: number) {
  return expansion.itemType === "Expansion" && importParentIds(expansion).includes(baseBggId);
}
