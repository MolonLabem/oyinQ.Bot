import type { CampConfiguration, CampRegistrationField } from "../api/types";
import { useState } from "react";
import { Field, FormSection, Notice } from "./Ui";

export const emptyCampConfiguration = (): CampConfiguration => ({ version: 1, registrationFields: [] });
const id = () => crypto.randomUUID();
const newField = (type: CampRegistrationField["type"]): CampRegistrationField => ({
  id: id(), label: "", type, required: false, options: type === "Choice" ? [{ id: id(), label: "", amount: 0 }, { id: id(), label: "", amount: 0 }] : [], amount: 0, perDay: false
});
export function halloweenFields(): CampRegistrationField[] {
  return [
    { ...newField("Choice"), label: "Во сколько планируете приехать?", options: Array.from({ length: 24 }, (_, hour) => ({ id: `hour-${hour}`, label: `${String(hour).padStart(2, "0")}:00`, amount: 0 })), hint: "Укажите местное время приезда." },
    { ...newField("Choice"), label: "Будете в костюме?", options: ["Да 🎃", "Нет", "Пока не знаю"].map(label => ({ id: id(), label, amount: 0 })) },
    { ...newField("Multiline"), label: "Комментарий организатору" }
  ];
}
function Amount({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return <Field label={label}><input type="number" min="0" max="10000000" step="0.01" required value={Number.isFinite(value) ? value : ""} onChange={e => onChange(e.target.valueAsNumber)} /></Field>;
}
export function CampConfigurationEditor({ value, onChange, fieldsLocked = false, disabled = false }: {
  value: CampConfiguration; onChange: (value: CampConfiguration) => void; fieldsLocked?: boolean; disabled?: boolean;
}) {
  const fields = value.registrationFields;
  const [openQuestions, setOpenQuestions] = useState<Record<string, boolean>>({});
  const update = (patch: Partial<CampConfiguration>) => onChange({ ...value, ...patch });
  const changeField = (index: number, patch: Partial<CampRegistrationField>) => update({ registrationFields: fields.map((field, i) => i === index ? { ...field, ...patch } : field) });
  const pricing = value.pricing;
  return <fieldset className="camp-configuration" disabled={disabled}>
    <FormSection title="О кэмпе" hint="Участники увидят эту информацию перед регистрацией и смогут вернуться к ней позже.">
      <Field label="Описание" hint="Программа, правила, что взять с собой. Переносы строк сохранятся."><textarea rows={6} maxLength={6000} value={value.description ?? ""} onChange={e => update({ description: e.target.value })} placeholder="Расскажите, что ждёт участников" /></Field>
      <Field label="Место проведения"><input maxLength={200} value={value.locationName ?? ""} onChange={e => update({ locationName: e.target.value })} /></Field>
      <Field label="Ссылка на место" hint="Ссылка на карту или сайт, начиная с https://"><input type="url" maxLength={1000} value={value.locationUrl ?? ""} onChange={e => update({ locationUrl: e.target.value })} /></Field>
      <Field label="Инструкции по оплате" hint="Реквизиты и комментарий к переводу. Приложение не принимает платежи."><textarea maxLength={2000} value={value.paymentInstructions ?? ""} onChange={e => update({ paymentInstructions: e.target.value })} /></Field>
    </FormSection>
    <FormSection title="Стоимость" hint="Итог рассчитывается из цены участия, выбранных дней и платных вариантов ответа.">
      <label className="check"><input type="checkbox" checked={Boolean(pricing)} disabled={fieldsLocked && fields.some(field => field.amount > 0 || field.options.some(option => option.amount > 0))} onChange={e => update({ pricing: e.target.checked ? { currency: "KZT", amount: 0, perDay: false } : null, registrationFields: fieldsLocked || e.target.checked ? fields : fields.map(field => ({ ...field, amount: 0, perDay: false, options: field.options.map(option => ({ ...option, amount: 0 })) })) })} />Рассчитывать стоимость участия</label>
      {pricing && <>
        <div className="date-range"><Amount label="Цена участия" value={pricing.amount} onChange={amount => update({ pricing: { ...pricing, amount } })} />
          <Field label="Валюта"><select value={pricing.currency} onChange={e => update({ pricing: { ...pricing, currency: e.target.value } })}>{["KZT", "RUB", "USD", "EUR", "KGS", "UZS"].map(currency => <option key={currency}>{currency}</option>)}</select></Field></div>
        <label className="check"><input type="checkbox" checked={pricing.perDay} onChange={e => update({ pricing: { ...pricing, perDay: e.target.checked } })} />Цена за каждый выбранный день</label>
        <Amount label="Доплата за жильё" value={pricing.accommodationAmount ?? 0} onChange={accommodationAmount => update({ pricing: { ...pricing, accommodationAmount } })} />
        <label className="check"><input type="checkbox" checked={pricing.accommodationPerDay ?? false} onChange={e => update({ pricing: { ...pricing, accommodationPerDay: e.target.checked } })} />Доплата за жильё за каждый выбранный день</label>
        <label className="check"><input type="checkbox" checked={pricing.earlyAmount != null} onChange={e => update({ pricing: { ...pricing, earlyAmount: e.target.checked ? pricing.amount : null, earlyUntil: null } })} />Ранняя цена</label>
        {pricing.earlyAmount != null && <div className="date-range"><Amount label="Ранняя цена участия" value={pricing.earlyAmount} onChange={earlyAmount => update({ pricing: { ...pricing, earlyAmount } })} />
          <Field label="Последний день ранней цены" hint="Включительно, по часовому поясу кэмпа. Цена фиксируется при регистрации."><input type="date" required value={pricing.earlyUntil ?? ""} onChange={e => update({ pricing: { ...pricing, earlyUntil: e.target.value || null } })} /></Field></div>}
        <p className="muted">Расчёт не подтверждает оплату. Сохранённая сумма меняется при изменении дней, жилья или ответов; изменение имени или города сохраняет прежнюю сумму.</p>
      </>}
    </FormSection>
    <FormSection title="Вопросы при регистрации" hint="Имя, город, дни участия и необходимость жилья уже есть в форме. Игры и хотелки участники заполняют в своих разделах.">
      {fieldsLocked && <Notice>После первой регистрации вопросы, варианты и доплаты закреплены, чтобы сохранить ответы участников. Описание и базовую стоимость можно обновлять.</Notice>}
      <fieldset className="camp-question-list" disabled={fieldsLocked}>
        {fields.map((field, index) => <details className="camp-question" key={field.id} open={openQuestions[field.id] ?? false} onToggle={event => { const open = event.currentTarget.open; setOpenQuestions(current => current[field.id] === open ? current : { ...current, [field.id]: open }); }}>
          <summary>Вопрос {index + 1} · {field.label || "Новый вопрос"}</summary><section aria-label={`Вопрос ${index + 1}`}>
          <div className="row"><h3>Вопрос {index + 1}</h3><button type="button" className="ghost danger" onClick={() => update({ registrationFields: fields.filter((_, i) => i !== index) })}>Удалить вопрос {index + 1}</button></div>
          <Field label={`Название вопроса ${index + 1}`}><input required maxLength={120} value={field.label} onChange={e => changeField(index, { label: e.target.value })} /></Field>
          <Field label={`Тип вопроса ${index + 1}`}><select value={field.type} onChange={e => { const type = e.target.value as CampRegistrationField["type"]; changeField(index, { type, options: type === "Choice" ? newField(type).options : [], amount: 0, perDay: false }); }}><option value="Text">Короткий текст</option><option value="Multiline">Длинный текст</option><option value="Choice">Один вариант из списка</option><option value="Checkbox">Флажок</option></select></Field>
          <Field label={`Подсказка к вопросу ${index + 1}`}><input maxLength={400} value={field.hint ?? ""} onChange={e => changeField(index, { hint: e.target.value })} /></Field>
          <label className="check"><input type="checkbox" checked={field.required} onChange={e => changeField(index, { required: e.target.checked })} />{field.type === "Checkbox" ? "Обязательно поставить отметку" : "Обязательный ответ"}</label>
          {field.type === "Choice" && <div className="camp-options">{field.options.map((option, optionIndex) => <div className="camp-option" key={option.id}>
            <Field label={`Вариант ${optionIndex + 1} вопроса ${index + 1}`}><input required maxLength={120} value={option.label} onChange={e => changeField(index, { options: field.options.map((item, i) => i === optionIndex ? { ...item, label: e.target.value } : item) })} /></Field>
            {pricing && <Amount label={`Доплата за вариант ${optionIndex + 1} вопроса ${index + 1}`} value={option.amount} onChange={amount => changeField(index, { options: field.options.map((item, i) => i === optionIndex ? { ...item, amount } : item) })} />}
            <button type="button" className="ghost" disabled={field.options.length <= 2} aria-label={`Удалить вариант ${optionIndex + 1} вопроса ${index + 1}`} onClick={() => changeField(index, { options: field.options.filter((_, i) => i !== optionIndex) })}>×</button>
          </div>)}<button type="button" disabled={field.options.length >= 32} onClick={() => changeField(index, { options: [...field.options, { id: id(), label: "", amount: 0 }] })}>Добавить вариант</button></div>}
          {pricing && field.type === "Checkbox" && <Amount label={`Доплата за отметку в вопросе ${index + 1}`} value={field.amount} onChange={amount => changeField(index, { amount })} />}
          {pricing && (field.type === "Choice" || field.type === "Checkbox") && <label className="check"><input type="checkbox" checked={field.perDay} onChange={e => changeField(index, { perDay: e.target.checked })} />Доплата за каждый выбранный день</label>}
        </section></details>)}
        {!fields.length && <p className="muted">Дополнительных вопросов пока нет.</p>}
        <div className="row"><button type="button" disabled={fields.length >= 20} onClick={() => { const field = newField("Text"); setOpenQuestions(current => ({ ...current, [field.id]: true })); update({ registrationFields: [...fields, field] }); }}>Добавить вопрос</button>
          <button type="button" disabled={fields.length > 17} onClick={() => update({ registrationFields: [...fields, ...halloweenFields()] })}>Вопросы для Хэллоуина</button></div>
      </fieldset>
    </FormSection>
  </fieldset>;
}
