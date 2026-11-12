import type { ComplexityInfo } from "../api/types";
import { Badge } from "./Ui";

export function ComplexityBadge({ info, showUnknown = false }: { info?: ComplexityInfo | null; showUnknown?: boolean }) {
  if (!info) return showUnknown ? <span className="complexity-unknown" title="В сохранённых данных сложность не указана">Сложность —</span> : null;
  return <Badge className={`complexity-badge ${info.cssClass}`}>{info.displayName}</Badge>;
}

export function ComplexityDetails({ info }: { info?: ComplexityInfo | null }) {
  if (!info) return null;
  return <p>Сложность: {info.weight != null && `${new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 2 }).format(info.weight)} · `}<ComplexityBadge info={info} /></p>;
}
