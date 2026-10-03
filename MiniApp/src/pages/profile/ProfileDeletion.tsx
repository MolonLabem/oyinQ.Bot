import { useRef, useState } from "react";
import { api, json } from "../../api/client";
import { Card, Notice, Page, ProductFooter, SaveButton } from "../../components/Ui";
import { profileWasRecreated, profileWasDeleted } from "../../app/profileLifecycle";
import { telegram } from "../../telegram/webApp";

export function ProfileDeletion() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const running = useRef(false);
  async function remove() {
    if (running.current) return;
    running.current = true; setBusy(true); setError(undefined);
    try {
      if (!await telegram.confirm("Удалить профиль во всех сообществах? Коллекция, хотелки и регистрации будут удалены. Вы выйдете из будущих сборов, а ваши будущие сборы отменятся. Восстановить данные нельзя.")) return;
      await api<void>("/profile", json("DELETE", { confirmed: true }));
      profileWasDeleted();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { running.current = false; setBusy(false); }
  }
  return <Card className="danger-zone"><h2>Удалить профиль</h2>
    <p>Удаление действует во всех сообществах: исчезнут имя, коллекция, хотелки, регистрации на кэмпы и ответы на вопросы. Уведомления отключатся.</p>
    <p>Вы выйдете из будущих сборов, а сборы, которые вы организуете, отменятся. История сыгранных партий останется без вашего имени и контактов.</p>
    <p className="muted">Восстановить данные нельзя. Отправленные сообщения Telegram и данные в ранее скачанных файлах останутся у получателей. Для сохранения вашего выбора останется только Telegram ID и дата удаления.</p>
    {error && <Notice kind="danger">{error}</Notice>}
    <SaveButton busy={busy} label="Удалить мой профиль" busyLabel="Удаляем…" className="danger ghost" onClick={remove} />
  </Card>;
}

export function DeletedProfilePage({ onRecreated }: { onRecreated: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const running = useRef(false);
  async function recreate() {
    if (running.current) return;
    running.current = true; setBusy(true); setError(undefined);
    try {
      if (!await telegram.confirm("Создать новый профиль с данными вашего Telegram? Прежняя коллекция, регистрации и история участия не восстановятся.")) return;
      await api<void>("/profile/recreate", json("POST", { confirmed: true }));
      profileWasRecreated();
      history.replaceState({}, "", `${location.pathname}?tab=profile`);
      onRecreated();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
    finally { running.current = false; setBusy(false); }
  }
  return <Page title="Профиль удалён"><Card className="form-grid">
    <p>Личные данные удалены, уведомления отключены. Открытие приложения и сообщения боту не восстанавливают профиль.</p>
    <p>Если захотите вернуться, создайте новый профиль с пустой коллекцией и новыми регистрациями.</p>
    {error && <Notice kind="danger">{error}</Notice>}
    <SaveButton busy={busy} label="Создать новый профиль" busyLabel="Создаём…" onClick={recreate} />
  </Card><ProductFooter /></Page>;
}
