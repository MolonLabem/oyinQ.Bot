// @vitest-environment jsdom
import { act } from "react";
import { createRoot } from "react-dom/client";
import { expect, it, vi } from "vitest";
vi.mock("../telegram/webApp", () => ({ telegram: { openContact: vi.fn() }, successEventName: "success" }));
import { telegram } from "../telegram/webApp";
import { ProductFooter } from "./Ui";

it("keeps BGG attribution and opens the author's Telegram profile through the shared adapter", async () => {
  const host = document.createElement("div");
  const root = createRoot(host);
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  try {
    await act(async () => root.render(<ProductFooter />));
    expect(host.querySelector('a[href="https://boardgamegeek.com"] img')).toBeTruthy();
    const link = host.querySelector<HTMLAnchorElement>('a[href="https://t.me/MolonLabe"]')!;
    expect(link.textContent).toBe("@MolonLabe");
    expect(host.textContent).toContain("Автор бота — @MolonLabe");
    await act(async () => link.click());
    expect(telegram.openContact).toHaveBeenCalledWith("https://t.me/MolonLabe");
  } finally { await act(async () => root.unmount()); }
});
