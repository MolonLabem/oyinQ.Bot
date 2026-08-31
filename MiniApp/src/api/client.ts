import type { ApiErrorBody } from "./types";
import { telegram } from "../telegram/webApp";

export class ApiError extends Error {
  constructor(message: string, public status: number, public code?: string, public currentRevision?: number, public affectedGatherings?: ApiErrorBody["affectedGatherings"], public requiredDate?: string) { super(message); }
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers);
  headers.set("X-Telegram-Init-Data", telegram.initData);
  if (options.body && !(options.body instanceof FormData)) headers.set("Content-Type", "application/json");
  const response = await fetch(`/api/miniapp${path}`, { ...options, headers });
  if (!response.ok) {
    const body = await response.json().catch(() => ({} as ApiErrorBody)) as ApiErrorBody;
    throw new ApiError(body.message ?? "Не удалось выполнить запрос.", response.status, body.code, body.currentRevision, body.affectedGatherings, body.requiredDate);
  }
  return response.status === 204 ? undefined as T : response.json() as Promise<T>;
}

export const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });

export async function download(path: string, fileName: string): Promise<void> {
  const response = await fetch(`/api/miniapp${path}`, { headers: { "X-Telegram-Init-Data": telegram.initData } });
  if (!response.ok) throw new ApiError("Не удалось скачать файл.", response.status);
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a"); link.href = url; link.download = fileName; link.click();
  URL.revokeObjectURL(url);
}
