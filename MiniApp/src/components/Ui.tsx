import type { ReactNode } from "react";

export function Page({ title, subtitle, actions, children }: { title: string; subtitle?: string; actions?: ReactNode; children: ReactNode }) {
  return <main className="page"><header className="page-header"><div><h1>{title}</h1>{subtitle && <p>{subtitle}</p>}</div>{actions}</header>{children}</main>;
}
export function Card({ children, className = "" }: { children: ReactNode; className?: string }) { return <section className={`card ${className}`}>{children}</section>; }
export function Loading({ label = "Загрузка…" }: { label?: string }) { return <div className="stack" aria-live="polite"><div className="skeleton" /><div className="skeleton short" /><span className="muted">{label}</span></div>; }
export function Empty({ children }: { children: ReactNode }) { return <Card><p className="empty">{children}</p></Card>; }
export function ErrorState({ message, retry }: { message: string; retry?: () => void }) { return <Card className="error"><strong>Не удалось загрузить данные</strong><p>{message}</p>{retry && <button onClick={retry}>Повторить</button>}</Card>; }
export function Cover({ src, name }: { src?: string; name: string }) { return src ? <img className="cover" src={src} alt={`Обложка игры ${name}`} /> : <div className="cover placeholder" aria-hidden>🎲</div>; }
export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) { return <label className="field"><span>{label}</span>{children}{hint && <small>{hint}</small>}</label>; }
export function Notice({ children, kind = "info" }: { children: ReactNode; kind?: "info" | "warning" | "danger" | "success" }) { return <div className={`notice ${kind}`} role="status">{children}</div>; }
export function Badge({ children, tone = "neutral" }: { children: ReactNode; tone?: string }) { return <span className={`badge ${tone}`}>{children}</span>; }
