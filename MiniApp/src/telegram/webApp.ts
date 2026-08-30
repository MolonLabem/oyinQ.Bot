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
  get canFullscreen() { return Boolean(app?.requestFullscreen); },
  requestFullscreen(): Promise<boolean> {
    if (!app?.requestFullscreen) return Promise.resolve(false);
    return new Promise(resolve => {
      let settled = false;
      const finish = (value: boolean) => { if (settled) return; settled = true; window.clearTimeout(timer); app.offEvent("fullscreenChanged", changed); app.offEvent("fullscreenFailed", failed); resolve(value); };
      const changed = () => finish(true); const failed = () => finish(false);
      const timer = window.setTimeout(() => finish(Boolean(app.isFullscreen)), 1800);
      app.onEvent("fullscreenChanged", changed); app.onEvent("fullscreenFailed", failed); app.requestFullscreen!();
    });
  },
  requestPeer(preparedId: string): Promise<boolean> {
    if (!app?.requestChat) return Promise.resolve(false);
    return new Promise(resolve => app.requestChat(preparedId, resolve));
  },
  back(show: boolean, handler: () => void) {
    if (!app?.BackButton) return () => undefined;
    if (show) { app.BackButton.show(); app.BackButton.onClick(handler); } else app.BackButton.hide();
    return () => { app.BackButton.offClick(handler); if (show) app.BackButton.hide(); };
  },
  success(message = "Готово") { app?.HapticFeedback?.notificationOccurred("success"); window.dispatchEvent(new CustomEvent(successEventName, { detail: message })); },
  warning() { app?.HapticFeedback?.notificationOccurred("warning"); },
  confirm(message: string): Promise<boolean> {
    if (app?.showConfirm) return new Promise(resolve => app.showConfirm!(message, resolve));
    return Promise.resolve(window.confirm(message));
  }
};
