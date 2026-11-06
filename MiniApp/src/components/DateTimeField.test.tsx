// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { beforeEach, afterEach, it, expect, vi } from "vitest";
import { DateTimeField } from "./DateTimeField";
import { changeDateTime, readDateTime } from "./dateTimeTestHelper";
import { formatInstant, formatTime } from "../app/format";
let host: HTMLDivElement; let root: Root;
beforeEach(() => { Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true }); host = document.createElement("div"); document.body.append(host); root = createRoot(host); });
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });
function disableNativeDateTime() {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "type")!.set!;
  vi.spyOn(HTMLInputElement.prototype, "type", "set").mockImplementation(function (this: HTMLInputElement, value: string) {
    setter.call(this, value === "datetime-local" ? "text" : value);
  });
}
it("uses the browser's native regional date/time input while preserving exact API values", async () => {
  vi.stubGlobal("navigator", { languages: ["en-US"], language: "en-US" });
  function Form() { const [value, set] = useState(""); return <DateTimeField label="Когда" value={value} onChange={set} required />; }
  await act(async () => root.render(<Form />));
  const input = host.querySelector<HTMLInputElement>('input[type="datetime-local"]')!;
  expect(input.lang).toBe("en-US"); expect(input.required).toBe(true); expect(input.step).toBe("60");
  await changeDateTime(host, "2026-10-31T23:45"); expect(readDateTime(host)).toBe("2026-10-31T23:45");
  await changeDateTime(host, "2026-11-01T00:00"); expect(readDateTime(host)).toBe("2026-11-01T00:00");
  await changeDateTime(host, ""); expect(readDateTime(host)).toBe("");
});
it("retains exact native bounds and marks out-of-range edits invalid without rewriting the user's input", async () => {
  function Form() { const [value, set] = useState(""); return <DateTimeField label="Когда" value={value} onChange={set} min="2026-10-31T18:30" max="2026-11-01T11:15" />; }
  await act(async () => root.render(<Form />));
  const input = host.querySelector<HTMLInputElement>('input[type="datetime-local"]')!;
  expect(input.min).toBe("2026-10-31T18:30"); expect(input.max).toBe("2026-11-01T11:15");
  await changeDateTime(host, "2026-10-31T12:00"); expect(input.validity.rangeUnderflow).toBe(true);
  expect(readDateTime(host)).toBe("2026-10-31T12:00");
  await changeDateTime(host, "2026-11-01T18:30"); expect(input.validity.rangeOverflow).toBe(true);
  await changeDateTime(host, "2026-11-01T11:15"); expect(input.checkValidity()).toBe(true);
});
it("falls back to explicit 00–23 hours and preserves exact minutes and midnight without a native picker", async () => {
  disableNativeDateTime(); vi.stubGlobal("navigator", undefined);
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
  disableNativeDateTime();
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
