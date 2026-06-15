/**
 * DevTools panel entry point. Opens a long-lived port to the background
 * service worker and includes the inspected tab id in the handshake.
 */
import { isDevToolsMessage } from "../types/protocol.js";

/** Inspected tab id for this DevTools panel instance. */
const inspectedTabId = chrome.devtools.inspectedWindow.tabId;

/** Long-lived runtime port to the background service worker. */
const port = chrome.runtime.connect({
  name: "blazor-devtools-panel",
});

/**
 * Sends the panel connect handshake to register this tab with the background worker.
 *
 * @returns Nothing.
 */
const sendPanelConnect = (): void => {
  port.postMessage({
    type: "panel:connect",
    tabId: inspectedTabId,
  });
};

/**
 * Handles domain envelopes relayed from the background service worker.
 *
 * @param message - Message payload received on the panel runtime port.
 * @returns Nothing.
 */
const handlePanelMessage = (message: unknown): void => {
  if (!isDevToolsMessage(message)) {
    return;
  }

  console.log("[Blazor Dev Tools] message", message.type, message.payload);
};

/**
 * Handles loss of the background service worker connection.
 *
 * @returns Nothing.
 */
const handlePanelDisconnect = (): void => {
  // TODO: Handle background service worker disconnect / reconnect.
};

port.onMessage.addListener(handlePanelMessage);
port.onDisconnect.addListener(handlePanelDisconnect);
sendPanelConnect();
