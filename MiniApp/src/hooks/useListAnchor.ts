import { useLayoutEffect, useRef, useState } from "react";

// Capture the changed card and its neighbours before refreshing server ordering.
// Restoring after the new projection also handles a card leaving the active filter/page.
export function useListAnchor(result: unknown) {
  const list = useRef<HTMLDivElement>(null);
  const pending = useRef<{ id: string; candidates: { id: string; top: number }[] } | undefined>(undefined);
  const [notice, setNotice] = useState<string>();
  function capture(id: string) {
    const cards = [...list.current?.querySelectorAll<HTMLElement>("[data-box-game]") ?? []];
    const index = cards.findIndex(card => card.dataset.boxGame === id);
    if (index < 0) return;
    const saved = { id, candidates: [...cards.slice(index), ...cards.slice(0, index).reverse()]
      .map(card => ({ id: card.dataset.boxGame!, top: card.getBoundingClientRect().top })) };
    setNotice(undefined);
    // Arm only after the write succeeds: a focus refresh while saving must not
    // consume the anchor before filtering/reordering actually changes the list.
    return () => { pending.current = saved; };
  }
  useLayoutEffect(() => {
    const saved = pending.current;
    if (!saved || !list.current || list.current.closest("[hidden]")) return;
    pending.current = undefined;
    const cards = [...list.current.querySelectorAll<HTMLElement>("[data-box-game]")];
    for (const candidate of saved.candidates) {
      const card = cards.find(card => card.dataset.boxGame === candidate.id);
      if (card) { window.scrollTo(0, Math.max(0, window.scrollY + card.getBoundingClientRect().top - candidate.top)); break; }
    }
    if (!cards.some(card => card.dataset.boxGame === saved.id)) setNotice("Решение сохранено. Игра больше не входит в текущую страницу списка.");
  }, [result]);
  return { list, capture, notice };
}
