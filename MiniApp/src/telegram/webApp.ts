const app = window.Telegram?.WebApp;
export const successEventName = "oyinq:success";
function applyTheme() {
  const scheme = app?.colorScheme ?? "light";
  document.documentElement.dataset.theme = scheme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content",
    app?.themeParams?.bg_color ?? (scheme === "dark" ? "#111318" : "#f4f6f8"));
}
export const telegram = {
  get initData() { return app?.initData ?? ""; },
  get startParam() { return app?.initDataUnsafe.start_param; },
  initialize() { applyTheme(); app?.onEvent("themeChanged", applyTheme); app?.ready(); app?.expand(); },
  requestPeer(preparedId: string): Promise<boolean> {
    if (!app?.requestChat) return Promise.resolve(false);
    return new Promise(resolve => app.requestChat(preparedId, resolve));
  },
  back(show: boolean, handler: () => void) {
    if (!app?.BackButton) return () => undefined;
    if (show) { app.BackButton.show(); app.BackButton.onClick(handler); } else app.BackButton.hide();
    return () => app.BackButton.offClick(handler);
  },
  success(message = "Готово") { app?.HapticFeedback?.notificationOccurred("success"); window.dispatchEvent(new CustomEvent(successEventName, { detail: message })); },
  warning() { app?.HapticFeedback?.notificationOccurred("warning"); },
  confirm(message: string): Promise<boolean> {
    if (app?.showConfirm) return new Promise(resolve => app.showConfirm!(message, resolve));
    return Promise.resolve(window.confirm(message));
  }
};
