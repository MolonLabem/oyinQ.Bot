// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, it, expect, vi } from "vitest";
import { DateTimeField } from "./DateTimeField";
import { changeDateTime, readDateTime } from "./dateTimeTestHelper";
import { formatInstant, formatTime } from "../app/format";
let host: HTMLDivElement; let root: Root;
beforeEach(() => { Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); host = document.createElement("div"); document.body.append(host); root = createRoot(host); });
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });
it("uses explicit 00–23 hours, preserves exact minutes and midnight", async () => {
  function Form() { const [value, set] = useState(""); return <DateTimeField label="Когда" value={value} onChange={set} />; }
  await act(async () => root.render(<Form />));
  expect(host.querySelector('input[type="datetime-local"]')).toBeNull();
  await changeDateTime(host, "2026-10-31T23:45"); expect(readDateTime(host)).toBe("2026-10-31T23:45");
  await changeDateTime(host, "2026-11-01T00:00"); expect(readDateTime(host)).toBe("2026-11-01T00:00");
  const hours = host.querySelector<HTMLSelectElement>('[aria-label="Когда: часы"]')!;
  expect([...hours.options].map(option => option.text)).toEqual(Array.from({ length: 24 }, (_, hour) => String(hour).padStart(2, "0")));
  expect(formatInstant("2026-11-01T00:00:00Z", "UTC")).toContain("00:00");
  expect(formatTime("2026-11-01T23:45:00Z")).not.toMatch(/AM|PM/);
});
it("clamps the date to exact operating times and disables hours outside the range", async () => {
  function Form() { const [value, set] = useState(""); return <DateTimeField label="Когда" value={value} onChange={set} min="2026-10-31T18:30" max="2026-11-01T11:15" />; }
  await act(async () => root.render(<Form />));
  const date = host.querySelector<HTMLInputElement>('input[type="date"]')!;
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, "2026-10-31"); date.dispatchEvent(new Event("input", { bubbles: true })); });
  expect(readDateTime(host)).toBe("2026-10-31T18:30");
  const hours = host.querySelector<HTMLSelectElement>('[aria-label="Когда: часы"]')!;
  expect(hours.options[17].disabled).toBe(true); expect(hours.options[18].disabled).toBe(false);
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, "2026-11-01"); date.dispatchEvent(new Event("input", { bubbles: true })); });
  expect(readDateTime(host)).toBe("2026-11-01T11:15"); expect(hours.options[12].disabled).toBe(true);
});
