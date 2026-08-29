const dateFormatter = new Intl.DateTimeFormat("ru-RU", { day: "numeric", month: "long", year: "numeric" });

export function formatDate(value?: string) {
  if (!value) return "Дата не указана";
  const [year, month, day] = value.split("-").map(Number);
  return dateFormatter.format(new Date(year, month - 1, day));
}

export function plural(value: number, one: string, few: string, many: string) {
  const mod100 = value % 100; const mod10 = value % 10;
  const word = mod100 >= 11 && mod100 <= 19 ? many : mod10 === 1 ? one : mod10 >= 2 && mod10 <= 4 ? few : many;
  return `${value} ${word}`;
}

export function campStatusLabel(status: string) {
  return ({ Draft: "Черновик", Active: "Активен", Closed: "Закрыт", Cancelled: "Отменён" } as Record<string, string>)[status] ?? "Неизвестный статус";
}
