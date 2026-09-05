import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import type { Community, Expansion } from "../../api/types";
import { currentLocalMinute } from "../../app/format";
import { Card, ErrorState, Field, Loading, Notice } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";

type PlayPlayer = { id: string; name: string; score?: number; isWinner: boolean };
type PlayState = { revision: number; wasPlayed?: boolean; endedAtUtc?: string; durationMinutes?: number;
  location?: string; higherScoreWins: boolean; canEdit: boolean; canShare: boolean;
  references: { id: number; url: string; author: string; canRemove: boolean }[];
  players: PlayPlayer[]; selectedPlayerIds?: string[]; expansions: Expansion[]; selectedExpansionIds?: number[] };
type PlayExport = { bgStatsUrl: string };

export function PlayPanel({ community, id }: { community: Community; id: string }) {
  const base = `/gatherings/${id}/play`; const query = `?community=${encodeURIComponent(community.key)}`;
  const state = useAsync(() => api<PlayState>(base + query), [id, community.key]);
  const [played, setPlayed] = useState<boolean>(); const [players, setPlayers] = useState<string[]>([]); const [expansions, setExpansions] = useState<number[]>([]);
  const [scores, setScores] = useState<Record<string, string>>({}); const [winners, setWinners] = useState<string[]>([]); const [higherScoreWins, setHigherScoreWins] = useState(true);
  const [end, setEnd] = useState(""); const [duration, setDuration] = useState(""); const [location, setLocation] = useState(""); const [external, setExternal] = useState("");
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [exported, setExported] = useState<PlayExport>(); const [copied, setCopied] = useState(false);
  const [endError, setEndError] = useState<string>();
  useEffect(() => {
    const p = state.data; if (!p) return;
    const selected = p.selectedPlayerIds ?? p.players.map(x => x.id);
    setPlayed(p.wasPlayed ?? undefined); setPlayers(selected); setExpansions(p.selectedExpansionIds ?? p.expansions.map(x => x.bggId));
    setScores(Object.fromEntries(selected.map(playerId => [playerId, String(p.players.find(x => x.id === playerId)?.score ?? 0)])));
    setWinners(p.players.filter(x => selected.includes(x.id) && x.isWinner).map(x => x.id)); setHigherScoreWins(p.higherScoreWins ?? true);
    setEnd(currentLocalMinute(community.timeZoneId, p.endedAtUtc ? new Date(p.endedAtUtc) : new Date())); setDuration(p.durationMinutes?.toString() ?? "");
    setLocation(p.location?.trim() || community.name); setExternal(""); setExported(undefined); setCopied(false);
  }, [state.data, community.name, community.timeZoneId]);
  function togglePlayer(playerId: string) {
    const selected = players.includes(playerId);
    setPlayers(old => selected ? old.filter(x => x !== playerId) : [...old, playerId]);
    if (selected) {
      setWinners(old => old.filter(x => x !== playerId));
      setScores(old => { const next = { ...old }; delete next[playerId]; return next; });
    } else setScores(old => ({ ...old, [playerId]: "0" }));
  }
  async function save() {
    if (busy || !state.data || played === undefined) return;
    if (played && !end) { setEndError("Укажите дату и время окончания партии."); return; }
    setEndError(undefined); setBusy(true); setError(undefined);
    try {
      await api(base, json("PUT", { communityKey: community.key, wasPlayed: played, endedAtLocal: end,
        durationMinutes: duration ? Number(duration) : null, location: location.trim(),
        playerResults: players.map(playerId => ({ playerId, score: Number(scores[playerId] || 0), isWinner: winners.includes(playerId) })),
        expansionIds: expansions, expectedRevision: state.data.revision, higherScoreWins }));
      telegram.success("Запись о партии сохранена"); state.reload();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); }
  }
  async function prepareExport() { if (busy) return; setBusy(true); setError(undefined); try { setExported(await api<PlayExport>(base + "/export" + query)); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function share() { if (!exported) return; try { if (navigator.share) await navigator.share({ title: "Партия OyinQ", url: exported.bgStatsUrl }); else { await navigator.clipboard.writeText(exported.bgStatsUrl); setCopied(true); } } catch (e) { if (!(e instanceof DOMException && e.name === "AbortError")) setError("Не удалось поделиться. Скопируйте ссылку из поля ниже."); } }
  async function addReference() { if (busy) return; setBusy(true); setError(undefined); try { await api(base + "/references", json("POST", { communityKey: community.key, url: external })); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function removeReference(referenceId: number) { if (busy) return; setBusy(true); setError(undefined); try { await api(base + `/references/${referenceId}` + query, { method: "DELETE" }); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  if (state.loading) return <Loading />;
  if (state.error || !state.data) return <ErrorState message={state.error ?? "Запись недоступна"} retry={state.reload} />;
  const hasScores = players.length > 0;
  return <Card className="form-grid play-record-form"><h2>Игра состоялась?</h2>
    <p>Подтвердите факт партии и фактический состав. Это не меняет отметки посещаемости сбора.</p>
    {state.data.canEdit ? <><div className="choice-row"><button aria-pressed={played === true} className={played === true ? "active" : ""} onClick={() => setPlayed(true)}>Да, сыграли</button><button aria-pressed={played === false} className={played === false ? "active" : ""} onClick={() => setPlayed(false)}>Нет, не состоялась</button></div>
    {played && <><Field label={`Окончание партии (${community.timeZoneId})`} error={endError}><input type="datetime-local" value={end} onChange={e => { setEnd(e.target.value); setEndError(undefined); }} /></Field><Field label="Продолжительность, минут (необязательно)"><input type="number" min="1" max="10080" value={duration} onChange={e => setDuration(e.target.value)} /></Field>
      <Field label="Где играли"><input type="text" maxLength={160} value={location} onChange={e => setLocation(e.target.value)} placeholder={community.name} /></Field>
      <fieldset className="play-results"><legend>Кто играл и кто победил</legend><small>Можно выбрать несколько победителей. Для совместного поражения не отмечайте никого.</small>
        {state.data.players.map(p => <div className={`play-player-result${players.includes(p.id) ? " selected" : ""}`} key={p.id}>
          <label className="check play-player-name"><input type="checkbox" checked={players.includes(p.id)} onChange={() => togglePlayer(p.id)} />{p.name}</label>
          {players.includes(p.id) && <div className="play-result-fields">
            <label className="check winner-check"><input type="checkbox" checked={winners.includes(p.id)} onChange={() => setWinners(old => old.includes(p.id) ? old.filter(x => x !== p.id) : [...old, p.id])} />Победитель</label>
            <label className="play-score"><span>Счёт</span><input aria-label={`Счёт: ${p.name}`} type="number" step="any" value={scores[p.id] ?? "0"} onChange={e => setScores(old => ({ ...old, [p.id]: e.target.value }))} /></label>
          </div>}
        </div>)}
      </fieldset>
      {hasScores && <Field label="Как сравнивать счёт"><select value={higherScoreWins ? "higher" : "lower"} onChange={e => setHigherScoreWins(e.target.value === "higher")}><option value="higher">Больше — лучше</option><option value="lower">Меньше — лучше</option></select></Field>}
      {state.data.expansions.length > 0 && <fieldset><legend>С какими дополнениями</legend>{state.data.expansions.map(p => <label className="check" key={p.bggId}><input type="checkbox" checked={expansions.includes(p.bggId)} onChange={() => setExpansions(old => old.includes(p.bggId) ? old.filter(x => x !== p.bggId) : [...old, p.bggId])} />{p.name}</label>)}</fieldset>}
    </>}
    </> : <Notice>{state.data.wasPlayed === true ? "Партия подтверждена." : state.data.wasPlayed === false ? "Сбор не состоялся." : "Ожидаем подтверждения организатора или администратора."}</Notice>}
    {error && <Notice kind="danger">{error}</Notice>}
    {state.data.canEdit && <button className="primary" disabled={busy || played === undefined || (played && (players.length === 0 || !location.trim()))} aria-busy={busy} onClick={save}>{busy ? "Сохраняем…" : "Сохранить запись"}</button>}
    {state.data.wasPlayed && state.data.canShare && <section className="bgstats-export"><h3>Добавить в BG Stats</h3>
      {exported ? <><a className="button primary-link bgstats-open-link" href={exported.bgStatsUrl} target="_blank" rel="noreferrer">Открыть в BG Stats ↗</a><p>BG Stats откроет подтверждение импорта. Включённая автопубликация BG Stats может отправить партию в BGG.</p><button onClick={share}>Поделиться ссылкой</button>{copied && <Notice>Ссылка скопирована</Notice>}<input aria-label="Ссылка на импорт партии" value={exported.bgStatsUrl} readOnly onFocus={e => e.target.select()} /></>
        : <><button className="primary" disabled={busy} onClick={prepareExport}>Создать ссылку для BG Stats</button><small>В ссылке будут место, имена игроков, победители и счёт. Получатель сможет прочитать эти данные.</small></>}
    </section>}
    {(state.data.canShare || state.data.references.length > 0) && <section className="bgstats-references"><h3>Ссылки на партию</h3>{state.data.references.map(r => <div className="bgstats-reference" key={r.id}><p>{r.author}</p><a href={r.url} target="_blank" rel="noreferrer">Открыть запись</a>{r.canRemove && <button disabled={busy} onClick={() => removeReference(r.id)}>Удалить ссылку</button>}</div>)}
      {state.data.canShare && <><Field label="Поделиться ссылкой из BG Stats" hint="HTTPS-ссылка на app.bgstatsapp.com. Её увидят фактические игроки."><input type="url" maxLength={2048} value={external} onChange={e => setExternal(e.target.value)} placeholder="https://app.bgstatsapp.com/…" /></Field><button disabled={busy || !external.trim()} onClick={addReference}>Добавить ссылку</button></>}</section>}
  </Card>;
}
