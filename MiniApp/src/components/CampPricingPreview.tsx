import { useMemo, useState } from "react";
import { api, json } from "../api/client";
import type { CampConfiguration, CampRegistrationQuote } from "../api/types";
import { useAsync, useDebouncedValue } from "../hooks/useAsync";
import { CampPriceInformation, CampQuote, CampRegistrationFields } from "./CampInformation";
import { Field, Notice } from "./Ui";

export function CampPricingPreview({ configuration, timeZoneId }: { configuration: CampConfiguration; timeZoneId: string }) {
  const [days, setDays] = useState(1);
  const [accommodation, setAccommodation] = useState(false);
  const [registrationDate, setRegistrationDate] = useState("");
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const fields = useMemo(() => configuration.registrationFields.filter(field => field.amount > 0 || field.options.some(option => option.amount > 0))
    .map(field => ({ ...field, required: false })), [configuration.registrationFields]);
  const request = JSON.stringify({ configuration: { version: 1, pricing: configuration.pricing, registrationFields: fields },
    days, needsAccommodation: accommodation, answers: Object.fromEntries(Object.entries(answers).filter(([id]) => fields.some(field => field.id === id))),
    timeZoneId, registrationDate: registrationDate || null });
  const debounced = useDebouncedValue(request, 250);
  const preview = useAsync(async () => ({ request: debounced,
    ...await api<{ quote?: CampRegistrationQuote }>("/admin/camps/pricing-preview", json("POST", JSON.parse(debounced))) }), [debounced]);
  const current = request === debounced && preview.data?.request === request;
  const pricing = configuration.pricing!;
  return <div className="camp-price-preview">
    <h3>Участники увидят</h3><CampPriceInformation price={pricing} />
    <h3>Проверьте итоговую сумму</h3>
    <p className="muted">Выберите пример регистрации: цена участия + жильё, если нужно + доплаты за ответы. Для дневных тарифов сумма умножается на число выбранных дней.</p>
    <Field label="Дней участия в примере"><select value={days} onChange={event => setDays(Number(event.target.value))}>
      {Array.from({ length: 31 }, (_, i) => <option key={i + 1} value={i + 1}>{i + 1}</option>)}
    </select></Field>
    {Boolean(pricing.accommodationAmount) && <label className="check"><input type="checkbox" checked={accommodation} onChange={event => setAccommodation(event.target.checked)} />Нужно жильё в примере</label>}
    {pricing.earlyAmount != null && <Field label="Дата регистрации в примере" hint="Оставьте пустым для расчёта на сегодня."><input type="date" value={registrationDate} onChange={event => setRegistrationDate(event.target.value)} /></Field>}
    <CampRegistrationFields fields={fields} answers={answers} onChange={setAnswers} validated={false} currency={pricing.currency} />
    {request !== debounced || preview.loading ? <p className="muted" role="status">Рассчитываем пример…</p>
      : preview.error ? <Notice kind="warning">{preview.error} <button type="button" className="ghost" onClick={() => void preview.reload()}>Повторить расчёт</button></Notice>
      : current && preview.data?.quote && <CampQuote quote={preview.data.quote} title="Пример итоговой суммы" />}
  </div>;
}
