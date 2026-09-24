import { useEffect } from "react";
import { successEventName } from "../telegram/webApp";

export function useRefresh(reload: () => void) {
  useEffect(() => {
    const refresh = () => { if (document.visibilityState !== "hidden") reload(); };
    window.addEventListener(successEventName, refresh); window.addEventListener("focus", refresh); document.addEventListener("visibilitychange", refresh);
    return () => { window.removeEventListener(successEventName, refresh); window.removeEventListener("focus", refresh); document.removeEventListener("visibilitychange", refresh); };
  }, [reload]);
}
