const fallbackLocale = "ru-RU";

// Browsers expose regional preferences through navigator, not the Telegram UI language.
// Do not fall back to the server/runtime locale, which may be American English.
export function systemDateTimeLocale(): string | undefined {
  const preferences = typeof navigator === "undefined" ? [] : [...(navigator.languages ?? []), navigator.language];
  for (const preference of preferences) {
    if (!preference) continue;
    try {
      const [locale] = Intl.DateTimeFormat.supportedLocalesOf([preference], { localeMatcher: "lookup" });
      if (locale) return locale;
    } catch { /* Ignore malformed preferences and try the next language. */ }
  }
  return undefined;
}

export function dateTimeLocale(): string {
  return systemDateTimeLocale() ?? fallbackLocale;
}

export function dateTimeFormatter(options: Intl.DateTimeFormatOptions) {
  return new Intl.DateTimeFormat(dateTimeLocale(), { ...options, calendar: "gregory" });
}
