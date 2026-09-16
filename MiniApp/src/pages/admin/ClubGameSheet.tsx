import { useState } from "react";
import { ApiError, json } from "../../api/client";
import type { ClubCollectionState, ClubGame } from "../../api/types";
import { GameMeta, GamePicker } from "../../components/GamePicker";
import { ExpansionPicker } from "../../components/ExpansionPicker";
import { Badge, Cover, Notice } from "../../components/Ui";
import { useScreenRequest } from "../../hooks/useScreenRequest";
import { useMobileDialog } from "../../hooks/useMobileDialog";

export function ClubGameSheet({ clubId, collection, bggAvailable, close, saved }: {
  clubId: number; collection: ClubCollectionState; bggAvailable: boolean;
  close: () => void; saved: (game: ClubGame, editing: boolean) => void;
}) {
  const api = useScreenRequest();
  // Keep the revision that owns this draft even if background work reloads the page.
  const [baseline, setBaseline] = useState(collection);
  const [game, setGame] = useState<ClubGame>();
  const [selected, setSelected] = useState<number[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const [conflict, setConflict] = useState(false);
  const dismiss = () => { if (!busy) close(); };
  const dialog = useMobileDialog(dismiss);
  const existing = baseline.collection.games.find(item => item.bggId === game?.bggId);
  const expansions = [...new Map([...(existing?.expansions ?? []), ...(game?.expansions ?? [])].map(item => [item.bggId, item])).values()];
  const unchanged = !!existing && existing.expansions.length === selected.length
    && existing.expansions.every(item => selected.includes(item.bggId));

  async function save() {
    if (!game || busy || unchanged || conflict) return;
    setBusy(true); setError(undefined);
    try {
      await api(`/admin/clubs/${clubId}/games`, json("POST", {
        expectedRevision: baseline.revision, bggInput: String(game.bggId), expansionBggIds: selected,
      }));
      saved(game, !!existing);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : String(reason));
      setConflict(reason instanceof ApiError && reason.code === "stale_revision");
    } finally { setBusy(false); }
  }
  async function reload() {
    setBusy(true);
    try {
      const current = await api<ClubCollectionState>(`/admin/clubs/${clubId}/collection`);
      if (current.canEdit === false) { close(); return; }
      setBaseline(current); setGame(undefined); setSelected([]); setConflict(false); setError(undefined);
    } catch (reason) { setError(reason instanceof Error ? reason.message : String(reason)); }
    finally { setBusy(false); }
  }
  return <dialog ref={dialog} className="catalog-dialog club-game-dialog" aria-labelledby="club-game-title"
    onCancel={event => { event.preventDefault(); dismiss(); }}
    onClick={event => { if (event.target === event.currentTarget) dismiss(); }}>
    <div className="catalog-dialog-layout">
      <header><h2 id="club-game-title">{existing ? "Игра в коллекции" : "Добавить игру"}</h2><button type="button" disabled={busy} aria-label="Закрыть добавление игры" onClick={close}>×</button></header>
      <div className="catalog-dialog-scroll club-game-content">
        {!bggAvailable && <Notice kind="warning">BGG недоступен. Можно изменить дополнения уже сохранённых игр.</Notice>}
        <fieldset disabled={busy || conflict} className="club-game-fields">
          <GamePicker key={baseline.revision} catalog={baseline.collection.games} bggAvailable={bggAvailable} selected={game}
            onClear={() => { setGame(undefined); setSelected([]); setError(undefined); }}
            onSelect={(value, _source, ids) => {
              const stored = baseline.collection.games.find(item => item.bggId === value.bggId);
              setGame(value); setSelected([...new Set([...(stored?.expansions.map(item => item.bggId) ?? []), ...ids])]);
              setError(undefined);
            }} />
          {game && <div className="selected-game"><div className="media"><Cover src={game.thumbnailImageUrl} name={game.name} /><div><h3>{game.name}</h3>{existing && <Badge tone="accent">Уже в коллекции</Badge>}<GameMeta game={game} /></div></div>
            <ExpansionPicker expansions={expansions} selected={selected} onChange={setSelected} label="Дополнения в коллекции" />
            {unchanged && <p className="muted">Изменений нет. Можно выбрать другой набор дополнений.</p>}
          </div>}
        </fieldset>
        {error && <Notice kind="danger">{error}</Notice>}
        {conflict && <button disabled={busy} onClick={() => void reload()}>Загрузить актуальную коллекцию</button>}
      </div>
      <footer><button type="button" className="primary" disabled={!game || busy || unchanged || conflict} onClick={() => void save()}>
        {busy ? "Сохраняем…" : existing ? "Сохранить изменения" : "Добавить в коллекцию"}
      </button></footer>
    </div>
  </dialog>;
}
