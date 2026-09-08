import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import type { AdminCommunityOption } from "./adminNavigation";
import { CommunityPicker } from "../../components/CommunityPicker";
import { CommunityAvatar } from "../../components/CommunityAvatar";

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
        {selected && <CommunityAvatar key={selected.key} community={selected} />}
        <span className="context-name">{selected?.name}</span>
        <span className="admin-community-kind">{selected?.mode === "Camp" ? "Кэмп" : "Клуб"}</span>
        <span aria-hidden>⌄</span>
      </button>
      {expanded && <CommunityPicker id={menuId} className="admin-community-menu" communities={options}
        selectedKey={selected?.key} choose={key => { close(); select(key); }} onKeyDown={navigate} />}
    </> : <>
      {selected && <CommunityAvatar key={selected.key} community={selected} />}
      <strong className="admin-community-current">{selected?.label ?? (loading ? "Загрузка…" : "Нет доступных сообществ")}</strong>
    </>}
  </div>;
}
