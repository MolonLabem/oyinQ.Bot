import { useEffect, useRef, useState } from "react";
import { Badge } from "../../components/Ui";
import { telegram } from "../../telegram/webApp";

export function GuestRow({ name, editable, busy, rename, remove }: {
  name: string; editable: boolean; busy: boolean;
  rename: (name: string) => Promise<boolean>; remove: () => Promise<boolean>;
}) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(name);
  const [pending, setPending] = useState(false);
  const locked = useRef(false);
  const nameButton = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false);
  const disabled = busy || pending;
  useEffect(() => {
    if (!editing && !disabled && restoreFocus.current) {
      nameButton.current?.focus(); restoreFocus.current = false;
    }
  }, [editing, disabled]);
  function cancel() { restoreFocus.current = true; setEditing(false); }
  async function save() {
    if (disabled || locked.current || !draft.trim()) return;
    if (draft === name) { cancel(); return; }
    locked.current = true; setPending(true);
    try { if (await rename(draft)) cancel(); }
    finally { locked.current = false; setPending(false); }
  }
  async function deleteGuest() {
    if (disabled || locked.current) return;
    locked.current = true; setPending(true);
    try { if (await telegram.confirm(`Удалить гостя «${name}»?`)) await remove(); }
    finally { locked.current = false; setPending(false); }
  }
  return <li className="guest-row">
    <span className="participant-marker" aria-hidden>●</span>
    {editing && editable ? <form className="guest-edit" aria-busy={disabled} onSubmit={event => { event.preventDefault(); void save(); }}>
      <input autoFocus aria-label={`Имя гостя ${name}`} value={draft} maxLength={80} disabled={disabled}
        onChange={event => setDraft(event.target.value)}
        onKeyDown={event => { if (event.key === "Enter" && event.nativeEvent.isComposing) event.preventDefault(); if (event.key === "Escape" && !event.nativeEvent.isComposing && !disabled) { event.preventDefault(); cancel(); } }} />
      <button type="submit" disabled={disabled || !draft.trim()} aria-label="Сохранить имя гостя" title="Сохранить имя гостя">✓</button>
      <button type="button" className="ghost" disabled={disabled} onClick={cancel} aria-label="Отменить изменение имени" title="Отменить изменение имени">×</button>
    </form> : <>
      <div className="guest-name">
        {editable ? <button ref={nameButton} type="button" className="guest-name-button" disabled={disabled}
          aria-label={`Изменить имя гостя ${name}`} onClick={() => { setDraft(name); setEditing(true); }}>
          {name}<span className="guest-edit-hint" aria-hidden>✎</span>
        </button> : <span>{name}</span>}
        <Badge tone="neutral">Гость</Badge>
      </div>
      {editable && <button type="button" className="guest-remove danger ghost" disabled={disabled}
        aria-label={`Удалить гостя ${name}`} title={`Удалить гостя ${name}`} onClick={() => void deleteGuest()}>×</button>}
    </>}
  </li>;
}
