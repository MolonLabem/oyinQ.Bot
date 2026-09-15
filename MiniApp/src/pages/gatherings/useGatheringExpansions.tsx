import { useEffect, useState } from "react";
import { api } from "../../api/client";
import type { BggDetails, Expansion } from "../../api/types";
import { uniqueByBggId } from "../../components/gamePickerModel";
import { Notice } from "../../components/Ui";

// Selection belongs to the form. Refreshing provider options never clears it.
export function useGatheringExpansions(bggId: number | undefined, saved: Expansion[]) {
  const [result, setResult] = useState<{ id: number; expansions: Expansion[]; incomplete: boolean }>();
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (!bggId) return;
    const controller = new AbortController();
    setLoading(true); setFailed(false);
    void api<BggDetails>(`/bgg/game?input=${bggId}&mode=base`, { signal: controller.signal })
      .then(details => {
        if (controller.signal.aborted) return;
        if (details.game.bggId !== bggId || details.game.itemType === "Expansion") throw new Error("Выберите базовую игру.");
        setResult({ id: bggId, expansions: details.expansions, incomplete: !!details.expansionLookupIncomplete });
      })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [bggId, attempt]);
  const current = result?.id === bggId ? result : undefined;
  const expansions = uniqueByBggId([...saved, ...(current?.expansions ?? []).map(item => {
    const previous = saved.find(value => value.bggId === item.bggId);
    return { ...item, minPlayers: item.minPlayers ?? previous?.minPlayers,
      maxPlayers: item.maxPlayers ?? previous?.maxPlayers,
      thumbnailImageUrl: item.thumbnailImageUrl ?? previous?.thumbnailImageUrl,
      imageUrl: item.imageUrl ?? previous?.imageUrl,
      complexityInfo: item.complexityInfo ?? previous?.complexityInfo };
  })]);
  const retry = () => setAttempt(value => value + 1);
  const notice = loading ? <p role="status">Загружаем дополнения BGG…</p>
    : failed || current?.incomplete ? <Notice kind="warning">Не все данные дополнений удалось загрузить. Можно продолжить с доступными дополнениями или без них. <button type="button" onClick={retry}>Повторить загрузку дополнений</button></Notice>
    : current && expansions.length === 0 ? <p>У игры нет дополнений.</p> : null;
  return { expansions, notice };
}
