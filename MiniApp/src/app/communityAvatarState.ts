export const shouldShowCommunityPhoto = (avatarUrl?: string, failed = false) =>
  Boolean(avatarUrl) && !failed;
