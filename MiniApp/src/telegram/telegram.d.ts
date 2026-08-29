export {};
declare global {
  interface TelegramBackButton { show(): void; hide(): void; onClick(callback: () => void): void; offClick(callback: () => void): void }
  interface TelegramMainButton { show(): void; hide(): void; enable(): void; disable(): void; onClick(callback: () => void): void; offClick(callback: () => void): void; setText(text: string): void }
  interface TelegramHaptic { notificationOccurred(type: "error" | "success" | "warning"): void; impactOccurred(style: "light" | "medium" | "heavy"): void }
  interface TelegramWebApp {
    initData: string; initDataUnsafe: { start_param?: string }; colorScheme: "light" | "dark";
    themeParams?: { bg_color?: string };
    ready(): void; expand(): void; onEvent(event: string, callback: () => void): void; offEvent(event: string, callback: () => void): void;
    requestChat(preparedButtonId: string, callback?: (sent: boolean) => void): void;
    BackButton: TelegramBackButton; MainButton: TelegramMainButton; HapticFeedback?: TelegramHaptic;
    showConfirm?(message: string, callback: (confirmed: boolean) => void): void;
  }
  interface Window { Telegram?: { WebApp: TelegramWebApp } }
}
