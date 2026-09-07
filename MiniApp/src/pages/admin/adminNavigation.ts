import type { AdminOverview, Community, CommunityMode } from "../../api/types";
import { campStatusLabel } from "../../app/format";

export type AdminCommunityOption = { id: string; communityKey?: string; name: string; mode: CommunityMode; label: string };

export function adminCommunityOptions(overview: AdminOverview): AdminCommunityOption[] {
  return [
    ...overview.clubs.map(club => ({ id: club.communityKey, communityKey: club.communityKey, name: club.name, mode: "Club" as const,
      label: `${club.name} · Клуб${club.isActive ? "" : " · Архив"}` })),
    ...overview.camps.map(camp => ({ id: camp.communityKey, communityKey: camp.communityKey, name: camp.name, mode: "Camp" as const,
      label: `${camp.name} · Кэмп${camp.status === "Active" ? "" : ` · ${campStatusLabel(camp.status)}`}` })),
    ...overview.lockedCommunities.map(item => ({ id: item.communityKey ?? `chat:${item.telegramChatId}`, communityKey: item.communityKey,
      name: item.name, mode: item.mode, label: `${item.name} · ${item.communityKey ? "Доступ не выдан" : "Не настроено"}` })),
  ];
}

export function adminGatheringCommunity(overview: AdminOverview, key?: string): Community | undefined {
  const club = overview.clubs.find(item => item.communityKey === key && item.isActive);
  if (club) return { key: club.communityKey, name: club.name, mode: "Club", timeZoneId: club.timeZoneId };
  const camp = overview.camps.find(item => item.communityKey === key && item.status === "Active");
  if (camp) return { key: camp.communityKey, name: camp.name, mode: "Camp", timeZoneId: camp.timeZoneId,
    startsAtUtc: camp.startsAtUtc, endsAtUtc: camp.endsAtUtc, startDate: camp.startDate, endDate: camp.endDate };
}
