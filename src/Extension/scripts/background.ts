/**
 * Background service worker relay between the DevTools panel and content script.
 */
import { isDevToolsMessage, type DevToolsMessage } from "../types/protocol.js";
import { isPanelConnectMessage } from "../types/relay.js";

/** Long-lived DevTools panel ports keyed by inspected tab id. */
const panelPortsByTabId = new Map<number, chrome.runtime.Port>();

/** Last domain envelope per tab, buffered until a panel connects. */
const lastEnvelopeByTabId = new Map<number, DevToolsMessage>();

/**
 * Relays a domain message to the DevTools panel port for the given tab.
 * Buffers the message when no panel is connected yet.
 *
 * @param tabId - Inspected tab identifier from the content script sender.
 * @param message - Domain protocol envelope to forward to the panel.
 * @returns Nothing.
 */
const relayToPanel = (tabId: number, message: DevToolsMessage): void => {
  const port = panelPortsByTabId.get(tabId);
  if (!port) {
    lastEnvelopeByTabId.set(tabId, message);
    console.debug(
      "[Blazor Dev Tools] Buffered message for tab",
      tabId,
      "(panel not connected yet)",
    );
    return;
  }

  try {
    port.postMessage(message);
  } catch (error) {
    console.debug(
      "[Blazor Dev Tools] Failed to post to panel for tab",
      tabId,
      error,
    );
    panelPortsByTabId.delete(tabId);
  }
};

/**
 * Sends a previously buffered envelope to a newly connected panel port.
 *
 * @param tabId - Inspected tab identifier that just registered a panel port.
 * @param port - DevTools panel port to receive the buffered message.
 * @returns Nothing.
 */
const flushBufferedEnvelope = (
  tabId: number,
  port: chrome.runtime.Port,
): void => {
  const buffered = lastEnvelopeByTabId.get(tabId);
  if (!buffered) {
    return;
  }

  try {
    port.postMessage(buffered);
    lastEnvelopeByTabId.delete(tabId);
    console.debug("[Blazor Dev Tools] Flushed buffered message for tab", tabId);
  } catch (error) {
    console.debug(
      "[Blazor Dev Tools] Failed to flush buffered message for tab",
      tabId,
      error,
    );
  }
};

/**
 * Handles an incoming long-lived connection from the DevTools panel.
 *
 * @param port - Runtime port opened by {@link chrome.runtime.connect}.
 * @returns Nothing.
 */
const handlePanelConnect = (port: chrome.runtime.Port): void => {
  if (port.name !== "blazor-devtools-panel") {
    return;
  }

  let tabId: number | undefined;

  /**
   * Processes messages sent from the DevTools panel over its runtime port.
   *
   * @param message - Panel handshake or future panel-to-page relay payload.
   * @returns Nothing.
   */
  const onPanelMessage = (message: unknown): void => {
    if (isPanelConnectMessage(message)) {
      tabId = message.tabId;
      panelPortsByTabId.set(tabId, port);
      flushBufferedEnvelope(tabId, port);
      return;
    }

    // TODO: Relay panel messages to the content script for tabId.
  };

  /**
   * Cleans up panel port registration when the DevTools panel disconnects.
   *
   * @returns Nothing.
   */
  const onPanelDisconnect = (): void => {
    if (typeof tabId === "number") {
      panelPortsByTabId.delete(tabId);
    }
  };

  port.onMessage.addListener(onPanelMessage);
  port.onDisconnect.addListener(onPanelDisconnect);
};

/**
 * Handles domain envelopes forwarded from the content script.
 *
 * @param message - Message payload from {@link chrome.runtime.sendMessage}.
 * @param sender - Extension message sender metadata, including the tab id.
 * @returns `false` because no asynchronous response is sent to the caller.
 */
const handleContentScriptMessage = (
  message: unknown,
  sender: chrome.runtime.MessageSender,
): boolean => {
  const tabId = sender.tab?.id;
  if (typeof tabId !== "number" || !isDevToolsMessage(message)) {
    return false;
  }

  relayToPanel(tabId, message);
  return false;
};

chrome.runtime.onConnect.addListener(handlePanelConnect);
chrome.runtime.onMessage.addListener(handleContentScriptMessage);
