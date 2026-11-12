import { renderToStaticMarkup } from "react-dom/server";
import { expect, it } from "vitest";
import { GameCard } from "./GameCard";
import { GatheringCard } from "./GatheringCard";

const game = { name: "Немезида", minPlayers: 1, maxPlayers: 5, minPlayTimeMinutes: 90, maxPlayTimeMinutes: 180,
  bestPlayers: "4", bggRating: 8.2, complexityInfo: { level: "MediumHeavy", displayName: "Сложная", cssClass: "complexity-hard" } };

it.each(["catalog", "profile", "wishlist"] as const)("keeps the same metadata in the %s card", context => {
  const markup = renderToStaticMarkup(<GameCard game={game} context={context} open={() => {}} />);
  expect(markup).toContain("complexity-hard"); expect(markup).toContain("Лучше: 4");
  expect(markup).toContain("1–5"); expect(markup).toContain("90–180 мин"); expect(markup).toContain("8,2 BGG");
  expect(markup).toContain('aria-label="Открыть игру: Немезида"');
});

it("uses occupied seats and signup state for gatherings instead of the game's recommended range", () => {
  const markup = renderToStaticMarkup(<GatheringCard item={{ isOrganizer: false, currentUserStatus: "Confirmed", status: "Full", card: {
    ...game, publicId: "g", gameName: game.name, organizerName: "Пётр", canTeachRules: true, rulesText: "Правила объясню", localDateTime: "чт, 14:00", occupiedSeats: 4, maximumPlayers: 4, statusText: "Мест нет"
  } }} open={() => {}} />);
  expect(markup).toContain("4 из 4"); expect(markup).toContain("90–180 мин"); expect(markup).toContain("complexity-hard");
  expect(markup).toContain("Вы записаны"); expect(markup).not.toContain("Собирает");
  expect(markup).not.toContain("Лучше:"); expect(markup).not.toContain("8,2 BGG");
});

it("makes unknown difficulty explicit without inventing player counts or duration", () => {
  const markup = renderToStaticMarkup(<GameCard game={{ name: "Старый снимок" }} context="profile" open={() => {}} />);
  expect(markup).toContain("Сложность —"); expect(markup).not.toContain("game-facts-row");
});
