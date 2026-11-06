import { dateTimeFormatter } from "./dateTimeLocale";

export function formatCalendarDate(value: string, options: Intl.DateTimeFormatOptions) {
  return dateTimeFormatter({ ...options, timeZone: "UTC" }).format(new Date(`${value}T12:00:00Z`));
}

export function formatDate(value?: string) {
  if (!value) return "Дата не указана";
  return formatCalendarDate(value, { day: "2-digit", month: "2-digit", year: "numeric" });
}

// Format a community's wall-clock time without interpreting it in the device's zone.
export function formatLocalTime(value: string) {
  return dateTimeFormatter({ hour: "2-digit", minute: "2-digit", timeZone: "UTC" })
    .format(new Date(`2000-01-01T${value}:00Z`));
}

// A datetime-local value is wall time in the selected community, not a browser-zone instant.
export function formatLocalDateTime(value?: string) {
  if (!value) return "Дата не указана";
  const [date, time] = value.split("T");
  return `${formatDate(date)}, ${formatLocalTime(time)}`;
}

export function formatShortDate(value: string) {
  return formatDate(value);
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

export function formatInstant(value: string, timeZoneId: string) {
  return dateTimeFormatter({
    weekday: "short", day: "numeric", month: "long", year: "numeric",
    hour: "2-digit", minute: "2-digit", timeZone: timeZoneId
  }).format(new Date(value));
}

export function formatUpdatedAt(value: string) {
  return dateTimeFormatter({ day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(new Date(value));
}

export function formatTime(value: string) {
  return dateTimeFormatter({ hour: "2-digit", minute: "2-digit" }).format(new Date(value));
}

export function formatMoney(amount: number, currency: string) {
  return new Intl.NumberFormat("ru-RU", { style: "currency", currency, maximumFractionDigits: 2 }).format(amount);
}

export function plural(value: number, one: string, few: string, many: string) {
  const mod100 = value % 100; const mod10 = value % 10;
  const word = mod100 >= 11 && mod100 <= 19 ? many : mod10 === 1 ? one : mod10 >= 2 && mod10 <= 4 ? few : many;
  return `${value} ${word}`;
}

export function campStatusLabel(status: string) {
  return ({ Draft: "Черновик", Active: "Активен", Closed: "Закрыт", Cancelled: "Отменён" } as Record<string, string>)[status] ?? "Неизвестный статус";
}
