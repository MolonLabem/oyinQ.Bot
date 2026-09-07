import { describe, expect, it } from "vitest";
import {
  attendanceOutcomeTone,
  campStatusTone,
  gatheringStatusTone,
  importStatusTone,
  participationStatusTone
} from "./semanticTones";

describe("semantic tones", () => {
  it("maps gathering lifecycle without treating every active state as accent", () => {
    expect(gatheringStatusTone("Recruiting")).toBe("attention");
    expect(gatheringStatusTone("Ready")).toBe("success");
    expect(gatheringStatusTone("Full")).toBe("success");
    expect(gatheringStatusTone("Closed")).toBe("neutral");
    expect(gatheringStatusTone("Completed")).toBe("neutral");
    expect(gatheringStatusTone("Cancelled")).toBe("danger");
  });

  it("distinguishes participation, import, attendance and camp states", () => {
    expect(participationStatusTone("Confirmed")).toBe("success");
    expect(participationStatusTone("Waitlisted")).toBe("attention");
    expect(participationStatusTone("Withdrawn")).toBe("neutral");
    expect(importStatusTone("Queued", "Queued")).toBe("neutral");
    expect(importStatusTone("Running", "Saving")).toBe("accent");
    expect(importStatusTone("Completed", "Completed")).toBe("success");
    expect(importStatusTone("Failed", "Failed")).toBe("danger");
    expect(attendanceOutcomeTone("Attended")).toBe("success");
    expect(attendanceOutcomeTone("NoShow")).toBe("danger");
    expect(attendanceOutcomeTone("Unknown")).toBe("neutral");
    expect(campStatusTone("Active")).toBe("success");
    expect(campStatusTone("Cancelled")).toBe("danger");
  });

  it("keeps unknown backend values safely neutral", () => {
    expect(gatheringStatusTone("FutureStatus")).toBe("neutral");
    expect(importStatusTone(undefined)).toBe("neutral");
  });
  it("keeps terminal import status authoritative over a stale progress stage", () => {
    expect(importStatusTone("Failed", "Saving")).toBe("danger");
    expect(importStatusTone("Cancelled", "FetchingGames")).toBe("neutral");
    expect(importStatusTone("Confirmed", "Saving")).toBe("success");
  });
});
