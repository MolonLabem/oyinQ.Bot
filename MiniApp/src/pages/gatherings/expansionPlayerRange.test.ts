import { expect, it } from "vitest";
import cases from "../../../../test-data/game-player-ranges.json";
import { fitPlayerLimits, resolvePlayerCountRange } from "./playerCountRange";

it.each(cases)("shared backend contract: $name", value => {
  expect(resolvePlayerCountRange(value.minimum, value.maximum, value.expansions, value.selected)).toEqual({
    minimum: value.expectedMinimum, maximum: value.expectedMaximum, wasDefaulted: value.defaulted,
  });
});

it("updates both targets for more seats and clamps them when the expansion is removed", () => {
  const base = { minimum: 2, maximum: 4, wasDefaulted: false };
  const expanded = { ...base, maximum: 5 };
  expect(fitPlayerLimits({ minimum: 2, desired: 4, maximum: 4 }, base, expanded)).toEqual({ minimum: 2, desired: 5, maximum: 5 });
  expect(fitPlayerLimits({ minimum: 2, desired: 5, maximum: 5 }, expanded, base)).toEqual({ minimum: 2, desired: 4, maximum: 4 });
  expect(fitPlayerLimits({ minimum: 2, desired: 3, maximum: 3 }, base, expanded)).toEqual({ minimum: 2, desired: 5, maximum: 5 });
  expect(fitPlayerLimits({ minimum: 2, desired: 3, maximum: 3 }, expanded, expanded)).toEqual({ minimum: 2, desired: 3, maximum: 3 });
});

it("selects eight players with Prophecy of Kings and restores six after removing it", () => {
  const expansion = { bggId: 315895, name: "Prophecy of Kings", minPlayers: 3, maxPlayers: 8 };
  const base = resolvePlayerCountRange(3, 6, [expansion], []);
  const expanded = resolvePlayerCountRange(3, 6, [expansion], [315895]);
  const limits = fitPlayerLimits({ minimum: 3, desired: 6, maximum: 6 }, base, expanded);
  expect(limits).toEqual({ minimum: 3, desired: 8, maximum: 8 });
  expect(fitPlayerLimits(limits, expanded, base)).toEqual({ minimum: 3, desired: 6, maximum: 6 });
});
