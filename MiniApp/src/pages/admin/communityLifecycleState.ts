export type CommunityKind = "clubs" | "camps";

export const canCancelCamp = (status: string) => status === "Draft" || status === "Active";
export const canDeleteCommunity = (isSuperAdmin: boolean) => isSuperAdmin;

export function deletionConfirmation(kind: CommunityKind, name: string) {
  const label = kind === "clubs" ? "клуб" : "кэмп";
  return `Удалить ${label} «${name}»?\n\nЭто удалит ${label} из OyinQ и отключит его настройки. Будущие сборы будут отменены. Действие нельзя отменить.`;
}
