/**
 * Chrome-internal relay messages between the DevTools panel and background worker.
 * Separate from domain protocol envelopes in protocol.ts.
 */

/** Handshake sent when the DevTools panel connects to the background worker. */
export interface PanelConnectMessage {
  /** Discriminator for the panel connect handshake. */
  type: "panel:connect";
  /** Inspected tab identifier from {@link chrome.devtools.inspectedWindow.tabId}. */
  tabId: number;
}

/** Requests the content script highlight or clear a DOM element in the inspected tab. */
export interface PanelHighlightMessage {
  /** Discriminator for the panel highlight control message. */
  type: "panel:highlight";
  /** Inspected tab identifier from {@link chrome.devtools.inspectedWindow.tabId}. */
  tabId: number;
  /** CSS selector to highlight, or {@link null} to clear the overlay. */
  selector: string | null;
  /** Optional component display name shown on the overlay label. */
  name?: string;
}

/** Locator entry used by the page element picker for reverse matching. */
export interface PickerLocatorEntry {
  /** Component id from the tree payload. */
  id: string;
  /** CSS selector for the component's first rendered element. */
  selector: string;
  /** Tree depth used to prefer the most specific match. */
  depth: number;
}

/**
 * Determines whether a value is a picker locator entry.
 *
 * @param value - Candidate locator entry from a control message.
 * @returns `true` when {@link value} is a {@link PickerLocatorEntry}.
 */
export const isPickerLocatorEntry = (
  value: unknown,
): value is PickerLocatorEntry => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const entry = value as PickerLocatorEntry;
  return (
    typeof entry.id === "string" &&
    typeof entry.selector === "string" &&
    typeof entry.depth === "number"
  );
};

/** Enables or disables the content-script page element picker. */
export interface PanelPickerControlMessage {
  /** Discriminator for the panel picker control message. */
  type: "panel:picker";
  /** Inspected tab identifier from {@link chrome.devtools.inspectedWindow.tabId}. */
  tabId: number;
  /** Whether picker mode should be active. */
  active: boolean;
  /** Component locators for reverse matching. */
  locators: PickerLocatorEntry[];
}

/** Hover preview relay from the content script to the DevTools panel. */
export interface PickerHoverRelayMessage {
  /** Discriminator for picker hover relay messages. */
  type: "picker:hover";
  /** Matched component id, or {@link null} when none. */
  componentId: string | null;
}

/** Click relay from the content script to the DevTools panel. */
export interface PickerClickRelayMessage {
  /** Discriminator for picker click relay messages. */
  type: "picker:click";
  /** Matched component id, or {@link null} when none. */
  componentId: string | null;
}

/** Escape relay from the content script to the DevTools panel. */
export interface PickerEscapeRelayMessage {
  /** Discriminator for picker escape relay messages. */
  type: "picker:escape";
}

/**
 * Determines whether a value is a panel connect handshake message.
 *
 * @param value - The value received on a DevTools panel port.
 * @returns `true` when {@link value} is a {@link PanelConnectMessage}.
 */
export const isPanelConnectMessage = (
  value: unknown,
): value is PanelConnectMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as PanelConnectMessage;
  return message.type === "panel:connect" && typeof message.tabId === "number";
};

/**
 * Determines whether a value is a panel highlight control message.
 *
 * @param value - The value received on a DevTools panel port.
 * @returns `true` when {@link value} is a {@link PanelHighlightMessage}.
 */
export const isPanelHighlightMessage = (
  value: unknown,
): value is PanelHighlightMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as PanelHighlightMessage;
  return (
    message.type === "panel:highlight" &&
    typeof message.tabId === "number" &&
    (typeof message.selector === "string" || message.selector === null) &&
    (message.name === undefined || typeof message.name === "string")
  );
};

/**
 * Determines whether a value is a panel picker control message.
 *
 * @param value - The value received on a DevTools panel port.
 * @returns `true` when {@link value} is a {@link PanelPickerControlMessage}.
 */
export const isPanelPickerControlMessage = (
  value: unknown,
): value is PanelPickerControlMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as PanelPickerControlMessage;
  if (message.type !== "panel:picker" || typeof message.tabId !== "number") {
    return false;
  }

  if (typeof message.active !== "boolean" || !Array.isArray(message.locators)) {
    return false;
  }

  return message.locators.every(isPickerLocatorEntry);
};

/**
 * Determines whether a value is a picker hover relay message.
 *
 * @param value - The value received on a DevTools panel port.
 * @returns `true` when {@link value} is a {@link PickerHoverRelayMessage}.
 */
export const isPickerHoverRelayMessage = (
  value: unknown,
): value is PickerHoverRelayMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as PickerHoverRelayMessage;
  return (
    message.type === "picker:hover" &&
    (typeof message.componentId === "string" || message.componentId === null)
  );
};

/**
 * Determines whether a value is a picker click relay message.
 *
 * @param value - The value received on a DevTools panel port.
 * @returns `true` when {@link value} is a {@link PickerClickRelayMessage}.
 */
export const isPickerClickRelayMessage = (
  value: unknown,
): value is PickerClickRelayMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as PickerClickRelayMessage;
  return (
    message.type === "picker:click" &&
    (typeof message.componentId === "string" || message.componentId === null)
  );
};

/**
 * Determines whether a value is a picker escape relay message.
 *
 * @param value - The value received on a DevTools panel port.
 * @returns `true` when {@link value} is a {@link PickerEscapeRelayMessage}.
 */
export const isPickerEscapeRelayMessage = (
  value: unknown,
): value is PickerEscapeRelayMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  return (value as PickerEscapeRelayMessage).type === "picker:escape";
};
