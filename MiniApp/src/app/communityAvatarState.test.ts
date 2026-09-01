import { describe, expect, it } from "vitest";
import { shouldShowCommunityPhoto } from "./communityAvatarState";

describe("community avatar", () => {
  it("prefers an available Telegram photo", () => {
    expect(shouldShowCommunityPhoto("data:image/jpeg;base64,AQID")).toBe(true);
  });

  it("falls back when no photo exists or loading failed", () => {
    expect(shouldShowCommunityPhoto()).toBe(false);
    expect(shouldShowCommunityPhoto("data:image/jpeg;base64,AQID", true)).toBe(false);
  });
});
