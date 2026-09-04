import { describe, expect, it } from "vitest";
import type { PersonalCollectionItem } from "../api/types";
import { groupCollectionItems } from "./collectionGroups";

const base = (bggId: number): PersonalCollectionItem => ({ bggId, itemType: "BaseGame", source: "Manual", snapshot: { name: `Игра ${bggId}` } });
const expansion = (bggId: number, parentBggIds?: number[], parentBggId?: number): PersonalCollectionItem =>
  ({ bggId, itemType: "Expansion", source: "Manual", parentBggId, snapshot: { name: `Дополнение ${bggId}`, parentBggIds } });

describe("collection expansion groups", () => {
  it("uses every official parent and keeps orphans visible", () => {
    const groups = groupCollectionItems([expansion(3, [1, 2]), base(1), base(2), expansion(4, [99]), expansion(5, [], 1)]);
    expect(groups.map(group => [group.item.bggId, group.expansions.map(item => item.bggId)]))
      .toEqual([[1, [3, 5]], [2, [3]], [4, []]]);
  });
  it("keeps the parent when searching for a child and does not hide standalone expansions", () => {
    const items = [base(1), expansion(2, [1]), expansion(3, [1]), expansion(4)];
    expect(groupCollectionItems(items, item => item.bggId === 2).map(group => [group.item.bggId, group.expansions.map(item => item.bggId)]))
      .toEqual([[1, [2]]]);
    expect(groupCollectionItems(items, item => item.bggId === 4).map(group => group.item.bggId)).toEqual([4]);
    expect(groupCollectionItems(items, item => item.bggId === 1)[0].expansions).toHaveLength(2);
  });
});
