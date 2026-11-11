import { Notice } from "./Ui";
import { telegram } from "../telegram/webApp";

export function BotStartNotice({ required, startUrl, refresh }: { required?: boolean; startUrl?: string; refresh: () => void }) {
  if (!required) return null;
  return <Notice kind="warning"><p>Запустите бота для уведомлений о сборах.</p>
    {startUrl ? <button className="primary" onClick={() => telegram.openContact(startUrl)}>Запустить OyinQ</button>
      : <p>Откройте личный чат с ботом и отправьте /start.</p>}
    <button onClick={refresh}>Я запустил бота</button>
  </Notice>;
}
