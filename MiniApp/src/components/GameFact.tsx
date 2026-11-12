import type { ReactNode } from "react";

const factEmoji = { players: "👥", duration: "⏱️", rating: "⭐", date: "📅", time: "🕒", rules: "📖", box: "📦" };

export function GameFact({ kind, label, children }: { kind: keyof typeof factEmoji; label: string; children: ReactNode }) {
  return <span className="game-fact"><span className="game-fact-icon" aria-hidden="true">{factEmoji[kind]}</span>
    <span className="visually-hidden">{label}: </span><span className="game-fact-value">{children}</span></span>;
}

export function GameFacts({ players, duration, playersLabel = "Игроки" }: { players?: ReactNode; duration?: string | null; playersLabel?: string }) {
  if (!players && !duration) return null;
  return <span className="game-facts"><span className={`game-facts-row${players && duration ? " paired" : ""}`}>
    {players && <GameFact kind="players" label={playersLabel}>{players}</GameFact>}
    {duration && <GameFact kind="duration" label="Длительность">{duration}</GameFact>}
  </span></span>;
}
