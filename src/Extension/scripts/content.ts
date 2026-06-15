/**
 * Content script bridge to the in-page Blazor runtime.
 * Forwards Blazor Dev Tools protocol envelopes from window.postMessage to the
 * background service worker.
 */
import { isDevToolsMessage } from "../types/protocol.js";

/**
 * Determines whether a page message event should be forwarded to the extension.
 *
 * @param event - Window message event from the Blazor page runtime.
 * @returns `true` when the event carries a domain protocol envelope from the top-level page.
 */
const isForwardablePageMessage = (event: MessageEvent<unknown>): boolean => {
  // Only accept messages from the top-level page window (not iframes) on the same origin.
  // Known limitation: Blazor hosted inside an iframe is not handled in this phase.
  if (event.source !== window || event.origin !== window.location.origin) {
    return false;
  }

  return isDevToolsMessage(event.data);
};

/**
 * Forwards a validated domain envelope to the background service worker.
 *
 * @param envelope - Domain protocol message from the Blazor page.
 * @returns Nothing.
 */
const forwardToBackground = (envelope: unknown): void => {
  if (!chrome.runtime?.id) {
    return;
  }

  chrome.runtime.sendMessage(envelope).catch(() => {
    // Extension context invalidated after reload; ignore.
  });
};

/**
 * Handles window postMessage events from the Blazor page runtime.
 *
 * @param event - Window message event dispatched by the page.
 * @returns Nothing.
 */
const handlePageMessage = (event: MessageEvent<unknown>): void => {
  if (!isForwardablePageMessage(event)) {
    return;
  }

  forwardToBackground(event.data);
};

window.addEventListener("message", handlePageMessage);
