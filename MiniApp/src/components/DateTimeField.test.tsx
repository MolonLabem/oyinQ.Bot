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
it.each(["en-US", "en-GB", "ru-RU"])("uses day-first dates and 00–23 hours on a %s device", async language => {
  vi.stubGlobal("navigator", { languages: [language], language });
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
  const date = host.querySelector<HTMLInputElement>('input[data-date-input]')!;
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, "31.10.2026"); date.dispatchEvent(new Event("input", { bubbles: true })); });
  expect(readDateTime(host)).toBe("2026-10-31T18:30");
  const hours = host.querySelector<HTMLSelectElement>('[aria-label="Когда: часы"]')!;
  expect(hours.options[17].disabled).toBe(true); expect(hours.options[18].disabled).toBe(false);
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, "01.11.2026"); date.dispatchEvent(new Event("input", { bubbles: true })); });
  expect(readDateTime(host)).toBe("2026-11-01T11:15"); expect(hours.options[12].disabled).toBe(true);
});


it("keeps the chosen time through partial date edits and round-trips a saved wall time", async () => {
  function Form() { const [value, set] = useState("2026-08-07T23:45"); return <><DateTimeField label="Когда" value={value} onChange={set} required /><output>{value}</output></>; }
  await act(async () => root.render(<Form />));
  const date = host.querySelector<HTMLInputElement>('[data-date-input]')!;
  expect(date.value).toBe("07.08.2026");
  async function enter(text: string) {
    await act(async () => { Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, text); date.dispatchEvent(new Event("input", { bubbles: true })); });
  }
  await enter("08.08.");
  expect(host.querySelector("output")!.textContent).toBe("");
  expect(date.checkValidity()).toBe(false);
  await enter("08.08.2026");
  expect(host.querySelector("output")!.textContent).toBe("2026-08-08T23:45");
  expect(date.checkValidity()).toBe(true);
  await enter(""); expect(readDateTime(host)).toBe("");
});

it("rejects a saved time outside updated bounds until the user corrects it", async () => {
  function Form() { const [value, set] = useState("2026-10-31T12:00"); return <form><DateTimeField label="Когда" value={value} onChange={set} min="2026-10-31T18:30" max="2026-11-01T11:15" required /></form>; }
  await act(async () => root.render(<Form />));
  expect(host.querySelector("form")!.checkValidity()).toBe(false);
  await changeDateTime(host, "2026-10-31T18:30");
  expect(host.querySelector("form")!.checkValidity()).toBe(true);
});
