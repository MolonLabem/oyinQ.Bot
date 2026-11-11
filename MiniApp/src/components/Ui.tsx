import { type ReactNode, useEffect, useRef, useState } from "react";
import { successEventName } from "../telegram/webApp";
import { telegram } from "../telegram/webApp";
import type { BadgeTone } from "../app/semanticTones";

export function Page({ as: Element = "main", title, titleHref, subtitle, actions, children, preserveScroll = false, hideTitle = false }: { as?: "main" | "section"; title?: string; titleHref?: string; subtitle?: string; actions?: ReactNode; children: ReactNode; preserveScroll?: boolean; hideTitle?: boolean }) {
  useEffect(() => { if (!preserveScroll) window.scrollTo({ top: 0, behavior: "auto" }); }, [title, preserveScroll]);
  const hasHeading = Boolean(!hideTitle && title || subtitle);
  return <Element className="page">{hideTitle && title && <h1 className="visually-hidden">{title}</h1>}{(hasHeading || actions) && <header className={`page-header${hideTitle ? " compact" : hasHeading ? "" : " actions-only"}`}>{hasHeading && <div>{!hideTitle && title && <h1>{titleHref ? <a className="page-title-link" href={titleHref} target="_blank" rel="noreferrer">{title}</a> : title}</h1>}{subtitle && <p>{subtitle}</p>}</div>}{actions}</header>}{children}</Element>;
}
export function Card({ children, className = "" }: { children: ReactNode; className?: string }) { return <section className={`card ${className}`}>{children}</section>; }
export function FormSection({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return <Card className="form-grid form-section"><h2>{title}</h2>{hint && <p className="muted">{hint}</p>}{children}</Card>;
}
export function validateForm(form: HTMLFormElement | null) {
  if (!form) return false;
  form.querySelectorAll("input:invalid, select:invalid, textarea:invalid").forEach(control => {
    let parent = control.parentElement;
    while (parent && parent !== form) { if (parent instanceof HTMLDetailsElement) parent.open = true; parent = parent.parentElement; }
  });
  return form.reportValidity();
}
// Screens wire the native button through telegram.back; retain navigation in ordinary browsers.
export function BackButton({ onClick }: { onClick: () => void }) {
  return telegram.hasBackButton ? null : <button type="button" className="ghost page-back" aria-label="Назад" title="Назад" onClick={onClick}><NavigationArrow direction="previous" /></button>;
}
function NavigationArrow({ direction }: { direction: "previous" | "next" }) {
  return <svg className="navigation-arrow" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={direction === "previous" ? "M19 12H5m7-7-7 7 7 7" : "M5 12h14m-7-7 7 7-7 7"} /></svg>;
}
export function Pagination({ page, hasPrevious, hasNext, onChange, disabled = false, label = "Страницы", children }: {
  page: number; hasPrevious: boolean; hasNext: boolean; onChange: (page: number) => void; disabled?: boolean; label?: string; children?: ReactNode;
}) {
  if (!hasPrevious && !hasNext) return null;
  return <nav className="pagination" aria-label={label}>
    <button type="button" disabled={disabled || !hasPrevious} onClick={() => onChange(page - 1)} aria-label="Предыдущая страница" title="Предыдущая страница"><NavigationArrow direction="previous" /></button>
    <span className="muted" role="status">{children ?? `Страница ${page}`}</span>
    <button type="button" disabled={disabled || !hasNext} onClick={() => onChange(page + 1)} aria-label="Следующая страница" title="Следующая страница"><NavigationArrow direction="next" /></button>
  </nav>;
}
export function Loading({ label = "Загрузка…" }: { label?: string }) { return <div className="stack" aria-live="polite"><div className="skeleton" /><div className="skeleton short" /><span className="muted">{label}</span></div>; }
export function Empty({ children }: { children: ReactNode }) { return <Card><p className="empty">{children}</p></Card>; }
export function ErrorState({ message, retry }: { message: string; retry?: () => void }) { return <Card className="error"><strong>Не удалось загрузить данные</strong><p>{message}</p>{retry && <button onClick={retry}>Повторить</button>}</Card>; }
export function Cover({ src, name }: { src?: string; name: string }) { return src ? <img className="cover" src={src} alt={`Обложка игры ${name}`} /> : <div className="cover placeholder" aria-hidden>🎲</div>; }
export function Field({ label, children, hint, error }: { label: string; children: ReactNode; hint?: string; error?: string }) { return <label className={`field${error ? " invalid" : ""}`}><span>{label}</span>{children}{hint && <small>{hint}</small>}{error && <small className="field-error" role="alert">{error}</small>}</label>; }
export function Notice({ children, kind = "info" }: { children: ReactNode; kind?: "info" | "warning" | "danger" | "success" }) { return <div className={`notice ${kind}`} role="status">{children}</div>; }
export function Badge({ children, tone = "neutral", className = "" }: { children: ReactNode; tone?: BadgeTone; className?: string }) { return <span className={`badge ${tone} ${className}`}>{children}</span>; }
export type TabItem = { id: string; label: string };
export function Tabs({ items, active, onChange, label, className = "", scrollActiveIntoView = true }: { items: TabItem[]; active: string; onChange: (id: string) => void; label: string; className?: string; scrollActiveIntoView?: boolean }) {
  const activeButton = useRef<HTMLButtonElement>(null);
  const [focused, setFocused] = useState(active);
  const tabStop = items.some(item => item.id === focused) ? focused : active;
  useEffect(() => { setFocused(active); }, [active]);
  useEffect(() => { if (scrollActiveIntoView) activeButton.current?.scrollIntoView({ block: "nearest", inline: "nearest" }); }, [active, scrollActiveIntoView]);
  return <div className={`page-tabs ${className}`.trim()} role="tablist" aria-label={label}
    onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) setFocused(active); }}>{items.map((item, index) => <button
    key={item.id} ref={active === item.id ? activeButton : undefined} type="button" role="tab"
    tabIndex={item.id === tabStop ? 0 : -1} onFocus={() => setFocused(item.id)}
    aria-selected={active === item.id} className={active === item.id ? "active" : ""}
    onKeyDown={event => {
      if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
      const next = event.key === "ArrowRight" ? (index + 1) % items.length
        : event.key === "ArrowLeft" ? (index + items.length - 1) % items.length
        : event.key === "Home" ? 0 : event.key === "End" ? items.length - 1 : undefined;
      if (next === undefined) return;
      event.preventDefault();
      // Moving focus does not load a different section; Enter/Space use native button activation.
      event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus();
    }}
    onClick={() => onChange(item.id)}>{item.label}</button>)}</div>;
}
export function SegmentedControl({ items, active, onChange, label, className = "" }: { items: TabItem[]; active: string; onChange: (id: string) => void; label: string; className?: string }) {
  const activeButton = useRef<HTMLButtonElement>(null);
  useEffect(() => { activeButton.current?.scrollIntoView({ block: "nearest", inline: "nearest" }); }, [active]);
  return <div className={`filter-segments ${className}`.trim()} role="group" aria-label={label}>{items.map(item => <button
    key={item.id} ref={active === item.id ? activeButton : undefined} type="button"
    aria-pressed={active === item.id} className={active === item.id ? "active" : ""}
    onClick={() => onChange(item.id)}>{item.label}</button>)}</div>;
}
export function SaveButton({ busy, label, busyLabel = "Сохраняем…", disabled, onClick, className = "primary" }: {
  busy: boolean; label: string; busyLabel?: string; disabled?: boolean; onClick: () => void; className?: string;
}) {
  return <button type="button" className={className} disabled={busy || disabled} aria-busy={busy} onClick={onClick}>{busy ? busyLabel : label}</button>;
}
export function ProductFooter() {
  return <footer className="product-footer">
    <button className="privacy-link" onClick={() => telegram.openLink(`${location.origin}/privacy`)}>О ваших данных</button>
    <p className="footer-author">Автор бота <ContactLink url="https://t.me/MolonLabe">@MolonLabe</ContactLink></p>
  </footer>;
}
export function ContactLink({ url, children }: { url?: string; children: ReactNode }) {
  return url ? <a href={url} onClick={event => { event.preventDefault(); telegram.openContact(url); }}>{children}</a>
    : <span title="Telegram не разрешает открыть профиль этого пользователя">{children}</span>;
}
export function ToastViewport() {
  const [message, setMessage] = useState<string>();
  useEffect(() => {
    let timeout = 0;
    const show = (event: Event) => { setMessage((event as CustomEvent<string>).detail); window.clearTimeout(timeout); timeout = window.setTimeout(() => setMessage(undefined), 2600); };
    window.addEventListener(successEventName, show);
    return () => { window.removeEventListener(successEventName, show); window.clearTimeout(timeout); };
  }, []);
  return message ? <div className="toast" role="status" aria-live="polite">✓ {message}</div> : null;
}
