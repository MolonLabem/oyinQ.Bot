import { useEffect, useRef } from "react";
import { useBackButton } from "./useBackButton";

// Shared native-dialog lifecycle: keep the sheet above the keyboard and restore
// the underlying screen's scroll, focus and Telegram Back handler on dismissal.
export function useMobileDialog(onClose: () => void) {
  const dialog = useRef<HTMLDialogElement>(null);
  useBackButton(true, onClose, true);
  useEffect(() => {
    const node = dialog.current!;
    const opener = document.activeElement as HTMLElement | null;
    const scroll = window.scrollY;
    const previous = { position: document.body.style.position, top: document.body.style.top, width: document.body.style.width };
    document.body.style.position = "fixed"; document.body.style.top = `-${scroll}px`; document.body.style.width = "100%";
    const viewport = window.visualViewport;
    const resize = () => {
      node.style.setProperty("--catalog-viewport-height", `${viewport?.height ?? window.innerHeight}px`);
      node.style.bottom = `${Math.max(0, window.innerHeight - ((viewport?.height ?? window.innerHeight) + (viewport?.offsetTop ?? 0)))}px`;
    };
    resize(); viewport?.addEventListener("resize", resize); viewport?.addEventListener("scroll", resize);
    node.showModal();
    return () => {
      viewport?.removeEventListener("resize", resize); viewport?.removeEventListener("scroll", resize);
      node.close(); Object.assign(document.body.style, previous); window.scrollTo(0, scroll); opener?.focus();
    };
  }, []);
  return dialog;
}
