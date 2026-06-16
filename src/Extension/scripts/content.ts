/**
 * Content script bridge to the in-page Blazor runtime.
 * Forwards Blazor Dev Tools protocol envelopes from window.postMessage to the
 * background service worker.
 */
import { isDevToolsMessage } from "../types/protocol.js";
import { applyHighlight } from "./highlightOverlay.js";
import { setContentPickerActive } from "./pagePicker.js";
import type { ContentPickerLocator } from "./pagePicker.js";

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

/**
 * Handles extension requests to ask the Blazor page for a fresh tree snapshot.
 *
 * @param message - Message from the background service worker.
 * @returns `false` because no asynchronous response is sent.
 */
const handleExtensionMessage = (message: unknown): boolean => {
  if (typeof message !== "object" || message === null) {
    return false;
  }

  const typedMessage = message as {
    type?: string;
    selector?: unknown;
    name?: unknown;
    active?: unknown;
    locators?: unknown;
  };

  if (typedMessage.type === "bdt:requestRefresh") {
    window.postMessage(
      { type: "blazorDevTools:requestRefresh" },
      window.location.origin,
    );
    return false;
  }

  if (typedMessage.type === "bdt:highlight") {
    const selector =
      typeof typedMessage.selector === "string" ? typedMessage.selector : null;
    const name =
      typeof typedMessage.name === "string" ? typedMessage.name : undefined;
    applyHighlight(selector, name);
    return false;
  }

  if (typedMessage.type === "bdt:picker") {
    const active = typedMessage.active === true;
    const locators = Array.isArray(typedMessage.locators)
      ? typedMessage.locators.filter(isContentPickerLocator)
      : [];
    setContentPickerActive(active, locators);
    return false;
  }

  return false;
};

/**
 * Determines whether a value is a valid content-script picker locator entry.
 *
 * @param value - Candidate locator entry from a control message.
 * @returns `true` when {@link value} has the expected shape.
 */
const isContentPickerLocator = (
  value: unknown,
): value is ContentPickerLocator => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const entry = value as ContentPickerLocator;
  return (
    typeof entry.id === "string" &&
    typeof entry.selector === "string" &&
    typeof entry.depth === "number"
  );
};

chrome.runtime.onMessage.addListener(handleExtensionMessage);
