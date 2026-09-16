import { useEffect, useMemo, useState } from "react";
import { download, json } from "../../api/client";
import type { ClubCollectionState } from "../../api/types";
import { BackButton, Badge, Card, Cover, Empty, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { GameMeta, searchGames } from "../../components/GamePicker";
import { useAsync } from "../../hooks/useAsync";
import { useScreenRequest } from "../../hooks/useScreenRequest";
import { telegram } from "../../telegram/webApp";
import { plural } from "../../app/format";
import { bggImportProgressText, clubImportResultText, type BggImportStage } from "../../app/bggImportProgress";
import { importStatusTone } from "../../app/semanticTones";
import { hasAvailableExpansions, toggleExpansionList } from "./expansionAvailability";
import { ClubGameSheet } from "./ClubGameSheet";

export function ClubCollection({ clubId, bggAvailable, back }: { clubId: number; bggAvailable: boolean; back: () => void }) {
  const api = useScreenRequest();
  type ClubImport = {
    publicId: string;
    bggUsername: string;
    status: string;
    stage?: BggImportStage;
    foundGames: number;
    foundExpansions: number;
    progressCurrent: number;
    progressTotal: number;
    addedGames: number;
    addedExpansions: number;
    orphanExpansions: number;
    error?: string;
  };
  const state = useAsync(() => api<ClubCollectionState>(`/admin/clubs/${clubId}/collection`), [clubId]);
  const [query, setQuery] = useState("");
  const [addingGame, setAddingGame] = useState(false);
  const [expandedGameId, setExpandedGameId] = useState<number>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [refresh, setRefresh] = useState<{
    publicId: string;
    status: string;
    progressCurrent: number;
    progressTotal: number;
    updatedGames?: number;
    error?: string;
  }>();
  const [bggInput, setBggInput] = useState("");
  const [clubImport, setClubImport] = useState<ClubImport>();
  useEffect(() => {
    if (!refresh || !["Queued", "Running"].includes(refresh.status)) return;
    const timer = window.setInterval(
      () =>
        api<typeof refresh>(`/admin/clubs/${clubId}/metadata-refresh/${refresh.publicId}`)
          .then((value) => {
            setRefresh(value);
            if (value.status === "Completed") state.reload();
          })
          .catch((reason) => setError(reason instanceof Error ? reason.message : String(reason))),
      3000,
    );
    return () => window.clearInterval(timer);
  }, [refresh?.publicId, refresh?.status, clubId]);
  useEffect(() => {
    if (!clubImport || !["Queued", "Running"].includes(clubImport.status)) return;
    const timer = window.setInterval(
      () =>
        api<ClubImport>(`/admin/clubs/${clubId}/bgg-imports/${clubImport.publicId}`)
          .then((value) => {
            setClubImport(value);
            if (value.status === "Completed") {
              telegram.success("Коллекция BGG добавлена");
              state.reload();
            }
          })
          .catch((reason) => setError(reason instanceof Error ? reason.message : String(reason))),
      3000,
    );
    return () => window.clearInterval(timer);
  }, [clubImport?.publicId, clubImport?.status, clubId]);
  const games = useMemo(() => searchGames(state.data?.collection.games ?? [], query), [state.data, query]);
  async function remove(id: number, name: string) {
    if (busy || !state.data || !(await telegram.confirm(`Удалить «${name}» из коллекции клуба?\n\nИгра исчезнет из коллекции клуба. В уже созданных сборах сведения о ней останутся.`))) return;
    setBusy(true);
    setError(undefined);
    try {
      await api(`/admin/clubs/${clubId}/games/${id}?expectedRevision=${state.data.revision}`, { method: "DELETE" });
      telegram.success("Игра удалена из коллекции");
      state.reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      state.reload();
    } finally { setBusy(false); }
  }
  async function importFile(file: File) {
    if (busy || !state.data || !(await telegram.confirm(`Полностью заменить текущую коллекцию (${plural(state.data.collection.games.length, "игра", "игры", "игр")}) содержимым файла?\n\nУже созданные сборы сохранят свои снимки игр.`))) return;
    setBusy(true);
    setError(undefined);
    try {
      const document = JSON.parse(await file.text());
      await api(`/admin/clubs/${clubId}/collection`, json("PUT", { expectedRevision: state.data.revision, document }));
      telegram.success("Коллекция восстановлена");
      state.reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally { setBusy(false); }
  }
  async function refreshMetadata() {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      setRefresh(
        await api(`/admin/clubs/${clubId}/metadata-refresh`, {
          method: "POST",
        }),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  async function importBgg() {
    if (busy || !bggInput.trim() || !(await telegram.confirm("Добавить все игры и связанные дополнения из коллекции BGG?\n\nТекущие игры и выбранные дополнения не будут удалены."))) return;
    setBusy(true);
    setError(undefined);
    try {
      setClubImport(await api<ClubImport>(`/admin/clubs/${clubId}/bgg-imports`, json("POST", { bggInput })));
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <Page title="Коллекция" subtitle={state.data ? plural(state.data.collection.games.length, "игра", "игры", "игр") : undefined}
      actions={<BackButton onClick={back} />}>
      {state.data && state.data.canEdit !== false && <button className="primary club-add-action" onClick={() => setAddingGame(true)}>+ Добавить игру</button>}
      {addingGame && state.data && state.data.canEdit !== false && <ClubGameSheet clubId={clubId} collection={state.data} bggAvailable={bggAvailable}
        close={() => setAddingGame(false)} saved={(game, editing) => {
          setAddingGame(false); if (!editing) setQuery(game.name);
          telegram.success(editing ? "Изменения сохранены" : "Игра добавлена в коллекцию"); state.reload();
        }} />}
      {state.loading ? (
        <Loading />
      ) : state.error || !state.data ? (
        <ErrorState message={state.error ?? "Коллекция не найдена"} retry={state.reload} />
      ) : (
        <>

          {state.data.canEdit === false && <Notice>Общая коллекция обновляется автоматически. Для изменений откройте клуб-источник, если у вас есть права на его управление.</Notice>}
          {state.data.canEdit !== false && <>
          {!bggAvailable && <Notice kind="warning">BGG временно недоступен. Просмотр, поиск по коллекции, экспорт и восстановление продолжают работать.</Notice>}
          <details className="club-collection-tools"><summary>Обновление и перенос коллекции</summary>
          <Card className="form-grid">
            <h2>Добавить коллекцию BGG</h2>
            <p className="muted">Одноразово добавляет все принадлежащие пользователю базовые игры и связанные дополнения. Уже сохранённые игры и дополнения не удаляются.</p>
            <Field label="Пользователь BGG" hint="Имя пользователя или ссылка на профиль">
              <input value={bggInput} maxLength={300} onChange={(event) => setBggInput(event.target.value)} placeholder="RollMoveClub" />
            </Field>
            <button disabled={!bggAvailable || busy || !bggInput.trim() || Boolean(clubImport && ["Queued", "Running"].includes(clubImport.status))} onClick={importBgg}>
              {busy ? "Запускаем импорт…" : "Добавить из BGG"}
            </button>
            {clubImport && <Notice kind={clubImport.status === "Failed" ? "danger" : clubImport.status === "Completed" ? "success" : "info"}><Badge tone={importStatusTone(clubImport.status, clubImport.stage)}>{clubImport.status === "Queued" ? "В очереди" : clubImport.status === "Running" ? "Выполняется" : clubImport.status === "Completed" ? "Завершён" : "Ошибка"}</Badge><p>{clubImport.status === "Completed" ? clubImportResultText(clubImport) : clubImport.status === "Failed" ? (clubImport.error ?? "Не удалось импортировать коллекцию BGG.") : bggImportProgressText(clubImport)}</p></Notice>}
          </Card>
          <Card className="form-grid">
            <h2>Данные игр из BGG</h2>
            <p className="muted">Обновит названия, описания, изображения и характеристики игр из BGG. Игры и выбранные дополнения останутся в коллекции. Все данные появятся вместе после завершения.</p>
            <button disabled={!bggAvailable || busy || Boolean(refresh && ["Queued", "Running"].includes(refresh.status))} onClick={refreshMetadata}>
              Обновить данные из BGG
            </button>

          </Card>
          <Card>
            <h2>Экспорт / восстановление</h2>
            <div className="row">
              <button onClick={() => download(`/admin/clubs/${clubId}/collection/export`, `club-${clubId}.json`)}>Скачать JSON</button>
              <label className="button">
                Импорт JSON
                <input hidden type="file" accept="application/json,.json" onChange={(e) => e.target.files?.[0] && importFile(e.target.files[0])} />
              </label>
            </div>
          </Card>
          </details>
          </>}
          {state.data.canEdit === false && <button onClick={() => download(`/admin/clubs/${clubId}/collection/export`, `club-${clubId}.json`)}>Скачать JSON</button>}
          {refresh && <Notice kind={refresh.status === "Failed" ? "danger" : refresh.status === "Completed" ? "success" : "info"}>
            {refresh.status === "Completed" ? refresh.updatedGames === 0 ? "Данные уже актуальны" : <>Данные BGG обновлены{refresh.updatedGames != null && <p>Обновлено: {plural(refresh.updatedGames, "игра", "игры", "игр")}</p>}</>
              : refresh.status === "Failed" ? "Не удалось обновить данные BGG. Коллекция не изменена; повторите позже."
              : `Обновляем данные: ${refresh.progressCurrent} из ${refresh.progressTotal}`}
          </Notice>}
          <Field label="Поиск по коллекции">
            <input type="search" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Название игры" />
          </Field>
          {error && <Notice kind="danger">{error}</Notice>}
          {!games.length && <Empty>{query ? "Ничего не найдено. Попробуйте другое название." : "В коллекции пока нет игр. Нажмите «+ Добавить игру», чтобы начать."}</Empty>}
          <div className="stack">
            {games.map((game) => (
              <Card key={game.bggId}>
                <div className="row collection-row">
                  <div className="media">
                    <Cover src={game.thumbnailImageUrl} name={game.name} />
                    <div>
                      <h3>{game.name}</h3>
                      <GameMeta game={game} />
                    </div>
                  </div>
                  <div className="row">
                    {hasAvailableExpansions(game) && (
                      <button aria-expanded={expandedGameId === game.bggId} onClick={() => setExpandedGameId((current) => toggleExpansionList(current, game))}>
                        Дополнения ({game.expansions.length})
                      </button>
                    )}
                    <button disabled={state.data?.canEdit === false} className="danger ghost" onClick={() => remove(game.bggId, game.name)}>
                      Удалить
                    </button>
                  </div>
                </div>
                {expandedGameId === game.bggId && (
                  <div className="selected-game">
                    <h4>Дополнения в коллекции</h4>
                    <ul>
                      {game.expansions.map((expansion) => (
                        <li key={expansion.bggId}>{expansion.name}</li>
                      ))}
                    </ul>
                  </div>
                )}
              </Card>
            ))}
          </div>
          <details className="club-collection-technical"><summary>Техническая информация</summary>
            <p>Внутренняя версия коллекции: {state.data.revision}</p>
            <p className="muted">Версия опубликованного содержимого. Ход загрузки BGG учитывается отдельно.</p>
            <p className="muted">Обновлено {new Date(state.data.updatedAt).toLocaleString("ru-RU")}</p>
          </details>
        </>
      )}
    </Page>
  );
}
