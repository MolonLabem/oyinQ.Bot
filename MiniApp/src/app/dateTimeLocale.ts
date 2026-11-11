// App presentation is day-first, even when a Telegram WebView reports en-US.
// ISO transport values and community timezones are independent of this policy.
export function dateTimeFormatter(options: Intl.DateTimeFormatOptions) {
  return new Intl.DateTimeFormat("ru-RU", { ...options, calendar: "gregory", numberingSystem: "latn", hourCycle: "h23" });
}
