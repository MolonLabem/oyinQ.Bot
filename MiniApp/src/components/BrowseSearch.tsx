import { useId, useRef } from "react";

export function BrowseSearch({ label, value, onChange, placeholder = "Название игры", disabled = false }: {
  label: string; value: string; onChange: (value: string) => void; placeholder?: string; disabled?: boolean;
}) {
  const id = useId();
  const input = useRef<HTMLInputElement>(null);
  return <div className="browse-search">
    <label className="visually-hidden" htmlFor={id}>{label}</label>
    <svg className="browse-search-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 4.5 4.5" /></svg>
    <input ref={input} id={id} type="search" disabled={disabled} value={value} onChange={event => onChange(event.target.value)} placeholder={placeholder} />
    {value && <button type="button" disabled={disabled} aria-label="Очистить поиск" onClick={() => { onChange(""); input.current?.focus(); }}><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18" /></svg></button>}
  </div>;
}
