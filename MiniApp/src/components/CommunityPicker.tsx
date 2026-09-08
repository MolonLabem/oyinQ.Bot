import { useState, type KeyboardEvent } from "react";
import type { Community } from "../api/types";
import { CommunityAvatar } from "./CommunityAvatar";

export type CommunityPickerItem = Pick<Community, "key" | "name" | "mode" | "avatarUrl"> & { statusLabel?: string };

// Shared by the main community screen and the persistent admin switcher.
export function CommunityPicker({ communities, choose, selectedKey, id, className = "", onKeyDown }: {
  communities: CommunityPickerItem[]; choose: (key: string) => void; selectedKey?: string;
  id?: string; className?: string; onKeyDown?: (event: KeyboardEvent<HTMLDivElement>) => void;
}) {
  const [focusedKey, setFocusedKey] = useState<string>();
  const tabKey = communities.some(item => item.key === focusedKey) ? focusedKey : selectedKey ?? communities[0]?.key;
  function navigate(event: KeyboardEvent<HTMLDivElement>) {
    onKeyDown?.(event);
    if (event.defaultPrevented || !["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="option"]'));
    const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const next = event.key === "Home" ? 0 : event.key === "End" ? buttons.length - 1
      : (index + (event.key === "ArrowDown" ? 1 : -1) + buttons.length) % buttons.length;
    buttons[next]?.focus();
  }
  return <div id={id} className={`stack community-picker ${className}`} role="listbox" aria-label="Выберите сообщество" onKeyDown={navigate}>
    {communities.map(community => <button type="button" className="card community-option" key={community.key}
      role="option" aria-selected={community.key === selectedKey} tabIndex={community.key === tabKey ? 0 : -1}
      value={community.key} onFocus={() => setFocusedKey(community.key)} onClick={() => choose(community.key)}>
      <CommunityAvatar community={community} />
      <span className="community-option-label"><strong>{community.name}</strong>
        <small>{community.mode === "Club" ? "Клуб" : "Кэмп"}{community.statusLabel && ` · ${community.statusLabel}`}</small>
      </span>
      {community.key === selectedKey && <span className="community-option-check" aria-hidden>✓</span>}
    </button>)}
  </div>;
}
