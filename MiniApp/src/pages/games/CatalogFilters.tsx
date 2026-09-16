import { useEffect, useMemo, useState, type ReactNode } from "react";
import { api } from "../../api/client";
import type { Community } from "../../api/types";
import { activeGroups, availabilityLabels, catalogParams, emptyFilters, filterChips, normalizeFilters, ownershipLabels, planningLabels, showGames, type Filters, type FilterOptions } from "../../app/catalogFilters";
import { toggleValue } from "../../app/catalogQuery";
import { Field } from "../../components/Ui";
import { useMobileDialog } from "../../hooks/useMobileDialog";

export function FilterSection({ title, summary, children }: { title: string; summary: string; children: ReactNode }) {
  return <details className="catalog-filter-section"><summary><strong>{title}</strong><span>{summary}</span></summary><div className="catalog-section-content">{children}</div></details>;
}
export function FilterChoices<T extends string | number>({ title, values, selected, change }: { title: string; values: { key: T; value: string }[]; selected: T[]; change: (value: T) => void }) {
  const [expanded, setExpanded] = useState(false);
  const [search, setSearch] = useState("");
  const matching = values.filter(x => x.value.toLocaleLowerCase("ru").includes(search.toLocaleLowerCase("ru")));
  const shown = expanded ? matching : values.slice(0, 5);
  return <FilterSection title={title} summary={selected.length ? `Выбрано: ${selected.length}` : "Любые"}>
    {selected.length > 0 && <div className="filter-selection-chips">{selected.map(key => <button type="button" className="filter-chip" key={key} aria-label={`Убрать: ${values.find(x => x.key === key)?.value}`} onClick={() => change(key)}>{values.find(x => x.key === key)?.value} ×</button>)}</div>}
    {expanded && values.length > 15 && <Field label={`Поиск: ${title.toLowerCase()}`}><input type="search" value={search} onChange={e => setSearch(e.target.value)} /></Field>}
    <div className="catalog-checks">{shown.map(item => <label className="check" key={item.key}><input type="checkbox" checked={selected.includes(item.key)} onChange={() => change(item.key)} /><span>{item.value}</span></label>)}</div>
    {!matching.length && <p className="muted">Нет подходящих вариантов</p>}
    {values.length > 5 && <button className="ghost" type="button" aria-expanded={expanded} onClick={() => { setExpanded(!expanded); setSearch(""); }}>{expanded ? "Свернуть" : `Показать ещё · ${values.length - 5}`}</button>}
  </FilterSection>;
}
export function CatalogFilters({ community, applied, options, search, sort, onApply, onClose }: { community: Community; applied: Filters; options: FilterOptions; search: string; sort: string; onApply: (f: Filters) => void; onClose: () => void }) {
  const [draft, setDraft] = useState(() => normalizeFilters(applied, community.mode, options));
  const [preview, setPreview] = useState<{ request: { params: string; retry: number }; total?: number; error?: boolean }>();
  const [retry, setRetry] = useState(0);
  const dialog = useMobileDialog(onClose);
  const normalized = normalizeFilters(draft, community.mode, options);
  const params = catalogParams(community.key, community.mode, normalized, search, sort);
  const request = useMemo(() => ({ params, retry }), [params, retry]);
  useEffect(() => {
    const controller = new AbortController(); let current = true;
    const timer = window.setTimeout(() => {
      api<{ total: number }>(`/catalog?${params}&countOnly=true`, { signal: controller.signal })
        .then(result => { if (current) setPreview({ request, total: result.total }); })
        .catch(() => { if (current) setPreview({ request, error: true }); });
    }, 300);
    return () => { current = false; controller.abort(); window.clearTimeout(timer); };
  }, [request]);
  const chips = filterChips(normalized, options);
  const current = preview?.request === request ? preview : undefined;
  const select = (key: "ownership" | "availability" | "planning", labels: Record<string, string>, all: string) => <select aria-label={key === "ownership" ? "Чья игра" : key === "availability" ? "Коробка на кэмпе" : "Сборы"} value={normalized[key]} onChange={e => setDraft(normalizeFilters({ ...normalized, [key]: e.target.value }, community.mode, options))}><option value="">{all}</option>{Object.entries(labels).filter(([value]) => key !== "ownership" || value !== "participants" || community.mode === "Camp").map(([value, label]) => <option value={value} key={value}>{label}</option>)}</select>;
  return <dialog ref={dialog} id="catalog-filters" className="catalog-dialog" aria-labelledby="catalog-filter-title" onCancel={e => { e.preventDefault(); onClose(); }} onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
    <div className="catalog-dialog-layout">
      <header><div><h2 id="catalog-filter-title">Фильтры</h2><small className="muted">{activeGroups(normalized) ? `Выбрано групп: ${activeGroups(normalized)}` : "Подберите игру для вашей компании"}</small></div><button type="button" aria-label="Закрыть без применения" onClick={onClose}>×</button></header>
      <div className="catalog-dialog-scroll">
        {community.mode === "Camp" && <FilterSection title="День кэмпа" summary={normalized.attendanceDate || "Все дни"}><Field label="День кэмпа"><input type="date" min={community.startDate} max={community.endDate} value={normalized.attendanceDate ?? ""} onChange={e => setDraft({ ...normalized, attendanceDate: e.target.value })} /></Field></FilterSection>}
        <FilterSection title="Длительность" summary={normalized.maxDurationMinutes ? `До ${normalized.maxDurationMinutes} мин` : "Любая"}><Field label="Не дольше, минут"><input type="number" min="1" max="10080" value={normalized.maxDurationMinutes ?? ""} onChange={e => setDraft({ ...normalized, maxDurationMinutes: e.target.value ? Number(e.target.value) : undefined })} /></Field></FilterSection>
        <FilterChoices title="Сложность BGG" values={(options.complexities ?? []).map(x => ({ key: x.level, value: x.displayName }))} selected={normalized.complexities} change={x => setDraft({ ...normalized, complexities: toggleValue(normalized.complexities, x) })} />
        <FilterChoices title="Механики" values={(options.mechanics ?? []).map(x => ({ key: x.bggId, value: x.name }))} selected={normalized.mechanics} change={x => setDraft({ ...normalized, mechanics: toggleValue(normalized.mechanics, x) })} />
        <FilterSection title="Игроки" summary={normalized.players ? `${normalized.players}` : "Любое количество"}><div className="choice-row">{[1,2,3,4,5,6].map(value => <button type="button" key={value} aria-pressed={normalized.players === value} className={normalized.players === value ? "active" : ""} onClick={() => setDraft({ ...normalized, players: normalized.players === value ? undefined : value })}>{value}</button>)}</div><Field label="Количество игроков"><input type="number" min="1" inputMode="numeric" value={normalized.players ?? ""} onChange={e => setDraft({ ...normalized, players: e.target.value ? Number(e.target.value) : undefined })} /></Field></FilterSection>
        <FilterSection title="Чья игра" summary={ownershipLabels[normalized.ownership] ?? "Все источники"}>{select("ownership", ownershipLabels, "Все источники")}</FilterSection>
        {community.mode === "Camp" && normalized.ownership === "participants" && <FilterChoices title="Кто может привезти" values={(options.providers ?? []).map(x => ({ key: x.participantId, value: x.displayName }))} selected={normalized.providers} change={x => setDraft({ ...normalized, providers: toggleValue(normalized.providers, x) })} />}
        {community.mode === "Camp" && <FilterSection title="Коробка на кэмпе" summary={availabilityLabels[normalized.availability] ?? "Любая"}>{select("availability", availabilityLabels, "Любая")}</FilterSection>}
        <FilterSection title="Сборы" summary={planningLabels[normalized.planning] ?? "Все игры"}>{select("planning", planningLabels, "Все игры")}</FilterSection>
        <FilterChoices title="Тип игры" values={options.types} selected={normalized.types} change={x => setDraft({ ...normalized, types: toggleValue(normalized.types, x) })} />
        <FilterChoices title="Категории" values={options.categories.map(x => ({ key: x.bggId, value: x.name }))} selected={normalized.categories} change={x => setDraft({ ...normalized, categories: toggleValue(normalized.categories, x) })} />
        {chips.length > 0 && <p className="muted">Изменения появятся в каталоге после нажатия «Показать».</p>}
      </div>
      <footer><div role="status" aria-live="polite">{current?.error ? <>Не удалось посчитать игры. <button className="ghost" onClick={() => { setPreview(undefined); setRetry(x => x + 1); }}>Повторить</button></> : current?.total === undefined ? "Считаем подходящие игры…" : `Подходят: ${current.total}`}</div><div className="catalog-filter-actions"><button type="button" onClick={() => setDraft(emptyFilters())}>Сбросить</button><button className="primary" type="button" onClick={() => onApply(normalized)}>{current?.total !== undefined ? showGames(current.total) : "Показать игры"}</button></div></footer>
    </div>
  </dialog>;
}
