export interface MiniAppLaunchContext {
  communityKey?: string;
  gatheringId?: string;
}

export function miniAppLaunchContext(search: string, startParameter?: string): MiniAppLaunchContext {
  const query = new URLSearchParams(search);
  const direct = startParameterContext(startParameter);
  return {
    communityKey: nonEmpty(query.get("community")) ?? direct.communityKey,
    gatheringId: nonEmpty(query.get("gathering")) ?? direct.gatheringId
  };
}

function startParameterContext(parameter?: string): MiniAppLaunchContext {
  if (parameter?.startsWith("community-")) {
    return { communityKey: nonEmpty(parameter.slice("community-".length)) };
  }

  const match = /^g-([A-Za-z0-9_-]{22})-(.+)$/.exec(parameter ?? "");
  if (!match) return {};

  try {
    const bytes = Uint8Array.from(
      atob(match[1].replaceAll("-", "+").replaceAll("_", "/") + "=="),
      character => character.charCodeAt(0));
    if (bytes.length !== 16) return {};
    return { communityKey: match[2], gatheringId: dotNetGuid(bytes) };
  } catch {
    return {};
  }
}

function dotNetGuid(bytes: Uint8Array): string {
  const order = [3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15];
  const hex = order.map(index => bytes[index].toString(16).padStart(2, "0"));
  return `${hex.slice(0, 4).join("")}-${hex.slice(4, 6).join("")}-${hex.slice(6, 8).join("")}-${hex.slice(8, 10).join("")}-${hex.slice(10).join("")}`;
}

function nonEmpty(value: string | null): string | undefined {
  return value?.trim() || undefined;
}
