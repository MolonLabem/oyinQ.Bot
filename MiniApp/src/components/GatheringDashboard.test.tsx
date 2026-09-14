// @vitest-environment jsdom
import { act } from "react";
import { createRoot } from "react-dom/client";
import { expect, it, vi } from "vitest";
vi.mock("../api/client", () => ({ api: vi.fn(async () => ({ items: [], hasMore: false })), json: vi.fn() }));
vi.mock("../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
import { api } from "../api/client";
import { GatheringDashboard } from "./GatheringDashboard";

it("uses the global personal endpoint without requiring a selected community", async () => {
  Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });
  const host = document.createElement("div"); const root = createRoot(host);
  try {
    await act(async () => root.render(<GatheringDashboard open={() => {}} />));
    expect(api).toHaveBeenCalledWith("/profile/dashboard");
    expect(host.textContent).not.toContain("действий администратора");
  } finally { await act(async () => root.unmount()); }
});
