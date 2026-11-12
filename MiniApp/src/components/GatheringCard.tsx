import type { GatheringListItem } from "../api/types";
import { formatDuration } from "../app/format";
import { gatheringStatusTone } from "../app/semanticTones";
import { ComplexityBadge } from "./ComplexityBadge";
import { GameFact, GameFacts } from "./GameFact";
import { Cover } from "./Ui";

export function GatheringCard({ item, time, open }: { item: GatheringListItem; time?: string | null; open: (id: string) => void }) {
  const { card } = item;
  return <article className="gathering-card-shell">
    <button className="card gathering-card" onClick={() => open(card.publicId)}>
      <span className="visually-hidden">Открыть сбор: </span>
      <div className="compact-game-header">
        <Cover src={card.imageUrl} name={card.gameName} />
        <div className="gathering-card-body">
          <div className="compact-game-top"><GameFact kind="time" label="Начало"><time dateTime={item.startsAtUtc}>{time ?? card.localDateTime}</time></GameFact><ComplexityBadge info={card.complexityInfo} /></div>
          <h3>{card.gameName}</h3>
          <p className="gathering-card-organizer">{item.isOrganizer ? <span className="gathering-own">Ваш сбор</span> : <>Собирает {card.organizerName}</>}</p>
        </div>
      </div>
      <div className="compact-game-content">
        <GameFacts players={<strong>{card.occupiedSeats} из {card.maximumPlayers}</strong>} playersLabel="Занято мест" duration={formatDuration(card.minPlayTimeMinutes, card.maxPlayTimeMinutes)} />
        {!item.isOrganizer && item.currentUserStatus === "Confirmed" && <p className="gathering-own">Вы записаны</p>}
        {!item.isOrganizer && item.currentUserStatus === "Waitlisted" && <p className="gathering-own">В очереди{item.waitlistPosition != null ? ` · №${item.waitlistPosition}` : ""}</p>}
        {item.status !== "Recruiting" && item.status !== "Ready" && <p className={`gathering-card-status ${gatheringStatusTone(item.status)}`}>{card.statusText}</p>}
        {card.recruitment?.belowDesired && item.status !== "Closed" && <p className="gathering-card-activity">{card.recruitment.text}</p>}
        {card.rulesText && <p className="gathering-card-meta"><GameFact kind="rules" label="Правила">{card.rulesText}</GameFact></p>}
        {card.cancellationReason && <p className="gathering-card-cancellation">Причина: {card.cancellationReason}</p>}
        {card.boxWarning && <p className="gathering-box-warning"><GameFact kind="box" label="Коробка">{card.boxWarning}</GameFact></p>}
      </div>
    </button>
  </article>;
}
