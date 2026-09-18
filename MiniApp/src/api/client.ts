import type { ApiErrorBody } from "./types";
import { telegram } from "../telegram/webApp";

export class ApiError extends Error {
  constructor(message: string, public status: number, public code?: string, public currentRevision?: number, public affectedGatherings?: ApiErrorBody["affectedGatherings"], public requiredDate?: string, public conflicts?: ApiErrorBody["conflicts"]) { super(message); }
}

export function fallbackApiError(status: number) {
  if (status === 401) return "Не удалось подтвердить вход. Закройте приложение и снова откройте его из Telegram.";
  if (status === 403) return "У вас нет доступа к этому действию.";
  if (status === 404) return "Ничего не найдено. Возможно, запись уже удалена.";
  if (status >= 500) return "OyinQ временно недоступен. Попробуйте ещё раз позже.";
  return "Не удалось выполнить запрос. Проверьте данные и попробуйте ещё раз.";
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers);
  headers.set("X-Telegram-Init-Data", telegram.initData);
  if (options.body && !(options.body instanceof FormData)) headers.set("Content-Type", "application/json");
  let response: Response;
  try { response = await fetch(`/api/miniapp${path}`, { ...options, headers }); }
  catch { throw new ApiError("Нет соединения с OyinQ. Проверьте интернет и попробуйте ещё раз.", 0, "network_error"); }
  if (!response.ok) {
    const body = await response.json().catch(() => ({} as ApiErrorBody)) as ApiErrorBody;
    throw new ApiError(body.message?.trim() || fallbackApiError(response.status), response.status, body.code, body.currentRevision, body.affectedGatherings, body.requiredDate, body.conflicts);
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>;
}

export const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });

export async function download(path: string, fileName: string, signal?: AbortSignal): Promise<void> {
  let response: Response;
  try { response = await fetch(`/api/miniapp${path}`, { headers: { "X-Telegram-Init-Data": telegram.initData }, signal, cache: "no-store" }); }
  catch (error) { if (signal?.aborted) throw error; throw new ApiError("Нет соединения с OyinQ. Не удалось скачать файл.", 0, "network_error"); }
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as ApiErrorBody;
    throw new ApiError(body.message?.trim() || fallbackApiError(response.status), response.status, body.code);
  }
  const disposition = response.headers.get("Content-Disposition");
  const encodedName = /filename\*=UTF-8''([^;]+)/i.exec(disposition ?? "")?.[1];
  const quotedName = /filename="([^"]+)"/i.exec(disposition ?? "")?.[1];
  try { fileName = encodedName ? decodeURIComponent(encodedName) : quotedName ?? fileName; } catch { /* Keep the caller's safe fallback. */ }
  fileName = fileName.replace(/[\\/\x00-\x1f\x7f]/g, "_");
  const url = URL.createObjectURL(await response.blob());
  if (signal?.aborted) { URL.revokeObjectURL(url); return; }
  const link = document.createElement("a"); link.href = url; link.download = fileName;
  try { document.body.appendChild(link); link.click(); }
  finally {
    link.remove();
    // Embedded browsers may begin reading the object URL after the click handler returns.
    window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }
}

export async function gatheringMutation<T>(path: string, options: RequestInit): Promise<T> {
  try { return await api<T>(path, options); }
  catch (e) {
    if (!(e instanceof ApiError) || e.code !== "gathering_schedule_conflict" || !e.conflicts?.length) throw e;
    const summary = e.conflicts.map(x => `${x.gameName} · ${new Intl.DateTimeFormat("ru-RU", { timeZone: x.timeZoneId, dateStyle: "short", timeStyle: "short" }).format(new Date(x.startsAtUtc))} · ${x.community}`).join("\n");
    if (!await telegram.confirm(`Возможное пересечение\n\n${summary}\n\nВсё равно продолжить?`)) throw new Error("Действие отменено.");
    return api<T>(path, { ...options, body: JSON.stringify({ ...JSON.parse(String(options.body)), confirmScheduleConflict: true }) });
  }
}
