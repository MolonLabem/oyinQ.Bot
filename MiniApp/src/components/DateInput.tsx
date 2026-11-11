import { useState, type InputHTMLAttributes } from "react";

type Props = Omit<InputHTMLAttributes<HTMLInputElement>, "type" | "lang" | "value" | "onChange" | "min" | "max"> & {
  value: string; onChange: (value: string) => void; min?: string; max?: string;
};

function displayDate(value: string) {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) ? value.split("-").reverse().join(".") : "";
}
function parseDate(value: string) {
  const match = /^(\d{2})[./ ](\d{2})[./ ](\d{4})$/.exec(value);
  if (!match) return;
  const date = `${match[3]}-${match[2]}-${match[1]}`;
  const parsed = new Date(`${date}T12:00:00Z`);
  if (Number.isFinite(parsed.getTime()) && parsed.toISOString().slice(0, 10) === date) return date;
}

// Native date inputs can ignore lang and display MM/DD/YYYY. Own the visible
// format while keeping the same ISO values, calendar validation and form validity.
export function DateInput({ value, onChange, min, max, ...props }: Props) {
  const [draft, setDraft] = useState(() => ({ value, text: displayDate(value) }));
  if (draft.value !== value) setDraft({ value, text: displayDate(value) });
  return <input {...props} data-date-input data-date-value={value} type="text" lang="ru-RU" inputMode="numeric"
    placeholder="ДД.ММ.ГГГГ" value={draft.text} ref={input => {
      const date = parseDate(draft.text);
      input?.setCustomValidity(!draft.text ? "" : !date ? "Укажите дату в формате ДД.ММ.ГГГГ."
        : min && date < min || max && date > max ? "Дата вне допустимого диапазона." : "");
    }} onChange={event => {
      const entered = event.target.value;
      // Numeric phone keyboards need no punctuation key: 06112026 → 06.11.2026.
      const digits = entered.replaceAll(".", "");
      const text = /^\d{0,8}$/.test(digits)
        ? [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4)].filter(Boolean).join(".") : entered;
      const date = parseDate(text);
      const inRange = date && (!min || date >= min) && (!max || date <= max);
      const next = inRange ? date : "";
      setDraft({ value: next, text });
      onChange(next);
    }} />;
}
