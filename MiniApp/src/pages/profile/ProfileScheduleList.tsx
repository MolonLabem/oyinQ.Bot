import type { Community, ProfileGathering } from "../../api/types";
import { CommunityAvatar } from "../../components/CommunityAvatar";

export const profileScheduleEmptyText = "Вы пока не записаны ни на один предстоящий сбор.";

export function ProfileScheduleList({ items, communities, open }: {
  items: ProfileGathering[];
  communities: Community[];
  open: (communityKey: string, gatheringId: string) => void;
}) {
  const ordered = [...items].sort((left, right) => Date.parse(left.startsAtUtc) - Date.parse(right.startsAtUtc));
  const days = ordered.reduce<{ date: string; items: ProfileGathering[] }[]>((groups, item) => {
    const current = groups.find(group => group.date === item.localDate);
    if (current) current.items.push(item);
    else groups.push({ date: item.localDate, items: [item] });
    return groups;
  }, []);
  return <div className="schedule-agenda">{days.map(day => <section className="schedule-day" key={day.date}>
    <header><time dateTime={day.date}>{formatScheduleDay(day.date)}</time><span>{day.items.length}</span></header>
    <div className="stack">{day.items.map(item => {
      const itemCommunity = communities.find(value => value.key === item.communityKey)
        ?? { key: item.communityKey, name: item.communityName, mode: item.communityMode, timeZoneId: "UTC" };
      return <button className="card profile-schedule-item" key={item.publicId} onClick={() => open(item.communityKey, item.publicId)}><time className="schedule-time" dateTime={item.localTime}>{item.localTime}</time><CommunityAvatar community={itemCommunity} /><span><strong>{item.gameName}</strong><small>{item.communityName}</small></span>{item.isOrganizer && <span className="badge accent">Организатор</span>}</button>;
    })}</div>
  </section>)}</div>;
}

function formatScheduleDay(value: string): string {
  const [year, month, day] = value.split("-").map(Number);
  return new Intl.DateTimeFormat("ru-RU", { weekday: "short", day: "numeric", month: "long" })
    .format(new Date(year, month - 1, day));
}
