// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { Tabs } from "./Ui";

let host: HTMLDivElement; let root: Root;
const change = vi.fn();
const items = [
  { id: "collection", label: "Игры" }, { id: "calendar", label: "Календарь" },
  { id: "settings", label: "Настройки" }
];
function Screen() {
  const [active, setActive] = useState("collection");
  return <><Tabs label="Разделы профиля" items={items} active={active}
    onChange={id => { change(id); setActive(id); }} /><button>После вкладок</button></>;
}
beforeEach(() => {
  vi.clearAllMocks(); Element.prototype.scrollIntoView = vi.fn();
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  host = document.createElement("div"); document.body.append(host); root = createRoot(host);
});
afterEach(async () => { await act(async () => root.unmount()); host.remove(); vi.restoreAllMocks(); });
const tab = (id: number) => host.querySelectorAll<HTMLButtonElement>('[role="tab"]')[id];
async function key(value: string) {
  await act(async () => document.activeElement!.dispatchEvent(new KeyboardEvent("keydown", { key: value, bubbles: true, cancelable: true })));
}

it("moves focus with arrows, wraps, and supports Home/End without activating remote content", async () => {
  await act(async () => root.render(<Screen />));
  expect([...host.querySelectorAll('[role="tab"]')].map(x => x.getAttribute("tabindex"))).toEqual(["0", "-1", "-1"]);
  await act(async () => tab(0).focus()); await key("ArrowRight");
  expect(document.activeElement).toBe(tab(1)); expect(tab(1).tabIndex).toBe(0);
  expect(tab(0).getAttribute("aria-selected")).toBe("true"); expect(change).not.toHaveBeenCalled();
  await key("End"); expect(document.activeElement).toBe(tab(2));
  await key("ArrowRight"); expect(document.activeElement).toBe(tab(0));
  await key("ArrowLeft"); expect(document.activeElement).toBe(tab(2));
  await key("Home"); expect(document.activeElement).toBe(tab(0));
  await key("ArrowDown"); expect(document.activeElement).toBe(tab(0));
  expect(change).not.toHaveBeenCalled();
});

it("preserves click activation and returns the tab stop to selection after leaving the list", async () => {
  await act(async () => root.render(<Screen />));
  await act(async () => tab(1).click());
  expect(change).toHaveBeenLastCalledWith("calendar"); expect(tab(1).getAttribute("aria-selected")).toBe("true");
  await act(async () => tab(1).focus()); await key("ArrowRight");
  expect(document.activeElement).toBe(tab(2)); expect(change).toHaveBeenCalledTimes(1);
  await act(async () => host.querySelector<HTMLButtonElement>('button:not([role="tab"])')!.focus());
  expect(tab(1).tabIndex).toBe(0); expect(tab(2).tabIndex).toBe(-1);
});

it("leaves browser shortcuts intact and updates its entry tab after an external selection", async () => {
  await act(async () => root.render(<Tabs label="Разделы" items={items} active="collection" onChange={change} />));
  await act(async () => tab(0).focus());
  const shortcut = new KeyboardEvent("keydown", { key: "ArrowLeft", altKey: true, bubbles: true, cancelable: true });
  await act(async () => tab(0).dispatchEvent(shortcut));
  expect(shortcut.defaultPrevented).toBe(false); expect(document.activeElement).toBe(tab(0));
  await act(async () => root.render(<Tabs label="Разделы" items={items} active="settings" onChange={change} />));
  expect(tab(2).tabIndex).toBe(0); expect(tab(0).tabIndex).toBe(-1);
  expect(change).not.toHaveBeenCalled();
});
