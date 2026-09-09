import type { Expansion } from "../api/types";

export function ExpansionPicker({ expansions, selected, onChange, label = "Дополнения" }: {
  expansions: Expansion[]; selected: number[]; onChange: (ids: number[]) => void; label?: string;
}) {
  if (!expansions.length) return null;
  return <fieldset><legend>{label}</legend>{expansions.map(expansion => <label className="check" key={expansion.bggId}>
    <input type="checkbox" checked={selected.includes(expansion.bggId)} onChange={() => onChange(selected.includes(expansion.bggId)
      ? selected.filter(id => id !== expansion.bggId) : [...selected, expansion.bggId])} />{expansion.name}
    {expansion.minPlayers && expansion.maxPlayers ? <small> · {expansion.minPlayers}–{expansion.maxPlayers} игроков</small> : null}
  </label>)}</fieldset>;
}
