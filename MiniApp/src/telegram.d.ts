interface TelegramWebApp {
  initData: string;
  initDataUnsafe: { start_param?: string };
  ready(): void;
  expand(): void;
}

interface Window {
  Telegram?: { WebApp: TelegramWebApp };
}
