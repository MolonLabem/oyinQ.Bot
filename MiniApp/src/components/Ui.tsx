import { type ReactNode, useEffect, useState } from "react";
import { successEventName } from "../telegram/webApp";
import { telegram } from "../telegram/webApp";

export function Page({ title, subtitle, actions, children }: { title: string; subtitle?: string; actions?: ReactNode; children: ReactNode }) {
  useEffect(() => { window.scrollTo({ top: 0, behavior: "auto" }); }, [title]);
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
