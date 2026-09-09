import { Field } from "../../components/Ui";
import type { PlayerCountRange, PlayerLimits } from "./playerCountRange";

export function GatheringPlayerLimits({ range, value, onChange, disabled }: {
  range: PlayerCountRange; value: PlayerLimits; onChange: (value: PlayerLimits) => void; disabled?: boolean;
}) {
  const options = Array.from({ length: range.maximum - range.minimum + 1 }, (_, index) => range.minimum + index);
  return <div className="limits">
    <Field label="Минимум"><select disabled={disabled} value={value.minimum} onChange={event => onChange({ ...value, minimum: +event.target.value })}>
      {options.filter(option => option <= value.desired).map(option => <option key={option}>{option}</option>)}
    </select></Field>
    <Field label="Оптимально"><select disabled={disabled} value={value.desired} onChange={event => onChange({ ...value, desired: +event.target.value })}>
      {options.filter(option => option >= value.minimum && option <= value.maximum).map(option => <option key={option}>{option}</option>)}
    </select></Field>
    <Field label="Максимум"><select disabled={disabled} value={value.maximum} onChange={event => onChange({ ...value, maximum: +event.target.value })}>
      {options.filter(option => option >= value.desired).map(option => <option key={option}>{option}</option>)}
    </select></Field>
  </div>;
}
