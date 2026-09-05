import { describe, expect, it } from "vitest";
import { bggImportProgressText, clubImportResultText } from "./bggImportProgress";

describe("BGG import progress", () => {
  it("describes real collection stages without treating them as abstract steps", () => {
    expect(bggImportProgressText({ status: "Running", stage: "FetchingGames" })).toContain("подробные данные игр");
    expect(bggImportProgressText({ status: "Running", stage: "FetchingExpansions", foundGames: 2 })).toContain("2 игры");
    expect(bggImportProgressText({ status: "Running", stage: "Preparing", foundGames: 184, foundExpansions: 12 })).toContain("184 игры");
  });

  it("separates added and already existing games", () => {
    expect(clubImportResultText({ foundGames: 42, addedGames: 31, addedExpansions: 5, orphanExpansions: 1 }))
      .toBe("Добавлено: 31 игра · Уже были: 11. Дополнений добавлено: 5 · Без базовой игры: 1.");
    expect(clubImportResultText({ foundGames: 0, addedGames: 3, addedExpansions: 0, orphanExpansions: 0 }))
      .toBe("Добавлено: 3 игры. Дополнений добавлено: 0.");
  });
});
