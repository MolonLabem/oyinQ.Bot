export type NavigationIconName = "gatherings" | "games" | "profile" | "communities";
export type Tab = { id: string; label: string; icon: NavigationIconName };
export function Navigation({ tabs, active, onChange }: { tabs: Tab[]; active: string; onChange: (id: string) => void }) {
  return <nav className="bottom-nav" aria-label="Основная навигация">{tabs.map(tab => <button key={tab.id} aria-current={active === tab.id ? "page" : undefined} className={active === tab.id ? "active" : ""} onClick={() => onChange(tab.id)}><NavigationIcon name={tab.icon} />{tab.label}</button>)}</nav>;
}

function NavigationIcon({ name }: { name: NavigationIconName }) {
  if (name === "gatherings") return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><rect x="3.5" y="5" width="17" height="15.5" rx="2.5" /><path d="M8 3.5v3M16 3.5v3M3.5 9h17M8 13h.01M12 13h.01M16 13h.01M8 17h.01M12 17h.01" /></svg>;
  if (name === "games") return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><rect x="4" y="4" width="16" height="16" rx="3" /><path d="M8 8h.01M16 8h.01M12 12h.01M8 16h.01M16 16h.01" /></svg>;
  if (name === "communities") return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M8.5 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM3.5 19v-1.5a4.5 4.5 0 0 1 9 0V19M16 11a2.5 2.5 0 1 0 0-5M15.5 14a4 4 0 0 1 5 3.9V19" /></svg>;
  return <svg className="nav-icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><circle cx="12" cy="8" r="3.5" /><path d="M5.5 20v-1.5a6.5 6.5 0 0 1 13 0V20" /></svg>;
}
