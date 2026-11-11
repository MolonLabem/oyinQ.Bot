import { formatDate } from "../../app/format";
import { DateInput } from "../../components/DateInput";
import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { api } from "../../api/client";
import type { Community, ComplexityInfo } from "../../api/types";
import { availabilityLabels, catalogParams, emptyFilters, maxPublicationYear, minPublicationYear, normalizeFilters, ownershipLabels, planningLabels, showGames, yearFilterError, yearFilterLabel, type Filters, type FilterOptions } from "../../app/catalogFilters";
import { toggleValue } from "../../app/catalogQuery";
import { Field } from "../../components/Ui";
import { BrowseSearch } from "../../components/BrowseSearch";
import { ComplexityBadge } from "../../components/ComplexityBadge";
import { CloseIcon } from "../../components/CloseIcon";
import { useMobileDialog } from "../../hooks/useMobileDialog";

export function FilterSection({ title, summary, children }: { title: string; summary?: string; children: ReactNode }) {
  return <details className="catalog-filter-section"><summary><strong>{title}</strong>{summary && <span>{summary}</span>}<svg className="filter-chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="m6 9 6 6 6-6" /></svg></summary><div className="catalog-section-content">{children}</div></details>;
}
export function FilterChoices<T extends string | number>({ title, values, selected, change }: { title: string; values: { key: T; value: string; complexityInfo?: ComplexityInfo }[]; selected: T[]; change: (value: T) => void }) {
  const [search, setSearch] = useState("");
  const list = useRef<HTMLDivElement>(null);
  const matching = values.filter(x => x.value.toLocaleLowerCase("ru").replaceAll("ё", "е").includes(search.trim().toLocaleLowerCase("ru").replaceAll("ё", "е")));
  return <FilterSection title={title} summary={selected.length ? `Выбрано: ${selected.length}` : undefined}>
    {selected.length > 0 && <div className="filter-selection-chips">{selected.map(key => <button type="button" className="filter-chip" key={key} aria-label={`Убрать: ${values.find(x => x.key === key)?.value}`} onClick={() => change(key)}><span>{values.find(x => x.key === key)?.value}</span><CloseIcon /></button>)}</div>}
    {values.length > 6 && <BrowseSearch label={`Поиск: ${title.toLowerCase()}`} placeholder="Найти вариант" value={search} onChange={value => { setSearch(value); if (list.current) list.current.scrollTop = 0; }} />}
    <div ref={list} className="catalog-checks" role="group" aria-label={title}>{matching.map(item => <label className="check" key={item.key}><input type="checkbox" checked={selected.includes(item.key)} onChange={() => change(item.key)} />{item.complexityInfo ? <ComplexityBadge info={item.complexityInfo} /> : <span>{item.value}</span>}</label>)}</div>
    {!matching.length && <p className="muted" role="status">Ничего не найдено</p>}
  </FilterSection>;
}
export function CatalogFilters({ community, applied, options, search, sort, onApply, onClose }: { community: Community; applied: Filters; options: FilterOptions; search: string; sort: string; onApply: (f: Filters) => void; onClose: () => void }) {
  const [draft, setDraft] = useState(() => normalizeFilters(applied, community.mode, options));
  const [preview, setPreview] = useState<{ request: { params: string; retry: number }; total?: number; error?: boolean }>();
  const [retry, setRetry] = useState(0);
  const dialog = useMobileDialog(onClose);
  const normalized = normalizeFilters(draft, community.mode, options);
  const yearError = yearFilterError(draft);
  const params = catalogParams(community.key, community.mode, normalized, search, sort);
  const request = useMemo(() => ({ params, retry, yearError }), [params, retry, yearError]);
  useEffect(() => {
    if (yearError) return;
    const controller = new AbortController(); let current = true;
    const timer = window.setTimeout(() => {
      api<{ total: number }>(`/catalog?${params}&countOnly=true`, { signal: controller.signal })
        .then(result => { if (current) setPreview({ request, total: result.total }); })
        .catch(() => { if (current) setPreview({ request, error: true }); });
    }, 300);
    return () => { current = false; controller.abort(); window.clearTimeout(timer); };
  }, [request, yearError]);
  const current = preview?.request === request ? preview : undefined;
  const select = (key: "ownership" | "availability" | "planning", labels: Record<string, string>, all: string) => <select aria-label={key === "ownership" ? "Чья игра" : key === "availability" ? "Коробка на кэмпе" : "Сборы"} value={normalized[key]} onChange={e => setDraft({ ...normalizeFilters({ ...draft, [key]: e.target.value }, community.mode, options), fromYear: draft.fromYear, toYear: draft.toYear })}><option value="">{all}</option>{Object.entries(labels).filter(([value]) => key !== "ownership" || value !== "participants" || community.mode === "Camp").map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select>;
  return <dialog ref={dialog} id="catalog-filters" className="catalog-dialog" aria-labelledby="catalog-filter-title" onCancel={e => { e.preventDefault(); onClose(); }} onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
    <div className="catalog-dialog-layout">
      <header><h2 id="catalog-filter-title">Фильтры</h2><button className="catalog-filter-close" type="button" aria-label="Закрыть без применения" onClick={onClose}><CloseIcon /></button></header>
      <div className="catalog-dialog-scroll">
        {community.mode === "Camp" && <FilterSection title="День кэмпа" summary={normalized.attendanceDate ? formatDate(normalized.attendanceDate) : undefined}><Field label="День кэмпа"><DateInput min={community.startDate} max={community.endDate} value={normalized.attendanceDate ?? ""} onChange={value => setDraft({ ...draft, attendanceDate: value })} /></Field></FilterSection>}
        <FilterSection title="Игроки" summary={normalized.players ? `${normalized.playerCountMode === "best" ? "Лучше всего" : "Подходит"}: ${normalized.players}` : undefined}>
          <Field label="Подбор по числу игроков"><select value={normalized.playerCountMode} onChange={e => setDraft({ ...draft, playerCountMode: e.target.value === "best" ? "best" : "supported" })}>
            <option value="supported">Подходит для</option><option value="best">Лучше всего для</option>
          </select></Field>
          <div className="choice-row">{[1,2,3,4,5,6].map(value => <button type="button" key={value} aria-pressed={normalized.players === value} className={normalized.players === value ? "active" : ""} onClick={() => setDraft({ ...draft, players: normalized.players === value ? undefined : value })}>{value}</button>)}</div>
          <Field label="Количество игроков"><input type="number" min="1" inputMode="numeric" value={normalized.players ?? ""} onChange={e => setDraft({ ...draft, players: e.target.value ? Number(e.target.value) : undefined })} /></Field>
        </FilterSection>
        <FilterSection title="Длительность" summary={normalized.maxDurationMinutes ? `До ${normalized.maxDurationMinutes} мин` : undefined}><Field label="Не дольше, минут"><input type="number" min="1" max="10080" value={normalized.maxDurationMinutes ?? ""} onChange={e => setDraft({ ...draft, maxDurationMinutes: e.target.value ? Number(e.target.value) : undefined })} /></Field></FilterSection>
        <FilterChoices title="Сложность" values={(options.complexities ?? []).map(x => ({ key: x.level, value: x.displayName, complexityInfo: x }))} selected={normalized.complexities} change={x => setDraft({ ...draft, complexities: toggleValue(normalized.complexities, x) })} />
        <FilterChoices title="Механики" values={(options.mechanics ?? []).map(x => ({ key: x.bggId, value: x.name }))} selected={normalized.mechanics} change={x => setDraft({ ...draft, mechanics: toggleValue(normalized.mechanics, x) })} />
        <FilterChoices title="Тип игры" values={options.types} selected={normalized.types} change={x => setDraft({ ...draft, types: toggleValue(normalized.types, x) })} />
        <FilterChoices title="Категории" values={options.categories.map(x => ({ key: x.bggId, value: x.name }))} selected={normalized.categories} change={x => setDraft({ ...draft, categories: toggleValue(normalized.categories, x) })} />
        <FilterSection title="Год выпуска" summary={normalized.fromYear || normalized.toYear ? yearFilterLabel(normalized) : undefined}>
          <div className="filter-selects">{(["fromYear", "toYear"] as const).map(key => <Field key={key} label={key === "fromYear" ? "От" : "До"}>
            <input type="number" inputMode="numeric" min={minPublicationYear} max={maxPublicationYear} step="1" value={draft[key] ?? ""}
              aria-invalid={Boolean(yearError)} aria-describedby={yearError ? "catalog-year-error" : undefined}
              onChange={e => setDraft({ ...draft, [key]: e.target.value === "" ? undefined : e.target.valueAsNumber })} />
          </Field>)}</div>
        </FilterSection>
        {community.mode === "Camp" && <FilterSection title="Коробка на кэмпе" summary={availabilityLabels[normalized.availability]}>{select("availability", availabilityLabels, "Любая")}</FilterSection>}
        <FilterSection title="Чья игра" summary={ownershipLabels[normalized.ownership]}>{select("ownership", ownershipLabels, "Все источники")}</FilterSection>
        {community.mode === "Camp" && normalized.ownership === "participants" && <FilterChoices title="Кто может привезти" values={(options.providers ?? []).map(x => ({ key: x.participantId, value: x.displayName }))} selected={normalized.providers} change={x => setDraft({ ...draft, providers: toggleValue(normalized.providers, x) })} />}
        <FilterSection title="Сборы" summary={planningLabels[normalized.planning]}>{select("planning", planningLabels, "Все игры")}</FilterSection>
      </div>
      <footer>{yearError ? <div id="catalog-year-error" role="alert">{yearError}</div> : <div className={current?.error ? "catalog-preview-error" : "visually-hidden"} role="status" aria-live="polite">{current?.error ? <>Не удалось посчитать игры. <button className="ghost" onClick={() => { setPreview(undefined); setRetry(x => x + 1); }}>Повторить</button></> : current?.total === undefined ? "Считаем подходящие игры…" : `Подходят: ${current.total}`}</div>}<div className="catalog-filter-actions"><button type="button" onClick={() => setDraft(emptyFilters())}>Сбросить</button><button className="primary" type="button" aria-busy={!yearError && !current} disabled={Boolean(yearError)} onClick={() => { if (!yearError) onApply(normalized); }}>{!yearError && current?.total !== undefined ? showGames(current.total) : "Показать игры"}</button></div></footer>
    </div>
  </dialog>;
}
