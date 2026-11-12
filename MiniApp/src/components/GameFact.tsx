import type { ReactNode } from "react";

export function GameFact({ kind, label, children }: { kind: "players" | "duration" | "rating"; label: string; children: ReactNode }) {
  return <span className="game-fact"><svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    {kind === "players" ? <><circle cx="9" cy="7" r="3" /><path d="M3 21v-3a6 6 0 0 1 12 0v3M16 4a3 3 0 0 1 0 6M21 21v-3a6 6 0 0 0-4-5.7" /></>
      : kind === "duration" ? <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>
      : <path d="m12 3 2.8 5.7 6.3.9-4.6 4.5 1.1 6.3-5.6-3-5.6 3 1.1-6.3L2.9 9.6l6.3-.9Z" />}
  </svg><span className="visually-hidden">{label}: </span><span>{children}</span></span>;
}
