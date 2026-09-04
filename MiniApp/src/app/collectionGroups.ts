import type { PersonalCollectionItem } from "../api/types";

export function groupCollectionItems(items: PersonalCollectionItem[], matches: (item: PersonalCollectionItem) => boolean = () => true) {
  const bases = items.filter(item => item.itemType === "BaseGame");
  const expansions = items.filter(item => item.itemType === "Expansion");
  const parents = (item: PersonalCollectionItem) => item.snapshot.parentBggIds?.length
    ? item.snapshot.parentBggIds : item.parentBggId ? [item.parentBggId] : [];
  const groups = bases.map(item => ({ item, expansions: expansions.filter(exp => parents(exp).includes(item.bggId)) }));
  const orphans = expansions.filter(exp => !bases.some(base => parents(exp).includes(base.bggId)));
  return [...groups, ...orphans.map(item => ({ item, expansions: [] }))]
    .filter(group => matches(group.item) || group.expansions.some(matches))
    .map(group => ({ ...group, expansions: matches(group.item) ? group.expansions : group.expansions.filter(matches) }));
}
