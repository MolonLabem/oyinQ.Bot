import type { Expansion } from "../api/types";
import { useState, type ReactNode } from "react";

export function ExpansionPicker({ expansions, selected, onChange, label = "Дополнения", actions, disabled = false, searchable = false }: {
  expansions: Expansion[]; selected: number[]; onChange: (ids: number[]) => void; label?: string;
  actions?: (expansion: Expansion) => ReactNode; disabled?: boolean; searchable?: boolean;
}) {
  const [search, setSearch] = useState("");
  if (!expansions.length) return null;
  const visible = expansions.filter(x => !searchable || `${x.name} ${x.originalName ?? ""}`.toLocaleLowerCase().includes(search.toLocaleLowerCase()));
  return <fieldset disabled={disabled}><legend>{label}</legend>{searchable && <><p className="muted">Выбрано: {selected.length}. Без отметок — играем только в базовую игру.</p>{expansions.length > 8 && <input type="search" aria-label="Найти дополнение" placeholder="Найти дополнение" value={search} onChange={e => setSearch(e.target.value)} />}{selected.length > 0 && <button type="button" className="ghost" onClick={() => onChange([])}>Только базовая игра</button>}</>}{visible.map(expansion => <div key={expansion.bggId}><label className="check">
    <input type="checkbox" checked={selected.includes(expansion.bggId)} onChange={() => onChange(selected.includes(expansion.bggId)
      ? selected.filter(id => id !== expansion.bggId) : [...selected, expansion.bggId])} />{expansion.name}
    {expansion.minPlayers && expansion.maxPlayers ? <small> · {expansion.minPlayers}–{expansion.maxPlayers} игроков</small> : null}
  </label>{selected.includes(expansion.bggId) && actions?.(expansion)}</div>)}{searchable && visible.length === 0 && <p>Дополнения не найдены.</p>}</fieldset>;
}
