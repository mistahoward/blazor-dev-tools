/**
 * Page element picker running in the content script (shared DOM with the inspected page).
 */
import type { PickerLocatorEntry } from "../types/relay.js";
import type {
  ContentPickerClickMessage,
  ContentPickerEscapeMessage,
  ContentPickerHoverMessage,
} from "../types/contentRelay.js";

/** Minimum milliseconds between hover relay messages. */
const HOVER_THROTTLE_MS = 50;

/** Whether picker mode is active in this tab. */
let pickerActive = false;

/** Locator table supplied by the DevTools panel. */
let pickerLocators: PickerLocatorEntry[] = [];

/** Timestamp of the last hover message sent to the background worker. */
let lastHoverSentAt = 0;

/** Last component id sent for hover preview deduplication. */
let lastHoverComponentId: string | null = null;

/**
 * Finds the deepest component whose located element matches an ancestor of the target.
 *
 * @param target - DOM element under the pointer.
 * @returns Matched component id, if any.
 */
const findComponentForElement = (target: Element): string | null => {
  let bestId: string | null = null;
  let bestDepth = -1;

  for (const entry of pickerLocators) {
    if (!entry.selector)
      continue;

    let match: Element | null = null;
    try {
      match = document.querySelector(entry.selector);
    } catch {
      match = null;
    }

    if (
      match &&
      (match === target || match.contains(target)) &&
      entry.depth > bestDepth
    ) {
      bestId = entry.id;
      bestDepth = entry.depth;
    }
  }

  return bestId;
};

/**
 * Sends a throttled hover preview event to the DevTools panel.
 *
 * @param componentId - Matched component id, or {@link null} when none.
 * @returns Nothing.
 */
const sendHover = (componentId: string | null): void => {
  const now = Date.now();
  if (
    componentId === lastHoverComponentId &&
    now - lastHoverSentAt < HOVER_THROTTLE_MS
  ) {
    return;
  }

  lastHoverComponentId = componentId;
  lastHoverSentAt = now;

  const hoverMessage: ContentPickerHoverMessage = {
    type: "bdt:pickerHover",
    componentId,
  };
  chrome.runtime
    .sendMessage(hoverMessage)
    .catch(() => {
      // Extension context invalidated after reload; ignore.
    });
};

/**
 * Sends a pick (click) event to the DevTools panel.
 *
 * @param componentId - Matched component id, or {@link null} when none.
 * @returns Nothing.
 */
const sendPick = (componentId: string | null): void => {
  const clickMessage: ContentPickerClickMessage = {
    type: "bdt:pickerClick",
    componentId,
  };
  chrome.runtime
    .sendMessage(clickMessage)
    .catch(() => {
      // Extension context invalidated after reload; ignore.
    });
};

/**
 * Handles pointer movement while picker mode is active.
 *
 * @param event - Mouse move event from the page.
 * @returns Nothing.
 */
const handleMouseMove = (event: MouseEvent): void => {
  if (!pickerActive)
    return;

  const target = document.elementFromPoint(event.clientX, event.clientY);
  if (!target) {
    sendHover(null);
    return;
  }

  sendHover(findComponentForElement(target));
};

/**
 * Handles pointer press while picker mode is active.
 *
 * @param event - Mouse down event from the page.
 * @returns Nothing.
 */
const handleMouseDown = (event: MouseEvent): void => {
  if (!pickerActive)
    return;

  event.preventDefault();
  event.stopPropagation();
  event.stopImmediatePropagation();

  const target = document.elementFromPoint(event.clientX, event.clientY);
  sendPick(target ? findComponentForElement(target) : null);
};

/**
 * Handles Escape while picker mode is active.
 *
 * @param event - Keyboard event from the page.
 * @returns Nothing.
 */
const handleKeyDown = (event: KeyboardEvent): void => {
  if (!pickerActive || event.key !== "Escape")
    return;

  event.preventDefault();
  event.stopPropagation();

  const escapeMessage: ContentPickerEscapeMessage = { type: "bdt:pickerEscape" };
  chrome.runtime
    .sendMessage(escapeMessage)
    .catch(() => {
      // Extension context invalidated after reload; ignore.
    });
};

/** Whether document-level picker listeners are attached. */
let listenersAttached = false;

/**
 * Attaches document-level picker listeners once per content script lifetime.
 *
 * @returns Nothing.
 */
const ensureListeners = (): void => {
  if (listenersAttached)
    return;

  document.addEventListener("mousemove", handleMouseMove, true);
  document.addEventListener("mousedown", handleMouseDown, true);
  document.addEventListener("keydown", handleKeyDown, true);
  listenersAttached = true;
};

/**
 * Enables or disables page element picker mode in the content script.
 *
 * @param active - Whether picker mode should be active.
 * @param locators - Component locators for reverse matching.
 * @returns Nothing.
 */
export const setContentPickerActive = (
  active: boolean,
  locators: PickerLocatorEntry[],
): void => {
  ensureListeners();

  pickerActive = active;
  pickerLocators = locators;
  lastHoverComponentId = null;
  lastHoverSentAt = 0;

  if (document.body)
    document.body.style.cursor = active ? "crosshair" : "";
};
