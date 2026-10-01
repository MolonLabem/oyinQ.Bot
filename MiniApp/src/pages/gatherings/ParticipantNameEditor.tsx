import { useState } from "react";
import { Field } from "../../components/Ui";
import { useBackButton } from "../../hooks/useBackButton";

export function ParticipantNameEditor({ originalName, displayNameOverride, onChange, disabled = false, buttonLabel }: {
  originalName: string; displayNameOverride?: string | null; onChange: (name: string | null) => void; disabled?: boolean; buttonLabel?: string;
}) {
  const [open, setOpen] = useState(false);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  const name = displayNameOverride ?? originalName;
  const invalid = draft.trim().length > 128 || /[\u0000-\u001f\u007f-\u009f]/.test(draft.trim());
  function close() { setOpen(false); setEditing(false); }
  useBackButton(open, () => { if (!disabled) close(); }, true);
  function apply(value: string | null) { onChange(value?.trim() || null); close(); }
  return <div className="participant-name-editor">
    <button type="button" className="ghost participant-name-choice" disabled={disabled} aria-expanded={open}
      aria-label={buttonLabel ? `${buttonLabel}: ${name}` : undefined}
      onClick={() => { if (open) close(); else { setDraft(name); setOpen(true); } }}>{buttonLabel ?? name}</button>
    {open && <div className="participant-name-settings form-grid">
      <p>Имя в этой партии: <strong>{name}</strong></p>
      {editing ? <><Field label="Новое имя" hint="Только для этой партии. Пустое поле вернёт исходное имя.">
        <input type="text" maxLength={128} value={draft} disabled={disabled} autoFocus
          onChange={e => setDraft(e.target.value)} onKeyDown={e => { if (e.key === "Enter") { e.preventDefault(); if (!disabled && !invalid) apply(draft); } }} />
      </Field><button type="button" disabled={disabled || invalid} onClick={() => apply(draft)}>Применить имя</button></>
        : <button type="button" disabled={disabled} onClick={() => setEditing(true)}>✏️ Изменить имя</button>}
      {displayNameOverride != null && <button type="button" disabled={disabled} onClick={() => apply(null)}>↩️ Использовать исходное имя</button>}
      <button type="button" className="ghost" disabled={disabled} onClick={close}>Назад к составу</button>
    </div>}
  </div>;
}
