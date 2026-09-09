import { expect, it } from "vitest";
import cases from "../../../../test-data/game-player-ranges.json";
import { fitPlayerLimits, resolvePlayerCountRange } from "./playerCountRange";

it.each(cases)("shared backend contract: $name", value => {
  expect(resolvePlayerCountRange(value.minimum, value.maximum, value.expansions, value.selected)).toEqual({
    minimum: value.expectedMinimum, maximum: value.expectedMaximum, wasDefaulted: value.defaulted,
  });
});

it("offers the fifth seat and clamps it when the expansion is removed, preserving deliberate limits", () => {
  const base = { minimum: 2, maximum: 4, wasDefaulted: false };
  const expanded = { ...base, maximum: 5 };
  expect(fitPlayerLimits({ minimum: 2, desired: 4, maximum: 4 }, base, expanded)).toEqual({ minimum: 2, desired: 4, maximum: 5 });
  expect(fitPlayerLimits({ minimum: 2, desired: 5, maximum: 5 }, expanded, base)).toEqual({ minimum: 2, desired: 4, maximum: 4 });
  expect(fitPlayerLimits({ minimum: 2, desired: 3, maximum: 3 }, base, expanded).maximum).toBe(3);
});
