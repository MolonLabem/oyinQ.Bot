import type { GatheringListItem } from "../api/types";
import { gatheringStatusTone } from "../app/semanticTones";
import { GameFact } from "./GameFact";
import { GameCard } from "./GameCard";

export function GatheringCard({ item, time, open }: { item: GatheringListItem; time?: string | null; open: (id: string) => void }) {
  const { card } = item;
  return <article className="gathering-card-shell">
    <GameCard game={{ ...card, name: card.gameName }} context="gathering" open={() => open(card.publicId)}
      players={<strong>{card.occupiedSeats} из {card.maximumPlayers}</strong>} secondary={<>
          <span className="gathering-card-time"><GameFact kind="time" label="Начало"><time dateTime={item.startsAtUtc}>{time ?? card.localDateTime}</time></GameFact></span>
          {item.isOrganizer ? <span className="gathering-own">Ваш сбор</span>
            : item.currentUserStatus === "Confirmed" ? <span className="gathering-own">Вы записаны</span>
            : item.currentUserStatus === "Waitlisted" ? <span className="gathering-own">В очереди{item.waitlistPosition != null ? ` · №${item.waitlistPosition}` : ""}</span>
            : null}
      </>}>
        {item.status !== "Recruiting" && item.status !== "Ready" && <span className={`gathering-card-status ${gatheringStatusTone(item.status)}`}>{card.statusText}</span>}
        {card.recruitment?.belowDesired && item.status !== "Closed" && <span className="gathering-card-activity">{card.recruitment.text}</span>}
        {card.rulesText && <span className="gathering-card-meta"><GameFact kind="rules" label="Правила">{card.rulesText}</GameFact></span>}
        {card.cancellationReason && <span className="gathering-card-cancellation">Причина: {card.cancellationReason}</span>}
        {card.boxWarning && <span className="gathering-box-warning"><GameFact kind="box" label="Коробка">{card.boxWarning}</GameFact></span>}
    </GameCard>
  </article>;
}
