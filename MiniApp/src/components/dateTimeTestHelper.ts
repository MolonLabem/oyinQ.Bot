import { act } from "react";

export function readDateTime(host: Element, index = 0) {
  const group = host.querySelectorAll(".datetime-field")[index];
  const native = group.querySelector<HTMLInputElement>('input[type="datetime-local"]');
  if (native) return native.value;
  const date = group.querySelector<HTMLInputElement>('input[data-date-input]')!.getAttribute("data-date-value");
  const parts = group.querySelectorAll("select");
  return date ? `${date}T${parts[0].value}:${parts[1].value}` : "";
}
export async function changeDateTime(host: Element, value: string, index = 0) {
  const group = host.querySelectorAll(".datetime-field")[index];
  const native = group.querySelector<HTMLInputElement>('input[type="datetime-local"]');
  if (native) {
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(native, value);
      native.dispatchEvent(new Event("input", { bubbles: true }));
    });
    return;
  }
  await act(async () => {
    const date = group.querySelector<HTMLInputElement>('input[data-date-input]')!;
    const text = date.type === "date" ? value.slice(0, 10) : value.slice(0, 10).split("-").reverse().join(".");
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(date, text);
    date.dispatchEvent(new Event("input", { bubbles: true }));
  });
  if (!value) return;
  for (const [index, part] of [value.slice(11, 13), value.slice(14, 16)].entries()) {
    await act(async () => {
      const select = group.querySelectorAll("select")[index];
      select.value = part; select.dispatchEvent(new Event("change", { bubbles: true }));
    });
  }
}
