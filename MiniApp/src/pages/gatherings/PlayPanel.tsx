import { useEffect, useState } from "react";
import { api, json } from "../../api/client";
import type { Community, Expansion } from "../../api/types";
import { currentLocalMinute } from "../../app/format";
import { Card, ErrorState, Field, Loading, Notice } from "../../components/Ui";
import { useAsync } from "../../hooks/useAsync";

type PlayState = { revision: number; wasPlayed?: boolean; endedAtUtc?: string; durationMinutes?: number; canEdit: boolean; canShare: boolean; references: { id: number; url: string; author: string; canRemove: boolean }[]; players: { id: string; name: string }[]; selectedPlayerIds?: string[]; expansions: Expansion[]; selectedExpansionIds?: number[] };
type PlayExport = { bgStatsUrl: string };

export function PlayPanel({ community, id }: { community: Community; id: string }) {
  const base = `/gatherings/${id}/play`; const query = `?community=${encodeURIComponent(community.key)}`;
  const state = useAsync(() => api<PlayState>(base + query), [id, community.key]);
  const [played, setPlayed] = useState<boolean>(); const [players, setPlayers] = useState<string[]>([]); const [expansions, setExpansions] = useState<number[]>([]);
  const [end, setEnd] = useState(""); const [duration, setDuration] = useState(""); const [external, setExternal] = useState("");
  const [busy, setBusy] = useState(false); const [error, setError] = useState<string>(); const [exported, setExported] = useState<PlayExport>(); const [copied, setCopied] = useState(false);
  useEffect(() => { const p = state.data; if (!p) return; setPlayed(p.wasPlayed ?? undefined); setPlayers(p.selectedPlayerIds ?? p.players.map(x => x.id)); setExpansions(p.selectedExpansionIds ?? p.expansions.map(x => x.bggId)); setEnd(currentLocalMinute(community.timeZoneId, p.endedAtUtc ? new Date(p.endedAtUtc) : new Date())); setDuration(p.durationMinutes?.toString() ?? ""); setExternal(""); setExported(undefined); }, [state.data, community.timeZoneId]);
  async function save() { if (!state.data || played === undefined) return; setBusy(true); setError(undefined); try { await api(base, json("PUT", { communityKey: community.key, wasPlayed: played, endedAtLocal: end, durationMinutes: duration ? Number(duration) : null, playerIds: players, expansionIds: expansions,  expectedRevision: state.data.revision })); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function prepareExport() { setBusy(true); setError(undefined); try { setExported(await api<PlayExport>(base + "/export" + query)); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function share() { if (!exported) return; try { if (navigator.share) await navigator.share({ title: "Партия OyinQ", url: exported.bgStatsUrl }); else { await navigator.clipboard.writeText(exported.bgStatsUrl); setCopied(true); } } catch (e) { if (!(e instanceof DOMException && e.name === "AbortError")) setError("Не удалось поделиться. Скопируйте ссылку из поля ниже."); } }
  async function addReference() { setBusy(true); setError(undefined); try { await api(base + "/references", json("POST", { communityKey: community.key, url: external })); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  async function removeReference(referenceId: number) { setBusy(true); setError(undefined); try { await api(base + `/references/${referenceId}` + query, { method: "DELETE" }); state.reload(); } catch (e) { setError(e instanceof Error ? e.message : String(e)); } finally { setBusy(false); } }
  if (state.loading) return <Loading />;
  if (state.error || !state.data) return <ErrorState message={state.error ?? "Запись недоступна"} retry={state.reload} />;
  return <Card className="form-grid play-record-form"><h2>Игра состоялась?</h2>
    <p>Подтвердите факт партии и фактический состав. Это не меняет отметки посещаемости сбора.</p>
    {state.data.canEdit ? <><div className="choice-row"><button aria-pressed={played === true} className={played === true ? "active" : ""} onClick={() => setPlayed(true)}>Да, сыграли</button><button aria-pressed={played === false} className={played === false ? "active" : ""} onClick={() => setPlayed(false)}>Нет, не состоялась</button></div>
    {played && <><Field label={`Окончание партии (${community.timeZoneId})`}><input type="datetime-local" value={end} onChange={e => setEnd(e.target.value)} /></Field><Field label="Продолжительность, минут (необязательно)"><input type="number" min="1" max="10080" value={duration} onChange={e => setDuration(e.target.value)} /></Field>
      <fieldset><legend>Кто действительно играл</legend>{state.data.players.map(p => <label className="check" key={p.id}><input type="checkbox" checked={players.includes(p.id)} onChange={() => setPlayers(old => old.includes(p.id) ? old.filter(x => x !== p.id) : [...old, p.id])} />{p.name}</label>)}</fieldset>
      {state.data.expansions.length > 0 && <fieldset><legend>С какими дополнениями</legend>{state.data.expansions.map(p => <label className="check" key={p.bggId}><input type="checkbox" checked={expansions.includes(p.bggId)} onChange={() => setExpansions(old => old.includes(p.bggId) ? old.filter(x => x !== p.bggId) : [...old, p.bggId])} />{p.name}</label>)}</fieldset>}
    </>}
    </> : <Notice>{state.data.wasPlayed === true ? "Партия подтверждена организатором." : state.data.wasPlayed === false ? "Сбор не состоялся." : "Ожидаем подтверждения организатора."}</Notice>}
    {(state.data.canShare || state.data.references.length > 0) && <section><h3>BG Stats</h3>{state.data.references.map(r => <div key={r.id}><p>{r.author}</p><a href={r.url} target="_blank" rel="noreferrer">Открыть запись</a>{r.canRemove && <button disabled={busy} onClick={() => removeReference(r.id)}>Удалить ссылку</button>}</div>)}
      {state.data.canShare && <><Field label="Ваша ссылка BG Stats" hint="HTTPS-ссылка на app.bgstatsapp.com. Её увидят фактические игроки."><input type="url" maxLength={2048} value={external} onChange={e => setExternal(e.target.value)} placeholder="https://app.bgstatsapp.com/…" /></Field><button disabled={busy || !external.trim()} onClick={addReference}>Добавить ссылку BG Stats</button></>}</section>}
    {error && <Notice kind="danger">{error}</Notice>}
    {state.data.canEdit && <button className="primary" disabled={busy || played === undefined} onClick={save}>Сохранить запись</button>}
    {state.data.wasPlayed && state.data.canShare && <><button disabled={busy} onClick={prepareExport}>Подготовить ссылку BG Stats</button><small>Ссылка содержит имена выбранных игроков. Получатель ссылки сможет их прочитать.</small></>}
    {exported && <><a className="button" href={exported.bgStatsUrl} target="_blank" rel="noreferrer">Добавить в BG Stats</a><p>BG Stats откроет подтверждение импорта. Установка приложения необязательна для работы OyinQ. Включённая автопубликация BG Stats может отправить партию в BGG.</p><button onClick={share}>Поделиться ссылкой</button>{copied && <Notice>Ссылка скопирована</Notice>}<input aria-label="Ссылка на импорт партии" value={exported.bgStatsUrl} readOnly onFocus={e => e.target.select()} /></>}
  </Card>;
}
