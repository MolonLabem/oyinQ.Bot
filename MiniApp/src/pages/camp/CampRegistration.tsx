import { type ReactNode, useId, useLayoutEffect, useRef, useState } from "react";
import { ApiError, api, json } from "../../api/client";
import type { Community, CampConfiguration, CampRegistrationQuote } from "../../api/types";
import { BackButton, Card, ErrorState, Field, Loading, Notice, Page } from "../../components/Ui";
import { CampInformation, CampInformationSummary, CampQuote, CampRegistrationFields, hasCampInformation, requiredAnswersComplete } from "../../components/CampInformation";
import { useAsync, useDebouncedValue } from "../../hooks/useAsync";
import { telegram } from "../../telegram/webApp";
import { formatDate, formatInstant } from "../../app/format";
import { registrationSubmitEnabled, toggleRegistrationDate } from "./registrationLogic";

export type RegistrationState = { shareCollection?: boolean; shareWishes?: boolean; campStatus: string; startsAtUtc?: string; endsAtUtc?: string; startDate?: string; endDate?: string; dateLabels?: Record<string, string>; availableDates: string[]; baseGameIds: number[]; configuration?: CampConfiguration; displayName?: string; registration?: { registered: boolean; daysStaying: number; selectedDates: string[]; suggestedDates: string[]; needsAccommodation: boolean; city?: string; displayName?: string; data?: { answers?: Record<string, string>; quote?: CampRegistrationQuote | null } } };

export function CampRegistrationSettings({ community }: { community: Community }) {
  const registration = useAsync(() => api<RegistrationState>(`/camp/registration?community=${encodeURIComponent(community.key)}`), [community.key]);
  if (registration.loading) return <Loading />;
  if (registration.error || !registration.data) return <ErrorState message={registration.error ?? "Регистрация недоступна"} retry={registration.reload} />;
  return <CampRegistrationForm community={community} state={registration.data} done={registration.reload} />;
}

export function CampRegistrationGate({ community, canOpenAdminPanel, children }: { community: Community; canOpenAdminPanel: boolean; children: ReactNode }) {
  const registration = useAsync(() => api<RegistrationState>(`/camp/registration?community=${encodeURIComponent(community.key)}`), [community.key]);
  if (registration.loading) return <Page as="section" title="Кэмп"><Loading /></Page>;
  if (registration.error) return <Page as="section" title="Кэмп"><ErrorState message={registration.error} retry={registration.reload} /></Page>;
  if (!registration.data?.startDate || !registration.data.endDate) return <Page as="section" title="Кэмп ещё настраивается" subtitle={community.name}><Notice kind="warning">Организатор ещё не указал даты кэмпа. Регистрация и создание сборов станут доступны после настройки.</Notice>{canOpenAdminPanel && <a className="button primary-link" href="?admin=1">Указать даты в админ-панели</a>}</Page>;
  if (!registration.data.registration?.registered) return <CampRegistrationForm community={community} state={registration.data} done={registration.reload} />;
  return <>{hasCampInformation(registration.data.configuration) && <details className="camp-information-disclosure"><summary>О кэмпе · описание и стоимость</summary><CampInformation config={registration.data.configuration!} /></details>}{children}</>;
}

