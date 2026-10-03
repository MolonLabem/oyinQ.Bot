import type { CampConfiguration, CampRegistrationField, CampRegistrationQuote } from "../api/types";
import { formatDate, formatMoney } from "../app/format";
import { Field, FormSection } from "./Ui";

export function hasCampInformation(config?: CampConfiguration) {
  return Boolean(config && (config.description || config.locationName || config.locationUrl || config.paymentInstructions || config.pricing));
}
export function CampInformation({ config }: { config: CampConfiguration }) {
  const price = config.pricing;
  return <FormSection title="О кэмпе">
    {config.description && <p className="pre-line camp-description">{config.description}</p>}
    {(config.locationName || config.locationUrl) && <p>{config.locationUrl ? <a href={config.locationUrl} target="_blank" rel="noreferrer">{config.locationName || "Место проведения"}</a> : config.locationName}</p>}
    {price && <div><h3>Стоимость участия</h3><p>{formatMoney(price.amount, price.currency)}{price.perDay ? " за день" : " за кэмп"}</p>
      {price.earlyAmount != null && price.earlyUntil && <p>Ранняя цена: {formatMoney(price.earlyAmount, price.currency)}{price.perDay ? " за день" : ""} при регистрации до {formatDate(price.earlyUntil)} включительно.</p>}
      {Boolean(price.accommodationAmount) && <p>Жильё: +{formatMoney(price.accommodationAmount!, price.currency)}{price.accommodationPerDay ? " за день" : ""} при выборе «Нужно жильё».</p>}
      <p className="muted">Итог зависит от выбранных дней и вариантов в регистрации. Расчёт не подтверждает оплату.</p></div>}
    {config.paymentInstructions && <div><h3>Как оплатить</h3><p className="pre-line camp-description">{config.paymentInstructions}</p></div>}
  </FormSection>;
}
export function requiredAnswersComplete(fields: CampRegistrationField[], answers: Record<string, string>) {
  return fields.every(field => !field.required || (field.type === "Checkbox" ? answers[field.id] === "true" : Boolean(answers[field.id]?.trim())));
}
export function CampRegistrationFields({ fields, answers, onChange, validated, currency }: {
  fields: CampRegistrationField[]; answers: Record<string, string>; onChange: (answers: Record<string, string>) => void; validated: boolean; currency?: string;
}) {
  return <div className="camp-registration-questions">{fields.map(field => {
    const value = answers[field.id] ?? "";
    const change = (next: string) => onChange({ ...answers, [field.id]: next });
    const error = validated && !requiredAnswersComplete([field], answers) ? "Заполните обязательный вопрос." : undefined;
    const label = `${field.label}${field.required ? " *" : ""}`;
    if (field.type === "Checkbox") return <div key={field.id}><label className="check"><input type="checkbox" required={field.required} checked={value === "true"} aria-invalid={Boolean(error)} onChange={e => change(String(e.target.checked))} /><span>{label}{currency && field.amount > 0 && <small> +{formatMoney(field.amount, currency)}{field.perDay ? " за день" : ""}</small>}</span></label>{field.hint && <p className="muted">{field.hint}</p>}{error && <small className="field-error" role="alert">{error}</small>}</div>;
    return <Field key={field.id} label={label} hint={field.hint ?? undefined} error={error}>{field.type === "Choice" ? <select required={field.required} value={value} onChange={e => change(e.target.value)}><option value="">Выберите вариант</option>{field.options.map(option => <option key={option.id} value={option.id}>{option.label}{currency && option.amount > 0 ? ` (+${formatMoney(option.amount, currency)}${field.perDay ? " за день" : ""})` : ""}</option>)}</select>
      : field.type === "Multiline" ? <textarea required={field.required} maxLength={2000} value={value} onChange={e => change(e.target.value)} /> : <input required={field.required} maxLength={400} value={value} onChange={e => change(e.target.value)} />}</Field>;
  })}</div>;
}
export function CampQuote({ quote, title = "Расчёт стоимости" }: { quote: CampRegistrationQuote; title?: string }) {
  return <section className="camp-quote" aria-label={title}><h3>{title}</h3><dl>{quote.lines.map((line, index) => <div key={index}><dt>{line.label}{line.quantity > 1 ? ` · ${line.quantity} × ${formatMoney(line.unitAmount, quote.currency)}` : ""}</dt><dd>{formatMoney(line.amount, quote.currency)}</dd></div>)}<div className="camp-quote-total"><dt>Итого</dt><dd>{formatMoney(quote.total, quote.currency)}</dd></div></dl></section>;
}
