import { api } from "../api/client";
import type { GameProvider } from "../api/types";
import { useAsync } from "../hooks/useAsync";
import { Notice } from "./Ui";

export function GameProviderNotice({ communityKey, bggId, startsAtLocal, ownership }: { communityKey: string; bggId?: number; startsAtLocal?: string; ownership?: { gameName: string; add: boolean; bring: boolean; camp: boolean; setAdd: (value: boolean) => void; setBring: (value: boolean) => void } }) {
  const state = useAsync(() => bggId ? api<GameProvider>(`/catalog/${bggId}/provider?community=${encodeURIComponent(communityKey)}${startsAtLocal ? `&startsAtLocal=${encodeURIComponent(startsAtLocal)}` : ""}`) : Promise.resolve(null), [communityKey, bggId, startsAtLocal]);
  if (!bggId) return null;
  if (state.error) return <Notice kind="warning">Не удалось проверить коробку: {state.error}</Notice>;
  return state.data ? <><Notice kind={state.data.isConfirmed ? "success" : "warning"}>Коробка: {state.data.summary}{state.data.isOwned && <p>Есть у вас</p>}</Notice>
    {ownership && <fieldset><legend>Ваша коробка</legend>{!state.data.isConfirmed && <p>Для этого сбора пока никто не подтвердил коробку. Если приносите свою копию, отметьте её ниже.</p>}
      {<label className="check"><input type="checkbox" checked={ownership.add} onChange={e => ownership.setAdd(e.target.checked)} />{state.data.isOwned ? "Добавить выбранные дополнения в мою коллекцию" : `Добавить «${ownership.gameName}» и выбранные дополнения в мою коллекцию`}</label>}
      {ownership.camp && <label className="check"><input type="checkbox" disabled={!state.data.isOwned && !ownership.add} checked={ownership.bring && (state.data.isOwned || ownership.add)} onChange={e => ownership.setBring(e.target.checked)} />Я привезу игру и выбранные дополнения на этот кэмп</label>}
    </fieldset>}</> : null;
}
