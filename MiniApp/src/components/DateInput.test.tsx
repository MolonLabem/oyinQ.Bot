// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { DateInput } from "./DateInput";

let host: HTMLDivElement; let root: Root;
beforeEach(() => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.unstubAllGlobals(); });
async function enter(value: string) {
  const input = host.querySelector("input")!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
function Form({ bounded = false }: { bounded?: boolean }) {
  const [value, setValue] = useState("2026-11-06");
  return <><DateInput aria-label="Дата" value={value} onChange={setValue} required min={bounded ? "2026-11-06" : undefined} max={bounded ? "2026-11-08" : undefined} /><output>{value}</output></>;
}

it.each(["en-US", "en-GB", "ru-RU"])("uses day-first input with a %s device and keeps ISO values", async language => {
  vi.stubGlobal("navigator", { language, languages: [language] });
  await act(async () => root.render(<Form bounded />));
  const input = host.querySelector("input")!;
  expect(input.type).toBe("text"); expect(input.value).toBe("06.11.2026");
  await enter("08.11.2026"); expect(host.querySelector("output")!.textContent).toBe("2026-11-08");
});

it("shows day-month-year and clears stale saved values while an invalid date is edited", async () => {
  vi.stubGlobal("navigator", undefined); await act(async () => root.render(<Form />));
  const input = host.querySelector("input")!;
  expect(input.type).toBe("text"); expect(input.placeholder).toBe("ДД.ММ.ГГГГ"); expect(input.value).toBe("06.11.2026");
  await enter("30.02.2026"); expect(input.value).toBe("30.02.2026"); expect(input.checkValidity()).toBe(false);
  expect(host.querySelector("output")!.textContent).toBe("");
  await enter("29.02.2024"); expect(input.checkValidity()).toBe(true); expect(host.querySelector("output")!.textContent).toBe("2024-02-29");
  await enter("06 11"); expect(input.value).toBe("06 11"); expect(host.querySelector("output")!.textContent).toBe("");
  await enter("06 11 2026"); expect(input.checkValidity()).toBe(true); expect(host.querySelector("output")!.textContent).toBe("2026-11-06");
});

it("applies inclusive date bounds and allows clearing a required date", async () => {
  vi.stubGlobal("navigator", { languages: ["bad_locale"], language: "zz-ZZ" });
  await act(async () => root.render(<Form bounded />));
  const input = host.querySelector("input")!;
  await enter("05/11/2026"); expect(input.checkValidity()).toBe(false); expect(host.querySelector("output")!.textContent).toBe("");
  await enter("08/11/2026"); expect(input.checkValidity()).toBe(true); expect(host.querySelector("output")!.textContent).toBe("2026-11-08");
  await enter(""); expect(input.value).toBe(""); expect(input.checkValidity()).toBe(false); expect(host.querySelector("output")!.textContent).toBe("");
});


it("accepts numeric keyboard entry and distinguishes an ambiguous day and month", async () => {
  vi.stubGlobal("navigator", { language: "en-US", languages: ["en-US"] });
  await act(async () => root.render(<Form />));
  await enter("");
  for (const digit of "07082026") await enter(host.querySelector("input")!.value + digit);
  expect(host.querySelector("input")!.value).toBe("07.08.2026");
  expect(host.querySelector("output")!.textContent).toBe("2026-08-07");
  await enter("12.31.2026");
  expect(host.querySelector("input")!.checkValidity()).toBe(false);
  expect(host.querySelector("output")!.textContent).toBe("");
});

it("reflects an externally loaded date and keeps disabled fields out of validation", async () => {
  await act(async () => root.render(<DateInput value="2026-11-06" onChange={() => {}} required />));
  await act(async () => root.render(<DateInput value="2027-01-02" onChange={() => {}} required />));
  expect(host.querySelector("input")!.value).toBe("02.01.2027");
  await act(async () => root.render(<DateInput value="" onChange={() => {}} required disabled />));
  expect(host.querySelector("input")!.checkValidity()).toBe(true);
});
