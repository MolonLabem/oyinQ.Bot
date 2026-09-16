import { expect, it, vi } from "vitest";
import { backButtonStack } from "./backButtonStack";

it("consumes Back in the modal and restores the latest screen after close", () => {
  const button = { show: vi.fn(), hide: vi.fn(), onClick: vi.fn(), offClick: vi.fn() };
  const back = backButtonStack(button);
  const screen = vi.fn(), modal = vi.fn(), next = vi.fn();
  const disposeScreen = back(true, screen);
  const disposeModal = back(true, modal, 1);
  disposeScreen();
  const disposeNext = back(true, next);
  const dispatch = button.onClick.mock.calls[0][0] as () => void;
  dispatch(); expect(modal).toHaveBeenCalledOnce(); expect(screen).not.toHaveBeenCalled(); expect(next).not.toHaveBeenCalled();
  disposeModal(); dispatch(); expect(next).toHaveBeenCalledOnce();
  disposeNext(); expect(button.hide).toHaveBeenCalled(); expect(button.offClick).toHaveBeenCalledWith(dispatch);
  expect(button.onClick).toHaveBeenCalledOnce();
});
