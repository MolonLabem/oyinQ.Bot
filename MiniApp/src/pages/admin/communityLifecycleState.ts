export type CommunityKind = "clubs" | "camps";

export const canCancelCamp = (status: string) => status === "Draft" || status === "Active";
export const canDeleteCommunity = (isSuperAdmin: boolean) => isSuperAdmin;

export function deletionConfirmation(kind: CommunityKind, name: string) {
  const label = kind === "clubs" ? "клуб" : "кэмп";
  return `Удалить ${label} «${name}» из OyinQ?\n\nПривязка к Telegram-группе и настройки будут отключены, будущие сборы — отменены. Регистрации, коллекции и история сохранятся. Отменить это действие нельзя.`;
}

export function cancellationConfirmation(name: string) {
  return `Отменить проведение кэмпа «${name}»?\n\nБудущие сборы будут отменены, а новые регистрации и изменения станут недоступны. Регистрации, коллекции и история сохранятся. Возобновить кэмп после отмены нельзя.`;
}

export function campDateValidation(start: string, end: string) {
  return {
    start: start ? undefined : "Укажите дату и время начала.",
    end: !end ? "Укажите дату и время окончания." : start && end <= start ? "Окончание должно быть позже начала." : undefined,
  };
}
