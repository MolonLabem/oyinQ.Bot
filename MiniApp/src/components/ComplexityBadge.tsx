import type { ComplexityInfo } from "../api/types";
import { Badge } from "./Ui";

export function ComplexityBadge({ info }: { info?: ComplexityInfo | null }) {
  if (!info) return null;
  return <Badge className={info.cssClass}>⚖️ {info.displayName}</Badge>;
}

export function ComplexityDetails({ info }: { info?: ComplexityInfo | null }) {
  if (!info) return null;
  return <p>⚖️ Сложность: {info.weight != null && `${new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 2 }).format(info.weight)} · `}{info.displayName}</p>;
}
