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
