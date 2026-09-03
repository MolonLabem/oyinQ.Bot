import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
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
});
