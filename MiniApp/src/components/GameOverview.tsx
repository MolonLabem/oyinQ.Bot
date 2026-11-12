import type { ReactNode } from "react";
import { formatDuration } from "../app/format";
import type { GameCardData } from "./GameCard";
import { ComplexityBadge, ComplexityDetails } from "./ComplexityBadge";
import { GameFact, GameFacts } from "./GameFact";
import { GameTaxonomy } from "./GameTaxonomy";
import { Card, Cover } from "./Ui";

type GameInformation = GameCardData & {
  bggId: number; bggUrl?: string; yearPublished?: number; minAge?: number; description?: string;
  typeNames?: string[]; types?: string[];
  categories?: (string | { name: string })[]; mechanics?: { name: string }[];
};

export function GameOverview({ game, children }: { game: GameInformation; children?: ReactNode }) {
  return <Card className="game-overview">
    <div className="game-overview-heading"><Cover src={game.imageUrl ?? game.thumbnailImageUrl} name={game.name} /><header>
      {game.yearPublished ? <p className="game-year">{game.yearPublished} год</p> : null}
      <h1>{game.name}</h1>
      <div className="game-overview-meta"><ComplexityBadge info={game.complexityInfo} showUnknown />{game.bggRating != null && <GameFact kind="rating" label="Рейтинг BGG">{game.bggRating.toLocaleString("ru-RU", { maximumFractionDigits: 1, minimumFractionDigits: 1 })} BGG</GameFact>}<a href={game.bggUrl ?? `https://boardgamegeek.com/boardgame/${game.bggId}`} target="_blank" rel="noreferrer">Открыть на BGG</a></div>
    </header></div>
    <div className="game-overview-facts">
      <GameFacts players={game.minPlayers && game.maxPlayers ? <>{game.expansionPlayerRange ? "Базовая игра: " : ""}{game.minPlayers === game.maxPlayers ? game.minPlayers : `${game.minPlayers}–${game.maxPlayers}`}</> : undefined} duration={formatDuration(game.minPlayTimeMinutes, game.maxPlayTimeMinutes)} />
      <div className="game-overview-secondary">{game.bestPlayers && <span>Лучше: {game.bestPlayers}</span>}{game.minAge != null && <span>{game.minAge}+</span>}</div>
      {game.expansionPlayerRange && <span>С дополнениями: {game.expansionPlayerRange.minimum}–{game.expansionPlayerRange.maximum}</span>}
    </div>
    {children}
  </Card>;
}

export function GameDescription({ game }: { game: GameInformation }) {
  const types = game.typeNames ?? game.types;
  return <>
    {game.description && <details className="content-section detail-section detail-disclosure"><summary>Об игре</summary><div>{game.description.split("\n").map((text, i) => text ? <p key={i}>{text}</p> : null)}</div></details>}
    {Boolean(game.complexityInfo || types?.length || game.categories?.length || game.mechanics?.length) && <details className="content-section detail-section detail-disclosure game-characteristics"><summary>Характеристики игры</summary><ComplexityDetails info={game.complexityInfo} />
      <GameTaxonomy typeNames={types} categoryNames={game.categories?.map(item => typeof item === "string" ? item : item.name)} mechanicNames={game.mechanics?.map(item => item.name)} />
    </details>}
  </>;
}
