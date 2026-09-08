import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import type { AdminCommunityOption } from "./adminNavigation";

export function AdminCommunitySwitcher({ options, selected, loading, select }: {
  options: AdminCommunityOption[]; selected?: AdminCommunityOption; loading: boolean; select: (key: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const menuId = useId();
  const expanded = open && !loading && options.length > 1;
  useEffect(() => {
    if (!expanded) return;
    const menu = root.current?.querySelector('[role="listbox"]');
    (menu?.querySelector<HTMLButtonElement>('[aria-selected="true"]') ?? menu?.querySelector<HTMLButtonElement>('button'))?.focus();
    const dismiss = (event: PointerEvent) => {
      if (event.target instanceof Node && !root.current?.contains(event.target)) setOpen(false);
    };
    document.addEventListener("pointerdown", dismiss);
    return () => document.removeEventListener("pointerdown", dismiss);
  }, [expanded]);
  function close() { setOpen(false); trigger.current?.focus(); }
  function navigate(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Tab") { close(); return; }
    if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); close(); return; }
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="option"]'));
    const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const next = event.key === "Home" ? 0 : event.key === "End" ? buttons.length - 1
      : (index + (event.key === "ArrowDown" ? 1 : -1) + buttons.length) % buttons.length;
    buttons[next]?.focus();
  }
  return <div className="admin-community-select" ref={root} onBlur={event => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setOpen(false);
  }}>
    {options.length > 1 ? <>
      <button ref={trigger} type="button" className="admin-community-trigger" aria-label="Сообщество для администрирования"
        title={selected?.label} aria-haspopup="listbox" aria-expanded={expanded} aria-controls={expanded ? menuId : undefined}
        disabled={loading} onClick={() => setOpen(value => !value)} onKeyDown={event => {
          if (event.key === "ArrowDown" || event.key === "ArrowUp") { event.preventDefault(); setOpen(true); }
        }}>
        <span aria-hidden className={`mode-dot ${selected?.mode.toLowerCase()}`} />
        <span className="context-name">{selected?.name}</span>
        <span className="admin-community-kind">{selected?.mode === "Camp" ? "Кэмп" : "Клуб"}</span>
        <span aria-hidden>⌄</span>
      </button>
      {expanded && <div id={menuId} className="admin-community-menu" role="listbox" aria-label="Сообщество для администрирования" onKeyDown={navigate}>
        {options.map(item => <button type="button" role="option" aria-selected={item.id === selected?.id}
          tabIndex={item.id === selected?.id ? 0 : -1} key={item.id} value={item.id}
          onClick={() => { close(); select(item.id); }}>
          <span aria-hidden className={`mode-dot ${item.mode.toLowerCase()}`} />
          <span className="admin-community-option-label">{item.label}</span>
          <span className="admin-community-check" aria-hidden>{item.id === selected?.id ? "✓" : ""}</span>
        </button>)}
      </div>}
    </> : <>
      {selected && <span aria-hidden className={`mode-dot ${selected.mode.toLowerCase()}`} />}
      <strong className="admin-community-current">{selected?.label ?? (loading ? "Загрузка…" : "Нет доступных сообществ")}</strong>
    </>}
  </div>;
}
