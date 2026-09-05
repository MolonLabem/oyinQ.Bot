import { renderToStaticMarkup } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { GatheringDetail } from "../../api/types";

const state = vi.hoisted(() => ({ data: {} as GatheringDetail, loading: false }));
vi.mock("../../hooks/useAsync", () => ({ useAsync: () => ({ ...state, reload: vi.fn() }) }));
vi.mock("../../telegram/webApp", () => ({ telegram: {}, successEventName: "success" }));
vi.mock("../../components/Wishlist", () => ({ WishButton: () => <button>Хочу сыграть</button> }));
vi.mock("./PlayPanel", () => ({ PlayPanel: () => <section>Запись партии</section> }));
import { GatheringDetails } from "./GatheringsPage";

function render(mode: "Club" | "Camp" = "Club") {
  return renderToStaticMarkup(<GatheringDetails community={{ key: "club", name: "Клуб", mode, timeZoneId: "UTC" }}
    id="g" onBack={() => {}} onCancelled={() => {}} editRegistration={() => {}} openCollection={() => {}} />);
}

beforeEach(() => {
  state.loading = false;
  state.data = {
    gathering: { publicId: "g", gameName: "Каркассон", organizerName: "Игрок", canTeachRules: true, rulesText: "Правила объясню",
      localDateTime: "5 сентября, 18:00", occupiedSeats: 2, statusText: "Есть места", bggId: 1, expansions: [] },
    status: "Ready", currentUserStatus: "None", canJoin: true, canLeave: false, canEdit: false, canClose: false, canReopen: false,
    canCancel: false, canManageGuests: false, hasStarted: false, confirmedParticipants: [{ name: "Игрок", isOrganizer: true }],
    guestParticipants: [], waitlistedParticipants: [], publicationStatus: "Published", canRetryPublication: false,
    startsAtLocal: "2026-09-05T18:00", minimumPlayers: 2, desiredPlayers: 3, maximumPlayers: 4, canTeachRules: true,
    knownExpansions: [], selectedExpansionIds: []
  };
});

describe("gathering detail action hierarchy", () => {
  it("puts the gathering before actions and attaches joining to its summary", () => {
    const markup = render();
    expect(markup.indexOf("Каркассон")).toBeLessThan(markup.indexOf("Занять место"));
    expect(markup.indexOf("Занять место")).toBeLessThan(markup.indexOf("Кто играет"));
    expect(markup.indexOf("Кто играет")).toBeLessThan(markup.indexOf("Хочу сыграть"));
    expect(markup).not.toContain("Управление сбором");
    expect(markup).not.toContain("Отменить сбор");
  });

  it("groups organizer tools behind a disclosure and keeps cancellation last", () => {
    Object.assign(state.data, { currentUserStatus: "Organizer", canJoin: false, canEdit: true, canClose: true, canCancel: true, canRequestRecruitment: true });
    const markup = render();
    const panel = markup.slice(markup.indexOf('class="card gathering-management"'), markup.indexOf('class="card gathering-players"'));
    expect(panel).toContain("<details><summary>");
    expect(panel).toContain("Изменить сбор");
    expect(panel).toContain("Закрыть запись");
    expect(panel.indexOf("Напомнить о сборе")).toBeLessThan(panel.indexOf("Отменить сбор"));
    expect(markup).not.toContain("Занять место");
  });

  it("shows waitlist status beside leaving and disables controls while refreshing", () => {
    Object.assign(state.data, { canJoin: false, canLeave: true, currentUserStatus: "Waitlisted", waitlistPosition: 2 });
    state.loading = true;
    const markup = render();
    expect(markup).toContain("Ваша позиция в очереди: 2");
    expect(markup).toContain('disabled="">Выйти из листа ожидания');
    expect(markup).not.toContain("Загрузка…");
  });

  it("surfaces a failed announcement inside the open management panel", () => {
    Object.assign(state.data, { canJoin: false, publicationStatus: "Failed", canRetryPublication: true });
    const markup = render();
    expect(markup).toContain('<details open=""><summary>');
    expect(markup).toContain("Объявление требует внимания");
    expect(markup).toContain("Повторить обновление");
    expect(markup).not.toContain("Отменить сбор");
  });

  it("keeps Camp box commitments beside the roster", () => {
    state.data.provider = { state: "MissingProvider", summary: "Нужна коробка", isConfirmed: false, isOwned: true, canBring: true, providers: [] };
    const markup = render("Camp");
    expect(markup.indexOf("Кто играет")).toBeLessThan(markup.indexOf("Я привезу"));
    expect(markup.indexOf("Нужна коробка")).toBeLessThan(markup.indexOf("Я привезу"));
  });

  it("places historical play recording after the gathering and roster", () => {
    Object.assign(state.data, { canJoin: false, hasStarted: true, canRecordPlay: true, status: "Completed" });
    const markup = render();
    expect(markup.indexOf("Кто играет")).toBeLessThan(markup.indexOf("Запись партии"));
    expect(markup).not.toContain("Занять место");
    expect(markup).not.toContain("Управление сбором");
  });

  it("shows play confirmation in the otherwise read-only admin view", () => {
    Object.assign(state.data, { canJoin: false, hasStarted: true, canRecordPlay: true, status: "Completed" });
    const markup = renderToStaticMarkup(<GatheringDetails readOnly community={{ key: "club", name: "Клуб", mode: "Club", timeZoneId: "UTC" }}
      id="g" onBack={() => {}} onCancelled={() => {}} editRegistration={() => {}} openCollection={() => {}} />);
    expect(markup).toContain("Запись партии");
    expect(markup).not.toContain("Занять место");
    expect(markup).not.toContain("Управление сбором");
  });
});
