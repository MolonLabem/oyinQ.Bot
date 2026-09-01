import type { PostingTopicSettings } from "../../api/types";

export const shouldShowPostingTopic = (settings?: PostingTopicSettings) => settings?.isForum === true;

export const postingTopicTitle = (settings: PostingTopicSettings) =>
  settings.messageThreadId === null ? "Основная тема" : settings.topicName ?? `Тема #${settings.messageThreadId}`;

export const selectablePostingTopics = (settings: PostingTopicSettings) =>
  settings.knownTopics.filter((topic) => !topic.isClosed);
