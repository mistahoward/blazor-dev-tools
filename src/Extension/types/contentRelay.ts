/**
 * Wire messages between the background service worker and content script.
 * Separate from panel relay types in relay.ts and domain protocol in protocol.ts.
 */
import { isPickerLocatorEntry, type PickerLocatorEntry } from "./relay.js";

/** Requests the Blazor page runtime refresh its component tree snapshot. */
export interface ContentRefreshMessage {
  /** Discriminator for the content-script refresh request. */
  type: "bdt:requestRefresh";
}

/** Requests the content script highlight or clear a DOM element overlay. */
export interface ContentHighlightMessage {
  /** Discriminator for the content-script highlight control message. */
  type: "bdt:highlight";
  /** CSS selector to highlight, or {@link null} to clear the overlay. */
  selector: string | null;
  /** Optional component display name shown on the overlay label. */
  name?: string;
}

/** Enables or disables the content-script page element picker. */
export interface ContentPickerControlMessage {
  /** Discriminator for the content-script picker control message. */
  type: "bdt:picker";
  /** Whether picker mode should be active. */
  active: boolean;
  /** Component locators for reverse matching. */
  locators: PickerLocatorEntry[];
}

/** Control messages sent from the background worker to the content script. */
export type ContentControlMessage =
  | ContentRefreshMessage
  | ContentHighlightMessage
  | ContentPickerControlMessage;

/** Hover preview event relayed from the content script to the background worker. */
export interface ContentPickerHoverMessage {
  /** Discriminator for content-script picker hover events. */
  type: "bdt:pickerHover";
  /** Matched component id, or {@link null} when none. */
  componentId: string | null;
}

/** Click event relayed from the content script to the background worker. */
export interface ContentPickerClickMessage {
  /** Discriminator for content-script picker click events. */
  type: "bdt:pickerClick";
  /** Matched component id, or {@link null} when none. */
  componentId: string | null;
}

/** Escape event relayed from the content script to the background worker. */
export interface ContentPickerEscapeMessage {
  /** Discriminator for content-script picker escape events. */
  type: "bdt:pickerEscape";
}

/** Picker events sent from the content script to the background worker. */
export type ContentPickerEventMessage =
  | ContentPickerHoverMessage
  | ContentPickerClickMessage
  | ContentPickerEscapeMessage;

/**
 * Determines whether a value is a nullable component id field.
 *
 * @param value - Candidate component id from a picker event.
 * @returns `true` when {@link value} is a string or {@link null}.
 */
const isNullableComponentId = (value: unknown): value is string | null =>
  typeof value === "string" || value === null;

/**
 * Determines whether a value is a content-script refresh request.
 *
 * @param value - Message payload from {@link chrome.runtime.onMessage}.
 * @returns `true` when {@link value} is a {@link ContentRefreshMessage}.
 */
export const isContentRefreshMessage = (
  value: unknown,
): value is ContentRefreshMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  return (value as ContentRefreshMessage).type === "bdt:requestRefresh";
};

/**
 * Determines whether a value is a content-script highlight control message.
 *
 * @param value - Message payload from {@link chrome.runtime.onMessage}.
 * @returns `true` when {@link value} is a {@link ContentHighlightMessage}.
 */
export const isContentHighlightMessage = (
  value: unknown,
): value is ContentHighlightMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as ContentHighlightMessage;
  return (
    message.type === "bdt:highlight" &&
    (typeof message.selector === "string" || message.selector === null) &&
    (message.name === undefined || typeof message.name === "string")
  );
};

/**
 * Determines whether a value is a content-script picker control message.
 *
 * @param value - Message payload from {@link chrome.runtime.onMessage}.
 * @returns `true` when {@link value} is a {@link ContentPickerControlMessage}.
 */
export const isContentPickerControlMessage = (
  value: unknown,
): value is ContentPickerControlMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as ContentPickerControlMessage;
  if (message.type !== "bdt:picker" || typeof message.active !== "boolean") {
    return false;
  }

  return (
    Array.isArray(message.locators) &&
    message.locators.every(isPickerLocatorEntry)
  );
};

/**
 * Determines whether a value is a content-script picker hover event.
 *
 * @param value - Message payload from {@link chrome.runtime.onMessage}.
 * @returns `true` when {@link value} is a {@link ContentPickerHoverMessage}.
 */
export const isContentPickerHoverMessage = (
  value: unknown,
): value is ContentPickerHoverMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as ContentPickerHoverMessage;
  return (
    message.type === "bdt:pickerHover" &&
    isNullableComponentId(message.componentId)
  );
};

/**
 * Determines whether a value is a content-script picker click event.
 *
 * @param value - Message payload from {@link chrome.runtime.onMessage}.
 * @returns `true` when {@link value} is a {@link ContentPickerClickMessage}.
 */
export const isContentPickerClickMessage = (
  value: unknown,
): value is ContentPickerClickMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as ContentPickerClickMessage;
  return (
    message.type === "bdt:pickerClick" &&
    isNullableComponentId(message.componentId)
  );
};

/**
 * Determines whether a value is a content-script picker escape event.
 *
 * @param value - Message payload from {@link chrome.runtime.onMessage}.
 * @returns `true` when {@link value} is a {@link ContentPickerEscapeMessage}.
 */
export const isContentPickerEscapeMessage = (
  value: unknown,
): value is ContentPickerEscapeMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  return (value as ContentPickerEscapeMessage).type === "bdt:pickerEscape";
};
