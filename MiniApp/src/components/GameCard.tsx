import { useId, type ReactNode } from "react";
import type { ComplexityInfo } from "../api/types";
import { formatDuration } from "../app/format";
import { ComplexityBadge } from "./ComplexityBadge";
import { GameFact, GameFacts } from "./GameFact";
import { Cover } from "./Ui";

export type GameCardData = {
  name: string; thumbnailImageUrl?: string; imageUrl?: string;
  complexityInfo?: ComplexityInfo | null; bggRating?: number | null;
  minPlayers?: number; maxPlayers?: number; bestPlayers?: string;
  minPlayTimeMinutes?: number; maxPlayTimeMinutes?: number;
  expansionPlayerRange?: { minimum: number; maximum: number };
};

type CardProps = {
  game: GameCardData;
  context: "catalog" | "profile" | "gathering" | "wishlist";
  open: () => void;
  embedded?: boolean;
  secondary?: ReactNode;
  children?: ReactNode;
  players?: ReactNode;
  selection?: { checked: boolean; disabled: boolean; toggle: () => void };
};

// Context changes the facts and action, never the placement of shared game metadata.
export function GameCard({ game, context, open, embedded = false, secondary, children, players, selection }: CardProps) {
  const factsId = useId();
  const secondaryId = useId();
  const className = `game-card ${embedded ? "embedded" : "card"} ${context}-card${selection?.checked ? " selected" : ""}`;
  const content = <>
    <span className="game-card-header">
      <Cover src={game.thumbnailImageUrl ?? game.imageUrl} name={game.name} />
      <span className="game-card-body">
        <span className="game-card-top"><strong className="game-card-title">{game.name}</strong><ComplexityBadge info={game.complexityInfo} showUnknown /></span>
        <span className="game-card-secondary-info" id={secondaryId}>{secondary}
        {context !== "gathering" && game.bggRating != null && <GameFact kind="rating" label="Рейтинг BGG">{game.bggRating.toLocaleString("ru-RU", { maximumFractionDigits: 1, minimumFractionDigits: 1 })} BGG</GameFact>}
        {context !== "gathering" && game.bestPlayers && <span className="game-card-secondary">Лучше: {game.bestPlayers}</span>}</span>
      </span>
    </span>
    <span className="game-card-content" id={factsId}>
      <span className="visually-hidden">Сложность: {game.complexityInfo?.displayName ?? "не указана"}. </span>
      <GameFacts players={players ?? (game.minPlayers && game.maxPlayers ? <>{game.expansionPlayerRange ? "Базовая игра: " : ""}{game.minPlayers === game.maxPlayers ? game.minPlayers : `${game.minPlayers}–${game.maxPlayers}`}</> : undefined)} playersLabel={context === "gathering" ? "Занято мест" : "Игроки"} duration={formatDuration(game.minPlayTimeMinutes, game.maxPlayTimeMinutes)} />
      {game.expansionPlayerRange && <span className="game-card-secondary">С дополнениями: {game.expansionPlayerRange.minimum}–{game.expansionPlayerRange.maximum}</span>}
      {children}
    </span>
  </>;
  return selection ? <label className={`${className} game-card-selectable`}>
    <span className="collection-select"><input type="checkbox" aria-label={`Выбрать «${game.name}»`} checked={selection.checked} disabled={selection.disabled} onChange={selection.toggle} /></span>
    {content}
  </label> : <button type="button" className={className} aria-label={`${context === "gathering" ? "Открыть сбор" : "Открыть игру"}: ${game.name}`} aria-describedby={`${secondaryId} ${factsId}`} onClick={open}>{content}</button>;
}
