import type { Community, ProfileGathering } from "../../api/types";
import { CommunityAvatar } from "../../components/CommunityAvatar";

export const profileScheduleEmptyText = "Вы пока не записаны ни на один предстоящий сбор.";

export function ProfileScheduleList({ items, communities, open }: {
  items: ProfileGathering[];
  communities: Community[];
  open: (communityKey: string, gatheringId: string) => void;
}) {
  return <div className="stack">{items.map(item => {
    const itemCommunity = communities.find(value => value.key === item.communityKey)
      ?? { key: item.communityKey, name: item.communityName, mode: item.communityMode, timeZoneId: "UTC" };
    return <button className="card profile-schedule-item" key={item.publicId} onClick={() => open(item.communityKey, item.publicId)}><CommunityAvatar community={itemCommunity} /><span><strong>{item.communityName} — {item.gameName}</strong><small>{item.localDateTime}</small></span>{item.isOrganizer && <span className="badge accent">Организатор</span>}</button>;
  })}</div>;
}
