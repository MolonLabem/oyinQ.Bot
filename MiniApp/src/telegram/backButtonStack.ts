type BackButton = { show: () => void; hide: () => void; onClick: (handler: () => void) => void; offClick: (handler: () => void) => void };

// Telegram invokes every registered listener. A modal must consume Back itself,
// then restore the screen handler instead of navigating both levels at once.
export function backButtonStack(button?: BackButton) {
  type Frame = { show: boolean; handler: () => void; priority: number };
  const frames: Frame[] = [];
  const current = () => frames.reduce<Frame | undefined>((best, frame) => !best || frame.priority >= best.priority ? frame : best, undefined);
  const dispatch = () => { const frame = current(); if (frame?.show) frame.handler(); };
  const update = () => { if (current()?.show) button?.show(); else button?.hide(); };
  return (show: boolean, handler: () => void, priority = 0) => {
    if (!button) return () => {};
    const frame = { show, handler, priority };
    if (!frames.length) button.onClick(dispatch);
    frames.push(frame); update();
    return () => {
      const index = frames.indexOf(frame);
      if (index < 0) return;
      frames.splice(index, 1); update();
      if (!frames.length) button.offClick(dispatch);
    };
  };
}
