import { useId } from "react";
import { Field } from "../../components/Ui";
import type { PlayerCountRange, PlayerLimits } from "./playerCountRange";

export function GatheringPlayerLimits({ range, value, onChange, disabled }: {
  range: PlayerCountRange; value: PlayerLimits; onChange: (value: PlayerLimits) => void; disabled?: boolean;
}) {
  const options = Array.from({ length: range.maximum - range.minimum + 1 }, (_, index) => range.minimum + index);
  const helpId = useId();
  return <div><div className="limits">
    <Field label="Минимум"><select aria-describedby={helpId} disabled={disabled} value={value.minimum} onChange={event => onChange({ ...value, minimum: +event.target.value })}>
      {options.filter(option => option <= value.desired).map(option => <option key={option}>{option}</option>)}
    </select></Field>
    <Field label="Оптимально"><select aria-describedby={helpId} disabled={disabled} value={value.desired} onChange={event => onChange({ ...value, desired: +event.target.value })}>
      {options.filter(option => option >= value.minimum && option <= value.maximum).map(option => <option key={option}>{option}</option>)}
    </select></Field>
    <Field label="Максимум"><select aria-describedby={helpId} disabled={disabled} value={value.maximum} onChange={event => onChange({ ...value, maximum: +event.target.value })}>
      {options.filter(option => option >= value.desired).map(option => <option key={option}>{option}</option>)}
    </select></Field>
  </div><p id={helpId} className="gathering-player-help">Включая вас. Если к началу не наберётся минимум, сбор отменится.</p></div>;
}
