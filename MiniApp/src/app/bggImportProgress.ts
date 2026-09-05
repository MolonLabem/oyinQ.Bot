import { plural } from "./format";

export type BggImportStage = "Queued" | "FetchingGames" | "FetchingExpansions" | "Preparing" | "Saving" | "Completed" | "Failed" | "Cancelled";

export function bggImportProgressText(value: { status: string; stage?: BggImportStage; foundGames?: number; foundExpansions?: number }) {
  const games = value.foundGames ?? 0;
  const expansions = value.foundExpansions ?? 0;
  if (value.status === "Queued" || !value.stage || value.stage === "Queued") return "Импорт поставлен в очередь.";
  if (value.stage === "FetchingGames") return "Получаем коллекцию и подробные данные игр с BGG…";
  if (value.stage === "FetchingExpansions") return `Получили основные игры — ${plural(games, "игра", "игры", "игр")}. Загружаем дополнения…`;
  if (value.stage === "Preparing") return `Получили коллекцию BGG — ${plural(games, "игра", "игры", "игр")} · ${plural(expansions, "дополнение", "дополнения", "дополнений")}. Готовим данные…`;
  if (value.stage === "Saving") return `Сохраняем в коллекцию: ${plural(games, "игра", "игры", "игр")} · ${plural(expansions, "дополнение", "дополнения", "дополнений")}…`;
  return "Импорт выполняется в фоне…";
}

export function clubImportResultText(value: { foundGames: number; addedGames: number; addedExpansions: number; orphanExpansions: number }) {
  const existing = value.foundGames >= value.addedGames ? value.foundGames - value.addedGames : undefined;
  return `Добавлено: ${plural(value.addedGames, "игра", "игры", "игр")}${existing === undefined ? "" : ` · Уже были: ${existing}`}. Дополнений добавлено: ${value.addedExpansions}${value.orphanExpansions ? ` · Без базовой игры: ${value.orphanExpansions}` : ""}.`;
}
