import { createContext, useContext, useEffect, useRef, type ReactNode } from "react";
import { telegram } from "../telegram/webApp";

const Scope = createContext({ active: true, depth: 0 });

// Use the existing Telegram listener stack, with explicit nesting independent of
// React's child-first effect order. Hidden, retained screens do not handle Back.
export function BackButtonScope({ active = true, children }: { active?: boolean; children: ReactNode }) {
  const parent = useContext(Scope);
  return <Scope.Provider value={{ active: parent.active && active, depth: parent.depth + 1 }}>{children}</Scope.Provider>;
}

export function useBackButton(show: boolean, handler: () => void, modal = false) {
  const { active, depth } = useContext(Scope);
  const callback = useRef(handler); callback.current = handler;
  useEffect(() => {
    if (active && show) return telegram.back(true, () => callback.current(), depth + (modal ? 100 : 0));
  }, [active, depth, show, modal]);
}
