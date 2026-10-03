import { Field } from "./Ui";

export function CampDateRange({ start, end, timeZoneId, errors, onChange }: {
  start: string;
  end: string;
  timeZoneId: string;
  errors: { start?: string; end?: string };
  onChange: (start: string, end: string) => void;
}) {
  return <div className="date-range">
    <Field label="Начало кэмпа" hint={`Местное время (${timeZoneId})`} error={errors.start}>
      <input type="datetime-local" value={start} required aria-invalid={Boolean(errors.start)}
        onChange={event => onChange(event.target.value, end)} />
    </Field>
    <Field label="Окончание кэмпа" hint={`Местное время (${timeZoneId})`} error={errors.end}>
      <input type="datetime-local" min={start || undefined} value={end} required aria-invalid={Boolean(errors.end)}
        onChange={event => onChange(start, event.target.value)} />
    </Field>
  </div>;
}