export function CampRegistrationForm({ community, state, done, cancel }: { community: Community; state: RegistrationState; done: () => void; cancel?: () => void }) {
  const [answers, setAnswers] = useState<Record<string, string>>(state.registration?.data?.answers ?? {});
  const fields = state.configuration?.registrationFields ?? [];
  const initialDates = state.registration?.selectedDates?.length ? state.registration.selectedDates : state.registration?.suggestedDates ?? [];
  const [selectedDates, setSelectedDates] = useState<string[]>(initialDates);
  const [housing, setHousing] = useState(state.registration?.needsAccommodation ?? false);
  const [name, setName] = useState(state.registration?.displayName ?? state.displayName ?? "");
  const [city, setCity] = useState(state.registration?.city ?? "");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [validationAttempt, setValidationAttempt] = useState(0);
  const form = useRef<HTMLFormElement>(null);
  const summaryId = useId();
  const validated = validationAttempt > 0;
  const missing = [
    ...(!city.trim() ? ["Город"] : []),
    ...(!selectedDates.length ? ["Дни участия"] : []),
    ...fields.filter(field => !requiredAnswersComplete([field], answers)).map(field => field.label)
  ];
  useLayoutEffect(() => {
    if (!validationAttempt) return;
    const first = form.current?.querySelector<HTMLElement>('[aria-invalid="true"], input:invalid, select:invalid, textarea:invalid');
    first?.scrollIntoView?.({ block: "center", behavior: "auto" });
    first?.focus({ preventScroll: true });
  }, [validationAttempt]);
  const quoteInput = JSON.stringify({ communityKey: community.key, selectedDates, answers, needsAccommodation: housing });
  const debouncedQuoteInput = useDebouncedValue(quoteInput, 250);
  const canQuote = Boolean(state.configuration?.pricing && selectedDates.length && state.campStatus === "Active");
  const preview = useAsync(async () => canQuote ? { input: debouncedQuoteInput, ...await api<{ quote: CampRegistrationQuote | null }>("/camp/registration/quote", { method: "POST", headers: { "Content-Type": "application/json" }, body: debouncedQuoteInput }) } : undefined, [debouncedQuoteInput, canQuote]);
  const quoteCurrent = !preview.loading && preview.data?.input === quoteInput;
  async function save(confirmAttendanceChanges = false) {
    await api("/camp/registration", json("PUT", { communityKey: community.key, selectedDates, needsAccommodation: housing, displayName: name, city, answers, confirmAttendanceChanges }));
  }
  async function submit() {
    if (busy) return;
    if (!registrationSubmitEnabled(city, selectedDates, state.campStatus) || missing.length) {
      setValidationAttempt(value => value + 1);
      return;
    }
    setBusy(true); setError(undefined);
    try { await save(); telegram.success("Регистрация сохранена"); done(); }
    catch (e) {
      if (e instanceof ApiError && e.code === "registration_dates_affect_gatherings" && e.affectedGatherings?.length) {
        const summary = e.affectedGatherings.map(x => `• ${x.gameName}, ${formatInstant(x.startsAtUtc, community.timeZoneId)}`).join("\n");
        if (await telegram.confirm(`Вы больше не сможете участвовать в этих сборах:\n${summary}\n\nПродолжить?`)) {
          try { await save(true); telegram.success("Регистрация обновлена"); done(); }
          catch (retry) { setError(retry instanceof Error ? retry.message : String(retry)); }
        }
      } else setError(e instanceof Error ? e.message : String(e));
    } finally { setBusy(false); }
  }
  return <div className="camp-registration-screen"><Page as="section" title={state.registration?.registered ? "Редактировать регистрацию" : "Регистрация на кэмп"}
    subtitle={`${formatDate(state.startDate)} — ${formatDate(state.endDate)}`} actions={cancel && <BackButton onClick={cancel} />}>
    {hasCampInformation(state.configuration) && <CampInformationSummary config={state.configuration!} />}
    <Card className="camp-registration-card"><form ref={form} className="form-grid camp-registration-form" noValidate
      aria-label="Регистрация на кэмп" aria-describedby={validated && missing.length ? summaryId : undefined}
      onSubmit={event => { event.preventDefault(); void submit(); }}>
      <Field label="Имя" hint="Так вас увидят участники этого кэмпа."><input maxLength={128} value={name} onChange={e => setName(e.target.value)} /></Field>
      <Field label="Город" hint="Его увидят только участники этого кэмпа" error={validated && !city.trim() ? "Укажите город." : undefined}>
        <input required maxLength={100} aria-invalid={validated && !city.trim()} value={city} onChange={e => setCity(e.target.value)} placeholder="Например, Астана" />
      </Field>
      <fieldset className="registration-days" tabIndex={-1} aria-invalid={validated && !selectedDates.length}>
        <legend>Когда вы будете на кэмпе?</legend>
        <p className="muted">В выбранные дни можно создавать сборы и участвовать в них.</p>
        {state.availableDates.map(date => <label className={`check registration-day${selectedDates.includes(date) ? " selected" : ""}`} key={date}>
          <input type="checkbox" checked={selectedDates.includes(date)} onChange={() => setSelectedDates(current => toggleRegistrationDate(current, date))} />
          {new Date(`${date}T00:00:00`).toLocaleDateString("ru-RU", { weekday: "short", day: "numeric", month: "long" })}{state.dateLabels?.[date] ? ` · ${state.dateLabels[date]}` : ""}
        </label>)}
        {validated && !selectedDates.length && <small className="field-error" role="alert">Выберите хотя бы один день.</small>}
      </fieldset>
      <label className="check"><input type="checkbox" checked={housing} onChange={e => setHousing(e.target.checked)} />Нужно жильё</label>
      {fields.length > 0 && <p className="muted">Ответы на дополнительные вопросы и расчёт стоимости видят только вы и администраторы кэмпа.</p>}
      <CampRegistrationFields fields={fields} answers={answers} onChange={setAnswers} validated={validated} currency={state.configuration?.pricing?.currency} />
      {state.registration?.data?.quote && <details className="saved-registration-quote"><summary>Сохранённая стоимость</summary><CampQuote quote={state.registration.data.quote} title="Сохранённая стоимость" /></details>}
      {canQuote && <div aria-live="polite">{preview.error ? <Notice kind="warning">{preview.error}<button type="button" onClick={preview.reload}>Повторить расчёт</button></Notice>
        : !quoteCurrent ? <p className="muted">Обновляем расчёт…</p> : preview.data?.quote && <CampQuote quote={preview.data.quote} title="Стоимость выбранных вариантов" />}
        <p className="muted">Итог рассчитывается при сохранении. Изменение дней, жилья или ответов обновит сумму по текущим тарифам. Оплата выполняется отдельно.</p>
      </div>}
      {error && <Notice kind="danger"><span className="pre-line">{error}</span></Notice>}
      <p className="muted">{state.shareCollection !== false && state.shareWishes !== false ? "Вашу коллекцию и хотелки видят участники этого кэмпа. Это можно изменить в настройках." : "Ваши настройки видимости сохранены. Изменить их можно в настройках профиля."}</p>
      {validated && missing.length > 0 && <div id={summaryId} className="notice danger" role="alert">Заполните: {missing.join(", ")}. Незаполненное поле выделено выше.</div>}
      <button type="submit" className="primary" disabled={busy || state.campStatus !== "Active"} aria-busy={busy}>
        {busy ? "Сохраняем…" : state.registration?.registered ? "Сохранить изменения" : "Зарегистрироваться"}
      </button>
    </form></Card>
  </Page></div>;
}
