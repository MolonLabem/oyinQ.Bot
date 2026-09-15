import { useEffect, useState } from "react";
import { api } from "../../api/client";
import type { BggDetails, Expansion } from "../../api/types";
import { mergeExpansions, type ExpansionLookup } from "../../components/gamePickerModel";
import { Notice } from "../../components/Ui";

// Selection belongs to the form. Refreshing provider options never clears it.
export function useGatheringExpansions(bggId: number | undefined, saved: Expansion[], initial?: ExpansionLookup) {
  const [result, setResult] = useState<{ id: number; expansions: Expansion[]; incomplete: boolean; initial?: ExpansionLookup }>();
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState<{ id?: number; initial?: ExpansionLookup; count: number }>();
  const requested = attempt?.id === bggId && attempt?.initial === initial;
  useEffect(() => {
    if (!bggId || initial && !requested) { setLoading(false); setFailed(false); return; }
    const controller = new AbortController();
    setLoading(true); setFailed(false);
    void api<BggDetails>(`/bgg/game?input=${bggId}&mode=base`, { signal: controller.signal })
      .then(details => {
        if (controller.signal.aborted) return;
        if (details.game.bggId !== bggId || details.game.itemType === "Expansion") throw new Error("Выберите базовую игру.");
        setResult({ id: bggId, expansions: details.expansions, incomplete: !!details.expansionLookupIncomplete, initial });
      })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [bggId, initial, attempt, requested]);
  const current = result?.id === bggId && result?.initial === initial ? result : undefined;
  const expansions = mergeExpansions(saved, current?.expansions ?? []);
  const retry = () => setAttempt(value => ({ id: bggId, initial, count: (value?.count ?? 0) + 1 }));
  const incomplete = current ? current.incomplete : initial?.status === "incomplete" || initial?.status === "failed";
  const notice = loading ? <p role="status">Загружаем дополнения BGG…</p>
    : failed || incomplete ? <Notice kind="warning">Не все данные дополнений удалось загрузить. Можно продолжить с доступными дополнениями или без них. <button type="button" onClick={retry}>Повторить загрузку дополнений</button></Notice>
    : (current || initial?.status === "complete") && expansions.length === 0 ? <p>У игры нет дополнений.</p> : null;
  return { expansions, notice };
}
