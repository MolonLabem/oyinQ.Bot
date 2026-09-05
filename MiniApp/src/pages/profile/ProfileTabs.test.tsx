import { renderToStaticMarkup } from "react-dom/server";
import type { ReactElement } from "react";
import { describe, expect, it, vi } from "vitest";
vi.mock("../../telegram/webApp", () => ({ telegram: { openContact: vi.fn() }, successEventName: "test:success" }));
import { telegram } from "../../telegram/webApp";
import { ProfileTabs } from "./ProfilePage";
import { BotStartNotice } from "../../components/BotStartNotice";
import { mainTab } from "../../app/launchContext";

describe("профиль", () => {
  it("объединяет коллекцию, календарь и настройки внутренними вкладками", () => {
    const select = vi.fn();
    const view = ProfileTabs({ active: "calendar", select });
    const markup = renderToStaticMarkup(view);
    expect(markup).toContain("Моя коллекция");
    expect(markup).toContain("Календарь");
    expect(markup).toContain("Настройки");
    expect(markup.match(/role="tab"/g)).toHaveLength(3);
    expect(markup).toContain('aria-selected="true"');
  });

  it("старый раздел mine открывает профиль", () => {
    expect(mainTab("mine")).toBe("profile");
    expect(mainTab("games")).toBe("games");
    expect(mainTab(null)).toBe("gatherings");
  });

  it("показывает честное приглашение запустить бота только до первого личного сообщения", () => {
    const markup = renderToStaticMarkup(<BotStartNotice required startUrl="https://t.me/RuntimeBot?start=context" refresh={() => {}} />);
    expect(markup).toContain("Чтобы получать уведомления о сборах, один раз запустите бота.");
    expect(markup).toContain("Запустить OyinQ");
    const notice = BotStartNotice({ required: true, startUrl: "https://t.me/RuntimeBot?start=context", refresh: () => {} })!;
    const children = notice.props.children as ReactElement<{ onClick: () => void }>[];
    children[1].props.onClick();
    expect(telegram.openContact).toHaveBeenCalledWith("https://t.me/RuntimeBot?start=context");
    expect(renderToStaticMarkup(<BotStartNotice required={false} refresh={() => {}} />)).toBe("");
    expect(renderToStaticMarkup(<BotStartNotice required refresh={() => {}} />)).toContain("/start");
  });
});
