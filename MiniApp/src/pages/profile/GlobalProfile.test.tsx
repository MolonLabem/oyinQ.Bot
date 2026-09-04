import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
const hooks = vi.hoisted(() => ({ data: [] as unknown }));
vi.mock("../../hooks/useAsync", () => ({ useAsync: () => ({ data: hooks.data, loading: false, reload: vi.fn() }) }));
vi.mock("../../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
import { GlobalProfileShell } from "../../app/App";
import { ProfileCollectionPage } from "./ProfileCollectionPage";
import { ProfileTabs } from "./ProfilePage";

describe("глобальный профиль", () => {
  it("позволяет открыть профиль при пустом списке сообществ", () => {
    const markup = renderToStaticMarkup(<GlobalProfileShell profile select={() => {}} communities={<p>Нет сообществ</p>}>
      <ProfileTabs active="collection" select={() => {}} />
    </GlobalProfileShell>);
    expect(markup).toContain("Сообщества"); expect(markup).toContain("Профиль");
    expect(markup).toContain("Моя коллекция"); expect(markup).toContain("Календарь"); expect(markup).toContain("Настройки");
    expect(markup).not.toContain("Нет сообществ");
  });
  it("сохраняет вход в профиль на экране выбора сообщества", () => {
    const markup = renderToStaticMarkup(<GlobalProfileShell profile={false} select={() => {}} communities={<p>Нет сообществ</p>}><p>Коллекция</p></GlobalProfileShell>);
    expect(markup).toContain("Нет сообществ"); expect(markup).toContain("Профиль"); expect(markup).not.toContain("Коллекция");
  });
  it("показывает личную коллекцию и BGG без контекстных действий кэмпа", () => {
    vi.stubGlobal("location", { search: "" });
    vi.stubGlobal("localStorage", { getItem: () => null });
    hooks.data = [{ bggId: 42, itemType: "BaseGame", snapshot: { name: "Моя игра" } }];
    const markup = renderToStaticMarkup(<ProfileCollectionPage bggAvailable />);
    expect(markup).toContain("Моя игра"); expect(markup).toContain("Импортировать коллекцию"); expect(markup).toContain("Добавить одну игру");
    expect(markup).not.toContain("Могу привезти"); expect(markup).not.toContain("Точно привезу"); expect(markup).not.toContain("Регистрация на кэмп");
    vi.unstubAllGlobals();
  });
});
