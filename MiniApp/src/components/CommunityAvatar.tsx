import { useEffect, useState } from "react";
import type { Community } from "../api/types";
import { shouldShowCommunityPhoto } from "../app/communityAvatarState";

export function CommunityAvatar({ community }: { community: Pick<Community, "mode" | "avatarUrl"> }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [community.avatarUrl]);
  return shouldShowCommunityPhoto(community.avatarUrl, failed)
    ? <img className="community-avatar" src={community.avatarUrl} alt="" onError={() => setFailed(true)} />
    : <span className={`mode-icon ${community.mode.toLowerCase()}`}>{community.mode === "Club" ? "♣" : "⛺"}</span>;
}
