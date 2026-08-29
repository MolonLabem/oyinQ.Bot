export type Tab = { id: string; label: string; icon: string };
export function Navigation({ tabs, active, onChange }: { tabs: Tab[]; active: string; onChange: (id: string) => void }) {
  return <nav className="bottom-nav" aria-label="Основная навигация">{tabs.map(tab => <button key={tab.id} className={active === tab.id ? "active" : ""} onClick={() => onChange(tab.id)}><span>{tab.icon}</span>{tab.label}</button>)}</nav>;
}
