import { expect, it } from "vitest";
import { formatDuration } from "./format";

it.each([
  [90, 120, "90–120 мин"], [90, 90, "90 мин"], [undefined, 90, "до 90 мин"],
  [90, undefined, "от 90 мин"], [undefined, undefined, undefined], [0, 0, undefined], [120, 90, undefined]
])("formats only known game duration bounds (%s, %s)", (min, max, result) => {
  expect(formatDuration(min as number | undefined, max as number | undefined)).toBe(result);
});
