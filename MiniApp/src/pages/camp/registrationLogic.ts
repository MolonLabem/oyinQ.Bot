export function toggleRegistrationDate(selected: readonly string[], date: string): string[] {
  return selected.includes(date)
    ? selected.filter(value => value !== date)
    : [...selected, date].sort();
}

export function registrationSubmitEnabled(city: string, selectedDates: readonly string[], campStatus: string): boolean {
  return city.trim().length > 0 && selectedDates.length > 0 && campStatus === "Active";
}

export function fullscreenLabel(fullscreen: boolean): string {
  return fullscreen ? "Свернуть" : "Развернуть на весь экран";
}
