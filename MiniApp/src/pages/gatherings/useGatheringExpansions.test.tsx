// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot } from "react-dom/client";
import { expect, it, vi } from "vitest";
import { api } from "../../api/client";
import { ExpansionPicker } from "../../components/ExpansionPicker";
import { useGatheringExpansions } from "./useGatheringExpansions";

vi.mock("../../api/client", () => ({ api: vi.fn() }));

it("retains attached expansions and multiselection through partial results, retry and rerender", async () => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.mocked(api).mockResolvedValueOnce({ game: { bggId: 42 }, expansions: [{ bggId: 99, name: "Новое" }], expansionLookupIncomplete: true })
    .mockResolvedValueOnce({ game: { bggId: 42 }, expansions: [{ bggId: 99, name: "Новое" }, { bggId: 100, name: "Второе" }] });
  function Form() {
    const [selected, setSelected] = useState([77]);
    const lookup = useGatheringExpansions(42, [{ bggId: 77, name: "Сохранённое" }]);
    return <>{lookup.notice}<ExpansionPicker expansions={lookup.expansions} selected={selected} onChange={setSelected} /></>;
  }
  const host = document.createElement("div"); document.body.append(host);
  const root = createRoot(host);
  try {
    await act(async () => root.render(<Form />));
    const boxes = () => [...host.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];
    expect(boxes().map(x => x.checked)).toEqual([true, false]);
    await act(async () => boxes()[1].click());
    await act(async () => host.querySelector<HTMLButtonElement>("button")!.click());
    await act(async () => root.render(<Form />));
    expect(boxes().map(x => x.checked)).toEqual([true, true, false]);
    expect(host.textContent).not.toContain("Не все данные");
    expect(api).toHaveBeenCalledTimes(2);
  } finally { await act(async () => root.unmount()); host.remove(); vi.resetAllMocks(); }
});

it("allows a failed lookup to retry to an empty expansion list", async () => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  vi.mocked(api).mockRejectedValueOnce(new Error("offline"))
    .mockResolvedValueOnce({ game: { bggId: 42 }, expansions: [] });
  function Form() { return useGatheringExpansions(42, []).notice; }
  const host = document.createElement("div"); const root = createRoot(host);
  try {
    await act(async () => root.render(<Form />));
    expect(host.textContent).toContain("Можно продолжить");
    await act(async () => host.querySelector<HTMLButtonElement>("button")!.click());
    expect(host.textContent).toBe("У игры нет дополнений.");
  } finally { await act(async () => root.unmount()); vi.resetAllMocks(); }
});


it("does not retry a failed picker lookup until requested and resets retry scope on game change", async () => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const failed = { status: "failed" as const };
  const complete = { status: "complete" as const };
  vi.mocked(api).mockResolvedValue({ game: { bggId: 42 }, expansions: [] });
  function Form({ id, initial }: { id: number; initial: import("../../components/gamePickerModel").ExpansionLookup }) {
    return useGatheringExpansions(id, [], initial).notice;
  }
  const host = document.createElement("div"); const root = createRoot(host);
  try {
    await act(async () => root.render(<Form id={42} initial={failed} />));
    expect(api).not.toHaveBeenCalled();
    await act(async () => host.querySelector<HTMLButtonElement>("button")!.click());
    expect(api).toHaveBeenCalledTimes(1);
    await act(async () => root.render(<Form id={43} initial={complete} />));
    expect(api).toHaveBeenCalledTimes(1);
    expect(host.textContent).toBe("У игры нет дополнений.");
  } finally { await act(async () => root.unmount()); vi.resetAllMocks(); }
});
