import { DateTimeField } from "./DateTimeField";

export function CampDateRange({ start, end, timeZoneId, errors, onChange }: {
  start: string;
  end: string;
  timeZoneId: string;
  errors: { start?: string; end?: string };
  onChange: (start: string, end: string) => void;
}) {
  return <div className="date-range">
    <DateTimeField label="Начало кэмпа" hint={`Местное время (${timeZoneId}) · 24 часа`} error={errors.start}
      value={start} required onChange={value => onChange(value, end)} />
    <DateTimeField label="Окончание кэмпа" hint={`Местное время (${timeZoneId}) · 24 часа`} error={errors.end}
      min={start || undefined} value={end} required onChange={value => onChange(start, value)} />
  </div>;
}
