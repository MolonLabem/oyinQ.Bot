import { useState, type InputHTMLAttributes } from "react";
import { systemDateTimeLocale } from "../app/dateTimeLocale";

type Props = Omit<InputHTMLAttributes<HTMLInputElement>, "type" | "lang" | "value" | "onChange" | "min" | "max"> & {
  value: string; onChange: (value: string) => void; min?: string; max?: string;
};

function fallbackDate(value: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) ? value.split("-").reverse().join(".") : "";
}
function parseFallbackDate(value: string) {
  const match = /^(\d{2})[./ ](\d{2})[./ ](\d{4})$/.exec(value);
  if (!match) return;
  const date = `${match[3]}-${match[2]}-${match[1]}`;
  const parsed = new Date(`${date}T12:00:00Z`);
  if (Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === date) return date;
}

// Native pickers use the browser/device's regional format; values stay YYYY-MM-DD.
export function DateInput({ value, onChange, min, max, ...props }: Props) {
  const locale = systemDateTimeLocale();
  const [native] = useState(() => {
    if (typeof document === "undefined") return false;
    const input = document.createElement("input"); input.type = "date"; return input.type === "date";
  });
  const [draft, setDraft] = useState(() => ({ value, text: fallbackDate(value) }));
  if (draft.value !== value) setDraft({ value, text: fallbackDate(value) });
  if (native && locale) return <input {...props} data-date-input data-date-value={value} type="date" lang={locale}
    value={value} min={min} max={max} ref={input => input?.setCustomValidity("")} onChange={event => onChange(event.target.value)} />;
  return <input {...props} data-date-input data-date-value={value} type="text" lang="ru-RU" inputMode="numeric"
    placeholder="ДД.ММ.ГГГГ" value={draft.text} ref={input => {
      const date = parseFallbackDate(draft.text);
      input?.setCustomValidity(!draft.text ? "" : !date ? "Укажите дату в формате ДД.ММ.ГГГГ."
        : min && date < min || max && date > max ? "Дата вне допустимого диапазона." : "");
    }} onChange={event => {
      const text = event.target.value;
      const date = parseFallbackDate(text);
      const inRange = date && (!min || date >= min) && (!max || date <= max);
      const next = inRange ? date : "";
      setDraft({ value: next, text });
      onChange(next);
    }} />;
}
