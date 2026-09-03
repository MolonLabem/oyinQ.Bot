import { renderToStaticMarkup } from "react-dom/server";
import type { ReactElement } from "react";
import { describe, expect, it, vi } from "vitest";
import { ProfileScheduleList, profileScheduleEmptyText } from "./ProfileScheduleList";

describe("profile gathering schedule", () => {
  it("renders community, game, local time, and organizer badge", () => {
    const markup = renderToStaticMarkup(<ProfileScheduleList communities={[
      { key: "rollmove", name: "RollMove", mode: "Club", timeZoneId: "Asia/Qyzylorda" }
    ]} items={[{
      publicId: "g-1", communityKey: "rollmove", communityName: "RollMove",
      communityMode: "Club", gameName: "Покорение Марса",
      startsAtUtc: "2026-09-12T14:00:00Z", localDate: "2026-09-12", localTime: "19:00",
      localDateTime: "12 сентября, 19:00", isOrganizer: true
    }]} open={() => undefined} />);

    expect(markup).toContain("Покорение Марса");
    expect(markup).toContain("RollMove");
    expect(markup).toContain("сб, 12 сентября");
    expect(markup).toContain("19:00");
    expect(markup).toContain("Организатор");
    expect(markup).toContain("<button");
  });

  it("provides a useful empty-state message", () => {
    expect(profileScheduleEmptyText).toContain("не записаны");
  });

  it("opens the exact gathering in its community", () => {
    const open = vi.fn();
    const view = ProfileScheduleList({ communities: [], items: [{
      publicId: "g-42", communityKey: "camp-2026", communityName: "Camp",
      communityMode: "Camp", gameName: "Brass", startsAtUtc: "2026-09-12T14:00:00Z",
      localDate: "2026-09-12", localTime: "19:00", localDateTime: "12 сентября, 19:00",
      isOrganizer: false
    }], open });
    const section = (view.props.children as ReactElement[])[0] as ReactElement<{ children: ReactElement[] }>;
    const stack = section.props.children[1] as ReactElement<{ children: ReactElement[] }>;
    const button = (stack.props.children as ReactElement[])[0];

    (button.props as { onClick: () => void }).onClick();

    expect(open).toHaveBeenCalledWith("camp-2026", "g-42");
  });

  it("sorts items chronologically and groups the same local date", () => {
    const markup = renderToStaticMarkup(<ProfileScheduleList communities={[]} open={() => undefined} items={[
      { publicId: "late", communityKey: "c", communityName: "Club", communityMode: "Club", gameName: "Late", startsAtUtc: "2026-09-13T18:00:00Z", localDate: "2026-09-13", localTime: "23:00", localDateTime: "13 сентября, 23:00", isOrganizer: false },
      { publicId: "early", communityKey: "c", communityName: "Club", communityMode: "Club", gameName: "Early", startsAtUtc: "2026-09-12T08:00:00Z", localDate: "2026-09-12", localTime: "13:00", localDateTime: "12 сентября, 13:00", isOrganizer: false },
      { publicId: "middle", communityKey: "c", communityName: "Club", communityMode: "Club", gameName: "Middle", startsAtUtc: "2026-09-12T12:00:00Z", localDate: "2026-09-12", localTime: "17:00", localDateTime: "12 сентября, 17:00", isOrganizer: false }
    ]} />);

    expect(markup.indexOf("Early")).toBeLessThan(markup.indexOf("Middle"));
    expect(markup.indexOf("Middle")).toBeLessThan(markup.indexOf("Late"));
    expect(markup.match(/class="schedule-day"/g)).toHaveLength(2);
  });
});
