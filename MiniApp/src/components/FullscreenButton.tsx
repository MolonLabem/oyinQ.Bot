import { useEffect, useState } from "react";
import { telegram } from "../telegram/webApp";
import { fullscreenLabel } from "../pages/camp/registrationLogic";

export function FullscreenButton() {
  const [fullscreen, setFullscreen] = useState(telegram.isFullscreen);
  useEffect(() => telegram.onFullscreenChanged?.(setFullscreen), []);
  if (!telegram.canFullscreen) return null;
  const label = fullscreenLabel(fullscreen);
  return <button type="button" className="fullscreen-action" aria-label={label} title={label}
    aria-pressed={fullscreen} onClick={() => fullscreen ? telegram.exitFullscreen() : void telegram.requestFullscreen()}>
    <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d={fullscreen
      ? "M9 4v5H4m11-5v5h5M9 20v-5H4m11 5v-5h5"
      : "M8 3H3v5m13-5h5v5M8 21H3v-5m13 5h5v-5"} /></svg>
  </button>;
}
