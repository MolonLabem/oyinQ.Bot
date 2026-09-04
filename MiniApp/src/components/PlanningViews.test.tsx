import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
const mock = vi.hoisted(() => ({ data: {} as unknown }));
vi.mock("../hooks/useAsync", () => ({ useAsync: () => ({ data: mock.data, loading: false, reload: vi.fn() }) }));
vi.mock("../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
import { GatheringDashboard } from "./GatheringDashboard";
import { GameProviderNotice } from "./GameProviderNotice";
import { ReleaseAnnouncementPage } from "../pages/admin/ReleaseAnnouncementPage";
import { PlayPanel } from "../pages/gatherings/PlayPanel";

describe("экраны планирования", () => {
  it("показывает очередь и явное действие владельца без обещания автоматически привезти", () => {
    mock.data = { items: [{ publicId: "g", communityKey: "camp", community: "Кэмп", gameName: "Игра", localDateTime: "4 сентября, 18:00", waitlistPosition: 1, isToday: true, provider: { summary: "Никто пока не подтвердил коробку", canBring: true, isConfirmed: false } }] };
    const markup = renderToStaticMarkup(<GatheringDashboard communityKey="camp" open={() => {}} />);
    expect(markup).toContain("Обзор организатора"); expect(markup).toContain("Лист ожидания: 1"); expect(markup).toContain("Я привезу"); expect(markup).toContain("Никто пока не подтвердил коробку");
  });
  it("показывает коробку клуба без цветного предупреждения", () => {
    mock.data = { isConfirmed: false, summary: "Нет в коллекции клуба", isOwned: false };
    const markup = renderToStaticMarkup(<GameProviderNotice mode="Club" communityKey="club" bggId={42} />);
    expect(markup).toContain("Коробка · Нет в коллекции клуба");
    expect(markup).toContain("gathering-box-status");
    expect(markup).not.toContain("notice");
  });
  it("не представляет Completed как подтверждённую партию", () => {
    mock.data = { revision: 0, canEdit: true, canShare: false, references: [], players: [], expansions: [] };
    const markup = renderToStaticMarkup(<PlayPanel community={{ key: "club", name: "Клуб", mode: "Club", timeZoneId: "Asia/Almaty" }} id="g" />);
    expect(markup).toContain("Игра состоялась?"); expect(markup).toContain("Да, сыграли");
    expect(markup).not.toContain("Добавить в BG Stats"); expect(markup).not.toContain("Скачать JSON");
  });
  it("публикация обновления начинается с выбора и предпросмотра", () => {
    mock.data = { releaseId: "2026-09-04", text: "Что нового", targets: [{ key: "club", name: "Клуб", canPost: true }] };
    const markup = renderToStaticMarkup(<ReleaseAnnouncementPage />);
    expect(markup).toContain("Предпросмотр"); expect(markup).not.toContain("Опубликовать</button>");
  });
  it("отделяет личную коллекцию от обещания привезти игру", () => {
    mock.data = { isConfirmed: false, summary: "Никто пока не подтвердил коробку", isOwned: false };
    const markup = renderToStaticMarkup(<GameProviderNotice communityKey="camp" bggId={42} ownership={{ gameName: "Игра", add: false, bring: false, camp: true, setAdd: () => {}, setBring: () => {} }} />);
    expect(markup).toContain("в мою коллекцию"); expect(markup).toContain("на этот кэмп"); expect(markup).not.toContain("checked");
  });
  it("не даёт обычному игроку редактировать исход или скачивать файлы", () => {
    mock.data = { revision: 1, canEdit: false, canShare: true, wasPlayed: true, references: [{ id: 1, url: "https://app.bgstatsapp.com/play", author: "Виктор", canRemove: false }], players: [], expansions: [] };
    const markup = renderToStaticMarkup(<PlayPanel community={{ key: "club", name: "Клуб", mode: "Club", timeZoneId: "UTC" }} id="g" />);
    expect(markup).toContain("Виктор"); expect(markup).toContain("Поделиться ссылкой из BG Stats"); expect(markup).not.toContain("Удалить ссылку"); expect(markup).not.toContain("Сохранить запись"); expect(markup).not.toContain("Скачать");
    expect(markup.indexOf("Создать ссылку для BG Stats")).toBeLessThan(markup.indexOf("Поделиться ссылкой из BG Stats"));
  });
});
