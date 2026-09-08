import { useCallback, useLayoutEffect, useRef } from "react";
import { api } from "../api/client";

// A keyed screen owns its requests. Never look up a mutable "current community"
// here: URLs/bodies keep the explicit target captured by the original action.
export function useScreenRequest(): typeof api {
  const lifetime = useRef({ active: true });
  useLayoutEffect(() => {
    const current = { active: true };
    lifetime.current = current;
    return () => { current.active = false; };
  }, []);
  return useCallback(async <T,>(path: string, options?: RequestInit): Promise<T> => {
    const current = lifetime.current;
    const ensureActive = () => {
      if (!current.active) throw new Error("Экран изменился. Повторите действие в выбранном сообществе.");
    };
    ensureActive();
    const result = await api<T>(path, options);
    // A response from an unmounted screen must not navigate or show success in
    // its replacement. Requests already sent retain their original target.
    ensureActive();
    return result;
  }, []);
}
