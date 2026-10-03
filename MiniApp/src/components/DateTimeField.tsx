import { useId } from "react";

export function DateTimeField({ label, value, onChange, min, max, hint, error, required = false, disabled = false }: {
  label: string; value: string; onChange: (value: string) => void; min?: string; max?: string;
  hint?: string; error?: string; required?: boolean; disabled?: boolean;
}) {
  const id = useId();
  const date = value.slice(0, 10), hour = value.slice(11, 13), minute = value.slice(14, 16);
  const valid = (candidate: string) => (!min || candidate >= min) && (!max || candidate <= max);
  const times = Array.from({ length: 60 }, (_, index) => String(index).padStart(2, "0"));
  function changeDate(next: string) {
    if (!next) { onChange(""); return; }
    let time = hour ? `${hour}:${minute}` : "12:00";
    if (next === min?.slice(0, 10) && `${next}T${time}` < min) time = min.slice(11);
    if (next === max?.slice(0, 10) && `${next}T${time}` > max) time = max.slice(11);
    onChange(`${next}T${time}`);
  }
  return <fieldset className={`field datetime-field${error ? " invalid" : ""}`} disabled={disabled} aria-describedby={hint || error ? `${id}-help` : undefined}>
    <legend>{label}</legend>
    <div className="datetime-controls">
      <input aria-label={`${label}: дата`} type="date" min={min?.slice(0, 10)} max={max?.slice(0, 10)}
        required={required} aria-invalid={Boolean(error)} value={date} onChange={event => changeDate(event.target.value)} />
      <div className="time-controls" role="group" aria-label={`${label}: время (24 часа)`}>
        <select aria-label={`${label}: часы`} disabled={!date} value={hour} onChange={event => {
          const next = event.target.value;
          const minutes = times.filter(part => valid(`${date}T${next}:${part}`));
          onChange(`${date}T${next}:${minutes.includes(minute) ? minute : minutes[0]}`);
        }}>
          {!date && <option value="">—</option>}
          {times.slice(0, 24).map(part => <option key={part} value={part} disabled={Boolean(date) && !times.some(m => valid(`${date}T${part}:${m}`))}>{part}</option>)}
        </select><span aria-hidden>:</span>
        <select aria-label={`${label}: минуты`} disabled={!date} value={minute} onChange={event => onChange(`${date}T${hour}:${event.target.value}`)}>
          {!date && <option value="">—</option>}
          {times.map(part => <option key={part} value={part} disabled={Boolean(date) && !valid(`${date}T${hour}:${part}`)}>{part}</option>)}
        </select>
      </div>
    </div>
    {(hint || error) && <div id={`${id}-help`}>{hint && <small>{hint}</small>}{error && <small className="field-error" role="alert">{error}</small>}</div>}
  </fieldset>;
}
