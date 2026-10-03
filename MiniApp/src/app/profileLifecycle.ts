export const profileDeletedEvent = "oyinq:profile-deleted";
let generation = 0;
export const profileGeneration = () => generation;

export function clearProfileLocalState() {
  for (const storageName of ["localStorage", "sessionStorage"] as const) {
    try {
      const storage = window[storageName];
      const keys = Array.from({ length: storage.length }, (_, index) => storage.key(index));
      for (const key of keys) if (key?.startsWith("oyinq-")) storage.removeItem(key);
    } catch { /* Erasure also works when browser storage is unavailable. */ }
  }
}

export function profileWasDeleted() {
  generation++;
  window.dispatchEvent(new Event(profileDeletedEvent));
  clearProfileLocalState();
}

export function profileWasRecreated() {
  generation++;
  clearProfileLocalState();
}
