import { describe, expect, it } from "vitest";
import type { PostingTopicSettings } from "../../api/types";
import { postingTopicTitle, selectablePostingTopics, shouldShowPostingTopic } from "./postingTopicState";

const forum: PostingTopicSettings = {
  isForum: true,
  messageThreadId: 42,
  topicName: "Сборы",
  needsSelection: false,
  knownTopics: [
    { messageThreadId: 42, name: "Сборы", isClosed: false },
    { messageThreadId: 43, name: "Архив", isClosed: true },
  ],
};

describe("posting topic admin state", () => {
  it("hides the control for non-forum groups", () => {
    expect(shouldShowPostingTopic({ ...forum, isForum: false })).toBe(false);
  });

  it("shows the currently selected topic", () => {
    expect(postingTopicTitle(forum)).toBe("Сборы");
  });

  it("offers only known open topics", () => {
    expect(selectablePostingTopics(forum).map((topic) => topic.messageThreadId)).toEqual([42]);
  });
});
