import { describe, expect, it } from "vitest";
import { normalizePlayerCountRange } from "./playerCountRange";

describe("player count range", () => {
  it.each([
    [undefined, undefined],
    [0, 0],
    [0, 6],
    [2, 0],
    [5, 2],
  ])("defaults an incomplete or invalid %s-%s range to 1-12", (minimum, maximum) => {
    expect(normalizePlayerCountRange(minimum, maximum)).toEqual({
      minimum: 1, maximum: 12, wasDefaulted: true
    });
  });

  it("preserves a complete valid range", () => {
    expect(normalizePlayerCountRange(2, 5)).toEqual({
      minimum: 2, maximum: 5, wasDefaulted: false
    });
  });
});
