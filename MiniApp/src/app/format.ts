const dateFormatter = new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long", year: "numeric" });
const localInputFormatter = new Intl.DateTimeFormat("ru-RU", {
  weekday: "short", day: "numeric", month: "long", year: "numeric",
  hour: "2-digit", minute: "2-digit", timeZone: "UTC"
});

export function formatDate(value?: string) {
  if (!value) return "Дата не указана";
  const [year, month, day] = value.split("-").map(Number);
  return dateFormatter.format(new Date(year, month - 1, day));
}

export function currentLocalMinute(timeZoneId: string, now = new Date()) {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: timeZoneId,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23"
  }).formatToParts(now);
  const value = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find(part => part.type === type)?.value ?? "";
  return `${value("year")}-${value("month")}-${value("day")}T${value("hour")}:${value("minute")}`;
}

export function isFutureLocalDateTime(value: string, timeZoneId: string, now = new Date()) {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value)
    && value > currentLocalMinute(timeZoneId, now);
}

export function formatLocalDateTimeInput(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) return value;
  const [, year, month, day, hour, minute] = match;
  return localInputFormatter.format(new Date(Date.UTC(+year, +month - 1, +day, +hour, +minute)));
}

export function formatInstant(value: string, timeZoneId: string) {
  return new Intl.DateTimeFormat("ru-RU", {
    weekday: "short", day: "numeric", month: "long", year: "numeric",
    hour: "2-digit", minute: "2-digit", timeZone: timeZoneId
  }).format(new Date(value));
}

export function plural(value: number, one: string, few: string, many: string) {
  const mod100 = value % 100; const mod10 = value % 10;
  const word = mod100 >= 11 && mod100 <= 19 ? many : mod10 === 1 ? one : mod10 >= 2 && mod10 <= 4 ? few : many;
  return `${value} ${word}`;
}

export function campStatusLabel(status: string) {
  return ({ Draft: "Черновик", Active: "Активен", Closed: "Закрыт", Cancelled: "Отменён" } as Record<string, string>)[status] ?? "Неизвестный статус";
}
