type PendingCreation = { signature: string; operationId: string };
const pending = new Map<string, PendingCreation>();

export function creationOperation(communityKey: string, body: unknown): string {
  const key = `oyinq-gathering-create-${communityKey}`;
  const signature = JSON.stringify(body);
  let saved = pending.get(key);
  try { saved ??= JSON.parse(sessionStorage.getItem(key) ?? "null") as PendingCreation | undefined; } catch { /* Storage may be disabled. */ }
  if (!saved || saved.signature !== signature || !saved.operationId) saved = { signature, operationId: crypto.randomUUID() };
  pending.set(key, saved);
  try { sessionStorage.setItem(key, JSON.stringify(saved)); } catch { /* Retain the in-memory retry identity. */ }
  return saved.operationId;
}

export function completeCreation(communityKey: string) {
  const key = `oyinq-gathering-create-${communityKey}`;
  pending.delete(key);
  try { sessionStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
}
