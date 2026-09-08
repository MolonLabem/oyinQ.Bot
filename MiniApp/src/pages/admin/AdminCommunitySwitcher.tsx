import type { AdminCommunityOption } from "./adminNavigation";

export function AdminCommunitySwitcher({ options, selected, loading, select }: {
  options: AdminCommunityOption[]; selected?: AdminCommunityOption; loading: boolean; select: (key: string) => void;
}) {
  return <div className="admin-community-select">
    {selected && <span aria-hidden className={`mode-dot ${selected.mode.toLowerCase()}`} />}
    {options.length > 1 ? <select aria-label="Сообщество для администрирования" title={selected?.label} value={selected?.id ?? ""}
      disabled={loading} onChange={event => select(event.target.value)}>
      {options.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
    </select> : <strong className="admin-community-current">{selected?.label ?? (loading ? "Загрузка…" : "Нет доступных сообществ")}</strong>}
  </div>;
}
