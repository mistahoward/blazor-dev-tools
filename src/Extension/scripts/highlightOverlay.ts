/**
 * Draws a DevTools-style highlight overlay over a DOM element in the inspected page.
 */

/** Overlay box drawn over the highlighted element. */
let overlayEl: HTMLDivElement | null = null;

/** Label showing the selected component name. */
let labelEl: HTMLDivElement | null = null;

/** CSS selector for the currently highlighted element. */
let activeSelector: string | null = null;

/** Optional component name shown on the overlay label. */
let activeName: string | undefined;

/** Whether a reposition animation frame is already scheduled. */
let repositionScheduled = false;

/** Observes size changes on the highlighted target element. */
let resizeObserver: ResizeObserver | null = null;

/**
 * Applies or clears the highlight overlay for the given CSS selector.
 *
 * @param selector - CSS selector to highlight, or {@link null} to clear.
 * @param name - Optional component display name for the overlay label.
 * @returns Nothing.
 */
export const applyHighlight = (
  selector: string | null,
  name?: string,
): void => {
  if (selector === null) {
    clearHighlight();
    return;
  }

  activeSelector = selector;
  activeName = name;
  ensureOverlayElements();
  attachRepositionListeners();
  scheduleReposition();
};

/**
 * Removes the highlight overlay and detaches reposition listeners.
 *
 * @returns Nothing.
 */
export const clearHighlight = (): void => {
  activeSelector = null;
  activeName = undefined;
  repositionScheduled = false;

  resizeObserver?.disconnect();
  resizeObserver = null;

  document.removeEventListener("scroll", handleRepositionEvent, true);
  window.removeEventListener("resize", handleRepositionEvent);

  overlayEl?.remove();
  labelEl?.remove();
  overlayEl = null;
  labelEl = null;
};

/**
 * Creates overlay DOM nodes when they do not yet exist.
 *
 * @returns Nothing.
 */
const ensureOverlayElements = (): void => {
  if (overlayEl !== null && labelEl !== null) {
    return;
  }

  overlayEl = document.createElement("div");
  overlayEl.style.position = "fixed";
  overlayEl.style.pointerEvents = "none";
  overlayEl.style.boxSizing = "border-box";
  overlayEl.style.border = "2px solid #1a73e8";
  overlayEl.style.backgroundColor = "color-mix(in srgb, #1a73e8 12%, transparent)";
  overlayEl.style.zIndex = "2147483646";

  labelEl = document.createElement("div");
  labelEl.style.position = "fixed";
  labelEl.style.pointerEvents = "none";
  labelEl.style.boxSizing = "border-box";
  labelEl.style.padding = "2px 6px";
  labelEl.style.font = "11px/1.4 sans-serif";
  labelEl.style.color = "#ffffff";
  labelEl.style.backgroundColor = "#1a73e8";
  labelEl.style.borderRadius = "2px";
  labelEl.style.zIndex = "2147483647";
  labelEl.hidden = true;

  document.body.append(overlayEl, labelEl);
};

/**
 * Attaches listeners used to keep the overlay aligned with the target element.
 *
 * @returns Nothing.
 */
const attachRepositionListeners = (): void => {
  document.removeEventListener("scroll", handleRepositionEvent, true);
  window.removeEventListener("resize", handleRepositionEvent);
  document.addEventListener("scroll", handleRepositionEvent, {
    capture: true,
    passive: true,
  });
  window.addEventListener("resize", handleRepositionEvent, { passive: true });
};

/**
 * Schedules a single animation-frame reposition pass.
 *
 * @returns Nothing.
 */
const scheduleReposition = (): void => {
  if (repositionScheduled) {
    return;
  }

  repositionScheduled = true;
  window.requestAnimationFrame(() => {
    repositionScheduled = false;
    repositionOverlay();
  });
};

/**
 * Repositions the overlay from the current selector and target element rect.
 *
 * @returns Nothing.
 */
const repositionOverlay = (): void => {
  if (activeSelector === null || overlayEl === null || labelEl === null) {
    return;
  }

  const target = (() => {
    try {
      return document.querySelector(activeSelector);
    } catch {
      return null;
    }
  })();
  if (!(target instanceof Element) || !target.isConnected) {
    clearHighlight();
    return;
  }

  observeTarget(target);

  const rect = target.getBoundingClientRect();
  if (rect.width <= 0 && rect.height <= 0) {
    overlayEl.style.display = "none";
    labelEl.hidden = true;
    return;
  }

  overlayEl.style.display = "block";
  overlayEl.style.top = `${rect.top}px`;
  overlayEl.style.left = `${rect.left}px`;
  overlayEl.style.width = `${rect.width}px`;
  overlayEl.style.height = `${rect.height}px`;

  if (activeName) {
    labelEl.textContent = activeName;
    labelEl.hidden = false;
    labelEl.style.top = `${Math.max(0, rect.top - 20)}px`;
    labelEl.style.left = `${rect.left}px`;
  } else {
    labelEl.hidden = true;
  }
};

/**
 * Observes the highlighted target for size changes.
 *
 * @param target - Element whose bounds should be tracked.
 * @returns Nothing.
 */
const observeTarget = (target: Element): void => {
  resizeObserver ??= new ResizeObserver(() => {
    scheduleReposition();
  });

  resizeObserver.disconnect();
  resizeObserver.observe(target);
};

/**
 * Handles scroll and resize events by scheduling an overlay reposition.
 *
 * @returns Nothing.
 */
const handleRepositionEvent = (): void => {
  scheduleReposition();
};
