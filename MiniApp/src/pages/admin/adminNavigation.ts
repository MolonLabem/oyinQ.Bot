import type { AdminCamp, AdminClub, AdminOverview, Community, CommunityMode } from "../../api/types";
import { campStatusLabel } from "../../app/format";

export const adminCommunityStorageKey = "oyinq-admin-community";
export type AdminSection = "community" | "settings" | "release" | "gatherings" | "administrators" | "export" | "collection" | "participants";
export type AdminCommunityOption = { id: string; communityKey: string; name: string; mode: CommunityMode; label: string; club?: AdminClub; camp?: AdminCamp };

export function adminCommunityOptions(overview: AdminOverview): AdminCommunityOption[] {
  return [
    ...overview.clubs.map(club => ({ id: club.communityKey, communityKey: club.communityKey, name: club.name, mode: "Club" as const, club,
      label: `${club.name} · Клуб${club.isActive ? "" : " · Архив"}` })),
    ...overview.camps.map(camp => ({ id: camp.communityKey, communityKey: camp.communityKey, name: camp.name, mode: "Camp" as const, camp,
      label: `${camp.name} · Кэмп${camp.status === "Active" ? "" : ` · ${campStatusLabel(camp.status)}`}` })),
  ];
}

export function resolveAdminCommunity(options: AdminCommunityOption[], storedKey: string) {
  return options.find(item => item.communityKey === storedKey) ?? options[0];
}

export function adminSectionForCommunity(section: AdminSection, community?: AdminCommunityOption): AdminSection {
  if (section === "release" || section === "export") return section;
  if (!community || (section === "collection" && !community.club) || (section === "participants" && !community.camp)) return "community";
  return section;
}

export function adminGatheringCommunity(selected?: AdminCommunityOption): Community | undefined {
  const club = selected?.club;
  if (club?.isActive) return { key: club.communityKey, name: club.name, mode: "Club", timeZoneId: club.timeZoneId };
  const camp = selected?.camp;
  if (camp?.status === "Active") return { key: camp.communityKey, name: camp.name, mode: "Camp", timeZoneId: camp.timeZoneId,
    startsAtUtc: camp.startsAtUtc, endsAtUtc: camp.endsAtUtc, startDate: camp.startDate, endDate: camp.endDate };
}
