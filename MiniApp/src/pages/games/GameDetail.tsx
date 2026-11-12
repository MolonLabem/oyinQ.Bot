import { useRef } from "react";
import { ApiError, api } from "../../api/client";
import type { Community, GameDetails } from "../../api/types";
import { formatDate } from "../../app/format";
import { collectionMissingMessage } from "../../app/collectionNavigation";
import { GameDescription, GameOverview } from "../../components/GameOverview";
import { BackButton, ContactLink, ErrorState, Loading, Notice, Page } from "../../components/Ui";
import { WishButton } from "../../components/WishButton";
import { useAsync } from "../../hooks/useAsync";
import { useBackButton } from "../../hooks/useBackButton";
import { GameGatherings } from "./GameGatherings";

export function GameDetail({ community, bggId, attendanceDate, back, createGathering, openGathering, openCampWishlist, openPerson }: { createGathering?: (id: number) => void; openGathering?: (id: string) => void; community: Community; bggId: number; attendanceDate?: string; back: () => void; openCampWishlist?: () => void; openPerson?: (id: string) => void }) {
  const state = useAsync(() => api<GameDetails>(`/catalog/${bggId}?community=${encodeURIComponent(community.key)}${attendanceDate ? `&attendanceDate=${encodeURIComponent(attendanceDate)}` : ""}`), [community.key, bggId, attendanceDate]);
  const gatherings = useRef<HTMLDivElement>(null);
  useBackButton(true, back);
  if (state.loading) return <Page title="Игра"><Loading /></Page>;
  if (state.failure instanceof ApiError && state.failure.code === "game_not_in_collection") return <Page title="Игра" subtitle={community.name} actions={<BackButton onClick={back} />}><Notice kind="warning">{collectionMissingMessage(community)}</Notice></Page>;
  if (state.error || !state.data) return <Page title="Игра" actions={<BackButton onClick={back} />}><ErrorState message={state.error ?? "Игра не найдена"} retry={state.reload} /></Page>;
  const game = state.data;
  const hasGatherings = Boolean(openGathering && game.scheduledGatherings && game.scheduledGatherings > 0);
  const gatheringList = openGathering && <div ref={gatherings} tabIndex={-1} role="region" aria-label="Сборы по этой игре"><GameGatherings communityKey={community.key} bggId={bggId} open={openGathering} preserveScroll /></div>;
  return <div className="game-detail-screen"><Page actions={<BackButton onClick={back} />}>
    <GameOverview game={game}>
      <div className="game-overview-actions">
        {hasGatherings && <button className="primary" onClick={() => { gatherings.current?.scrollIntoView({ block: "start" }); gatherings.current?.focus({ preventScroll: true }); }}>Посмотреть сборы · {game.scheduledGatherings}</button>}
        {game.canWish !== false && createGathering && <button className={hasGatherings ? "ghost" : "primary"} onClick={() => createGathering(bggId)}>Создать сбор по этой игре</button>}
        {game.canWish !== false && <WishButton key={bggId} communityKey={community.key} bggId={bggId} initial={game.isWished} />}
      </div>
    </GameOverview>
    {hasGatherings && gatheringList}
    {community.mode === "Camp" && game.canWish !== false && <button onClick={openCampWishlist}>Кто хочет сыграть · Попросить привезти</button>}
    <section className="content-section detail-section">{community.mode === "Club" ? <p className={`availability${game.availability.isInBaseCollection || game.availability.isOwned ? " success" : ""}`}>Коробка · {game.availability.isInBaseCollection ? "Есть в клубе" : "Нет в коллекции клуба"}{game.availability.isOwned && " · Есть у вас"}</p> : <><h2>Кто привезёт игру{attendanceDate ? ` · ${formatDate(attendanceDate)}` : ""}</h2>{game.availability.isOwned && <Notice kind="success">Есть у вас</Notice>}{game.availability.isInBaseCollection && <Notice kind="success">✓ Есть в коллекции клуба</Notice>}{game.availability.providers.length > 0 && <><h3>Кто может привезти</h3><ul className="provider-list">{game.availability.providers.map(provider => <li key={provider.participantId}><span>{provider.commitment === "Bringing" ? "✓ " : ""}<>{provider.publicId && openPerson ? <button className="person-link" onClick={() => openPerson(provider.publicId!)}>{provider.displayName}</button> : <ContactLink url={provider.contactUrl}>{provider.displayName}</ContactLink>}</>{provider.city ? ` (${provider.city})` : ""}</span><span className={`provider-status${provider.commitment === "Bringing" ? " success" : ""}`}>{provider.commitment === "Bringing" ? "точно привезёт" : "может привезти"}{provider.availableDates?.length ? ` · ${provider.availableDates.map(formatDate).join(", ")}` : ""}</span></li>)}</ul></>}{!game.availability.isInBaseCollection && !game.availability.hasCommittedProvider && game.availability.providers.length === 0 && <Notice kind="warning">Пока никто не подтвердил коробку. Сбор всё равно можно создать.</Notice>}{!game.availability.isInBaseCollection && !game.availability.hasCommittedProvider && game.availability.providers.length > 1 && <Notice kind="warning">Нужно решить, кто привезёт игру.</Notice>}</>}</section>
    <section className="content-section detail-section game-activity"><h2>Активность</h2><div><span><strong>{game.scheduledGatherings ?? 0}</strong><small>запланировано сборов</small></span><span><strong>{game.recordedPlays ?? 0}</strong><small>подтверждено партий</small></span></div></section>
    {!hasGatherings && gatheringList}
    <GameDescription game={game} />
    {game.expansions.length > 0 && <details className="content-section detail-section detail-disclosure"><summary>Дополнения в коллекции ({game.expansions.length})</summary><ul>{game.expansions.map(item => <li key={item.bggId}>{item.name}</li>)}</ul></details>}
  </Page></div>;
}
