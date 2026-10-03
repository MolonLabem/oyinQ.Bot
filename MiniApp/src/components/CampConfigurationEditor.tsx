import type { CampConfiguration, CampRegistrationField } from "../api/types";
import { useEffect, useRef, useState } from "react";
import { CampPricingPreview } from "./CampPricingPreview";
import { Field, FormSection, Notice } from "./Ui";

export const emptyCampConfiguration = (): CampConfiguration => ({ version: 1, registrationFields: [] });
const id = () => crypto.randomUUID();
const newField = (type: CampRegistrationField["type"]): CampRegistrationField => ({
  id: id(), label: "", type, required: false, options: type === "Choice" ? [{ id: id(), label: "", amount: 0 }, { id: id(), label: "", amount: 0 }] : [], amount: 0, perDay: false
});
function Amount({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return <Field label={label}><input type="number" min="0" max="10000000" step="0.01" required value={Number.isFinite(value) ? value : ""} onChange={e => onChange(e.target.valueAsNumber)} /></Field>;
}
export function CampConfigurationEditor({ value, onChange, fieldsLocked = false, disabled = false, timeZoneId = "UTC" }: {
  value: CampConfiguration; onChange: (value: CampConfiguration) => void; fieldsLocked?: boolean; disabled?: boolean; timeZoneId?: string;
}) {
  const fields = value.registrationFields;
  const [lockedIds] = useState(() => new Set(fieldsLocked ? fields.map(field => field.id) : []));
  const editor = useRef<HTMLFieldSetElement>(null);
  const [focusQuestion, setFocusQuestion] = useState<string>();
  useEffect(() => {
    if (!focusQuestion) return;
    const question = editor.current?.querySelector<HTMLElement>(`[data-question-id="${focusQuestion}"]`);
    question?.querySelector<HTMLInputElement>("input")?.focus();
    question?.scrollIntoView?.({ block: "nearest", behavior: "smooth" });
  }, [focusQuestion]);
  const [openQuestions, setOpenQuestions] = useState<Record<string, boolean>>({});
  const update = (patch: Partial<CampConfiguration>) => onChange({ ...value, ...patch });
  const changeField = (index: number, patch: Partial<CampRegistrationField>) => update({ registrationFields: fields.map((field, i) => i === index ? { ...field, ...patch } : field) });
  const pricing = value.pricing;
  return <fieldset ref={editor} className="camp-configuration" disabled={disabled}>
    <FormSection title="О кэмпе" hint="Участники увидят эту информацию перед регистрацией и смогут вернуться к ней позже.">
      <Field label="Описание" hint="Программа, правила, что взять с собой. Переносы строк сохранятся."><textarea rows={6} maxLength={6000} value={value.description ?? ""} onChange={e => update({ description: e.target.value })} placeholder="Расскажите, что ждёт участников" /></Field>
      <Field label="Место проведения"><input maxLength={200} value={value.locationName ?? ""} onChange={e => update({ locationName: e.target.value })} /></Field>
      <Field label="Ссылка на место" hint="Ссылка на карту или сайт, начиная с https://"><input type="url" maxLength={1000} value={value.locationUrl ?? ""} onChange={e => update({ locationUrl: e.target.value })} /></Field>
      <Field label="Инструкции по оплате" hint="Реквизиты и комментарий к переводу. Приложение не принимает платежи."><textarea maxLength={2000} value={value.paymentInstructions ?? ""} onChange={e => update({ paymentInstructions: e.target.value })} /></Field>
    </FormSection>
    <FormSection title="Стоимость" hint="Настройте тарифы и проверьте, какую сумму увидит участник.">
      <Field label="Расчёт стоимости"><select value={pricing ? "choices" : "none"}
        disabled={fields.some(field => lockedIds.has(field.id) && (field.amount > 0 || field.options.some(option => option.amount > 0)))}
        onChange={e => update({ pricing: e.target.value === "choices" ? { currency: "KZT", amount: 0, perDay: false } : null,
          registrationFields: e.target.value === "choices" ? fields : fields.map(field => lockedIds.has(field.id) ? field : ({ ...field, amount: 0, perDay: false, options: field.options.map(option => ({ ...option, amount: 0 })) })) })}>
        <option value="none">Без расчёта стоимости</option><option value="choices">По выбору участника</option>
      </select></Field>
      {!pricing && <p className="muted">Участник увидит описание и инструкции по оплате. Итоговая сумма не рассчитывается.</p>}
      {pricing && <>
        <div className="date-range"><Amount label="Цена участия" value={pricing.amount} onChange={amount => update({ pricing: { ...pricing, amount } })} />
          <Field label="Валюта"><select value={pricing.currency} onChange={e => update({ pricing: { ...pricing, currency: e.target.value } })}>{["KZT", "RUB", "USD", "EUR", "KGS", "UZS"].map(currency => <option key={currency}>{currency}</option>)}</select></Field></div>
        <Field label="Цена участия указана"><select value={pricing.perDay ? "day" : "camp"} onChange={e => update({ pricing: { ...pricing, perDay: e.target.value === "day" } })}><option value="camp">За весь кэмп</option><option value="day">За каждый выбранный день</option></select></Field>
        <div className="date-range"><Amount label="Доплата за жильё" value={pricing.accommodationAmount ?? 0} onChange={accommodationAmount => update({ pricing: { ...pricing, accommodationAmount } })} />
          <Field label="Доплата за жильё указана" hint="0 — без доплаты. Применяется при выборе «Нужно жильё»."><select value={pricing.accommodationPerDay ? "day" : "camp"} onChange={e => update({ pricing: { ...pricing, accommodationPerDay: e.target.value === "day" } })}><option value="camp">За весь кэмп</option><option value="day">За каждый выбранный день</option></select></Field></div>
        <div className="date-range"><Field label="Ранняя цена участия" hint="Необязательно. Оставьте пустым, если ранней цены нет."><input type="number" min="0" max="10000000" step="0.01" value={pricing.earlyAmount == null ? "" : Number.isFinite(pricing.earlyAmount) ? pricing.earlyAmount : ""} onChange={e => update({ pricing: { ...pricing, earlyAmount: e.target.value === "" ? null : e.target.valueAsNumber, earlyUntil: e.target.value === "" ? null : pricing.earlyUntil } })} /></Field>
          <Field label="Последний день ранней цены" hint="Включительно, по часовому поясу кэмпа. Цена фиксируется при регистрации."><input type="date" disabled={pricing.earlyAmount == null} required={pricing.earlyAmount != null} value={pricing.earlyUntil ?? ""} onChange={e => update({ pricing: { ...pricing, earlyUntil: e.target.value || null } })} /></Field></div>
        <CampPricingPreview configuration={value} timeZoneId={timeZoneId} />
        <p className="muted">Сохранённая сумма меняется при изменении дней, жилья или ответов; изменение имени или города сохраняет прежнюю сумму.</p>
      </>}
    </FormSection>
    <FormSection title="Вопросы при регистрации">
      {fieldsLocked && <Notice>Можно добавлять необязательные вопросы. Существующие вопросы, варианты и доплаты закреплены, чтобы сохранить ответы участников.</Notice>}
      <fieldset className="camp-question-list">
        {fields.map((field, index) => <details className="camp-question" key={field.id} open={openQuestions[field.id] ?? false} onToggle={event => { const open = event.currentTarget.open; setOpenQuestions(current => current[field.id] === open ? current : { ...current, [field.id]: open }); }}>
          <summary>Вопрос {index + 1} · {field.label || "Новый вопрос"}</summary><fieldset data-question-id={field.id} disabled={lockedIds.has(field.id)} aria-label={`Вопрос ${index + 1}`}>
          <div className="row"><h3>Вопрос {index + 1}</h3><button type="button" className="ghost danger" onClick={() => update({ registrationFields: fields.filter((_, i) => i !== index) })}>Удалить вопрос {index + 1}</button></div>
          <Field label={`Название вопроса ${index + 1}`}><input required maxLength={120} value={field.label} onChange={e => changeField(index, { label: e.target.value })} /></Field>
          <Field label={`Тип вопроса ${index + 1}`}><select value={field.type} onChange={e => { const type = e.target.value as CampRegistrationField["type"]; changeField(index, { type, options: type === "Choice" ? newField(type).options : [], amount: 0, perDay: false }); }}><option value="Text">Короткий текст</option><option value="Multiline">Длинный текст</option><option value="Choice">Один вариант из списка</option><option value="Checkbox">Флажок</option></select></Field>
          <Field label={`Подсказка к вопросу ${index + 1}`}><input maxLength={400} value={field.hint ?? ""} onChange={e => changeField(index, { hint: e.target.value })} /></Field>
          <label className="check"><input type="checkbox" checked={field.required} disabled={fieldsLocked} onChange={e => changeField(index, { required: e.target.checked })} />{field.type === "Checkbox" ? "Обязательно поставить отметку" : "Обязательный ответ"}</label>
          {field.type === "Choice" && <div className="camp-options">{field.options.map((option, optionIndex) => <div className="camp-option" key={option.id}>
            <Field label={`Вариант ${optionIndex + 1} вопроса ${index + 1}`}><input required maxLength={120} value={option.label} onChange={e => changeField(index, { options: field.options.map((item, i) => i === optionIndex ? { ...item, label: e.target.value } : item) })} /></Field>
            {pricing && <Amount label={`Доплата за вариант ${optionIndex + 1} вопроса ${index + 1}`} value={option.amount} onChange={amount => changeField(index, { options: field.options.map((item, i) => i === optionIndex ? { ...item, amount } : item) })} />}
            <button type="button" className="ghost" disabled={field.options.length <= 2} aria-label={`Удалить вариант ${optionIndex + 1} вопроса ${index + 1}`} onClick={() => changeField(index, { options: field.options.filter((_, i) => i !== optionIndex) })}>×</button>
          </div>)}<button type="button" disabled={field.options.length >= 32} onClick={() => changeField(index, { options: [...field.options, { id: id(), label: "", amount: 0 }] })}>Добавить вариант</button></div>}
          {pricing && field.type === "Checkbox" && <Amount label={`Доплата за отметку в вопросе ${index + 1}`} value={field.amount} onChange={amount => changeField(index, { amount })} />}
          {pricing && (field.type === "Choice" || field.type === "Checkbox") && <label className="check"><input type="checkbox" checked={field.perDay} onChange={e => changeField(index, { perDay: e.target.checked })} />Доплата за каждый выбранный день</label>}
        </fieldset></details>)}
        {!fields.length && <p className="muted">Дополнительных вопросов пока нет.</p>}
        <div className="row"><button type="button" disabled={fields.length >= 20} onClick={() => { const field = newField("Text"); setOpenQuestions(current => ({ ...current, [field.id]: true })); setFocusQuestion(field.id); update({ registrationFields: [...fields, field] }); }}>Добавить вопрос</button></div>
      </fieldset>
    </FormSection>
  </fieldset>;
}
