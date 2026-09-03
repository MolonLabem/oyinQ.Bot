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
      localDateTime: "12 сентября, 19:00", isOrganizer: true
    }]} open={() => undefined} />);

    expect(markup).toContain("RollMove — Покорение Марса");
    expect(markup).toContain("12 сентября, 19:00");
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
      communityMode: "Camp", gameName: "Brass", localDateTime: "12 сентября, 19:00",
      isOrganizer: false
    }], open });
    const button = (view.props.children as ReactElement[])[0];

    (button.props as { onClick: () => void }).onClick();

    expect(open).toHaveBeenCalledWith("camp-2026", "g-42");
  });
});
