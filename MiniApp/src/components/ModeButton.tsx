import type { ButtonHTMLAttributes } from "react";
import { CloseIcon } from "./CloseIcon";

export function ModeButton({ active = false, className = "", children, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { active?: boolean }) {
  return <button type="button" {...props} className={`mode-button ${active ? "active" : className}`} aria-pressed={active}>{active && <CloseIcon />}{children}</button>;
}
