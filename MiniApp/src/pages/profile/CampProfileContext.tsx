import { createContext, useContext, type ReactNode } from "react";
import { api } from "../../api/client";
import type { Community } from "../../api/types";
import { useAsync } from "../../hooks/useAsync";
import { ErrorState, Loading, Notice } from "../../components/Ui";
import { useCampWishlistAction } from "../../hooks/useCampWishlistAction";
import { useRefresh } from "../../hooks/useRefreshOnActivity";

export type CampProfileSettings = { canAct: boolean; shareCollection: boolean; shareWishes: boolean; myDates: string[]; declinedGameIds: number[]; suggestedGameIds: number[] };
type State = ReturnType<typeof useAsync<CampProfileSettings>>;
const Context = createContext<State | undefined>(undefined);
export const useCampProfile = () => useContext(Context);

export function CampProfileProvider({ community, children }: { community: Community; children: ReactNode }) {
  const state = useAsync(() => api<CampProfileSettings>(`/camp-wishlist?community=${encodeURIComponent(community.key)}&view=settings`), [community.key]);
  useRefresh(state.reload);
  return <Context.Provider value={state}>{children}</Context.Provider>;
}

export function CampPrivacy({ community }: { community: Community }) {
  const state = useCampProfile();
  const action = useCampWishlistAction(community.key);
  const data = state?.data;
  return <section className="camp-privacy" id="camp-privacy" aria-label="Видимость в этом кэмпе">
    <h2>Видимость в этом кэмпе</h2><p className="muted">Только для участников «{community.name}».</p>
    {!data && state?.loading && <Loading />}
    {state?.error && <ErrorState message={state.error} retry={state.reload} />}
    {data && <>{([
      ["shareCollection", "Скрыть мою коллекцию"],
      ["shareWishes", "Скрыть авторство моих хотелок"]
    ] as const).map(([field, label]) => <label className="check" key={field}><input type="checkbox" checked={!data[field]} disabled={!data.canAct || action.busy || state?.loading} onChange={async event => {
      if (await action.act({ action: "privacy", [field]: !event.target.checked })) state?.reload();
    }} />{label}</label>)}<p className="muted">Предложенные и обещанные коробки остаются видимыми. Скрытые хотелки учитываются без имени и не показываются в вашем профиле. Имя в сборах и списке участников остаётся видимым.</p></>}
    {action.busy && <p role="status">Сохраняем…</p>}{action.error && <Notice kind="danger">{action.error}</Notice>}
  </section>;
}
