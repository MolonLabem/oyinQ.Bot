const preferredTimeZones = [
  "Asia/Almaty",
  "Asia/Qyzylorda",
  "Asia/Aqtobe",
  "Asia/Aqtau",
  "Asia/Atyrau",
  "Asia/Oral",
  "Europe/Moscow",
  "Europe/Istanbul",
  "Asia/Tashkent",
  "Asia/Bishkek",
  "Asia/Dubai",
  "UTC",
];

function availableTimeZones(current: string) {
  const supportedValuesOf = (Intl as typeof Intl & {
    supportedValuesOf?: (key: "timeZone") => string[];
  }).supportedValuesOf;
  const supported = supportedValuesOf?.("timeZone") ?? preferredTimeZones;
  return [...new Set([current, ...preferredTimeZones, ...supported].filter(Boolean))];
}

export function TimeZoneSelect({ value, onChange, disabled = false }: {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
}) {
  const zones = availableTimeZones(value);
  const preferred = zones.filter(zone => preferredTimeZones.includes(zone));
  const others = zones.filter(zone => !preferredTimeZones.includes(zone));
  return <select value={value} disabled={disabled} onChange={event => onChange(event.target.value)}>
    <optgroup label="Часто используемые">
      {preferred.map(zone => <option key={zone} value={zone}>{zone.replaceAll("_", " ")}</option>)}
    </optgroup>
    <optgroup label="Все часовые пояса">
      {others.map(zone => <option key={zone} value={zone}>{zone.replaceAll("_", " ")}</option>)}
    </optgroup>
  </select>;
}
