import type { Expansion } from "../../api/types";
import { ExpansionPicker } from "../../components/ExpansionPicker";

export type ExpansionOwnership = { add: number[]; bring: number[] };
export const emptyExpansionOwnership = (): ExpansionOwnership => ({ add: [], bring: [] });
export function retainExpansionOwnership(value: ExpansionOwnership, selected: number[]): ExpansionOwnership {
  return { add: value.add.filter(id => selected.includes(id)), bring: value.bring.filter(id => selected.includes(id)) };
}
export function GatheringExpansionPicker({ expansions, selected, onChange, ownership, onOwnership, camp, disabled }: {
  expansions: Expansion[]; selected: number[]; onChange: (ids: number[]) => void;
  ownership: ExpansionOwnership; onOwnership: (value: ExpansionOwnership) => void; camp: boolean; disabled?: boolean;
}) {
  function toggle(kind: "add" | "bring", id: number, checked: boolean) {
    onOwnership({ ...ownership, [kind]: checked ? [...new Set([...ownership[kind], id])] : ownership[kind].filter(x => x !== id) });
  }
  return <section><ExpansionPicker expansions={expansions} selected={selected} disabled={disabled} searchable
    label="С какими дополнениями играем" onChange={ids => { onOwnership(retainExpansionOwnership(ownership, ids)); onChange(ids); }}
    actions={expansion => <div className="stack">
      <label className="check"><input type="checkbox" aria-label={`Добавить в мою коллекцию: ${expansion.name}`} checked={ownership.add.includes(expansion.bggId) || ownership.bring.includes(expansion.bggId)} disabled={ownership.bring.includes(expansion.bggId)} onChange={e => toggle("add", expansion.bggId, e.target.checked)} />Есть у меня — добавить в мою коллекцию</label>
      {camp && <label className="check"><input type="checkbox" aria-label={`Я привезу: ${expansion.name}`} checked={ownership.bring.includes(expansion.bggId)} onChange={e => toggle("bring", expansion.bggId, e.target.checked)} />Я привезу на кэмп</label>}
    </div>} />
    {expansions.length > 0 && <p className="muted">Отметьте дополнения для этой партии независимо от владельца. {camp && "«Я привезу» также добавит дополнение в вашу коллекцию, если его там ещё нет. "}Отметки сохранятся вместе со сбором. Удаление дополнения из сбора не удаляет его из коллекции{camp ? " и не отменяет обещание привезти на кэмп." : "."}</p>}
  </section>;
}
