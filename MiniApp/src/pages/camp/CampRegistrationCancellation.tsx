import { useRef, useState } from "react";
import { api, json } from "../../api/client";
import type { Community } from "../../api/types";
import { Notice } from "../../components/Ui";
import { telegram } from "../../telegram/webApp";

export function CampRegistrationCancellation({ community, registered, done }: {
  community: Community; registered: boolean; done: () => void;
}) {
  const [cancelled, setCancelled] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const pending = useRef(false);
  async function unregister() {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(undefined);
    try {
      if (!await telegram.confirm("Отменить регистрацию на кэмп?\n\nВсе ваши будущие сборы в этом кэмпе будут отменены, а участники получат уведомление. Вы также выйдете из будущих сборов других организаторов. История и личная коллекция сохранятся.")) return;
      await api("/camp/registration/unregister", json("POST", { communityKey: community.key }));
      setCancelled(true); telegram.success("Регистрация отменена"); done();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { pending.current = false; setBusy(false); }
  }
  return <section className="content-section registration-cancellation">
    <h2>Отменить регистрацию</h2><p className="muted">{community.name}</p>
    {cancelled || !registered ? <Notice>{cancelled ? "Регистрация отменена." : "У вас нет регистрации на этот кэмп."}</Notice> : <>
      <p>После отмены регистрации:</p>
      <ul>
        <li>Все ваши будущие сборы в этом кэмпе отменятся. Участники получат уведомление.</li>
        <li>Вы выйдете из будущих сборов других организаторов, а места освободятся.</li>
        <li>Отметки доступности ваших игр в этом кэмпе удалятся.</li>
      </ul>
      <p className="muted">История прошедших сборов и личная коллекция сохранятся.</p>
      {error && <Notice kind="danger">{error}</Notice>}
      <button type="button" className="danger" disabled={busy} aria-busy={busy} onClick={() => void unregister()}>{busy ? "Отменяем…" : "Отменить регистрацию на кэмп"}</button>
    </>}
  </section>;
}
