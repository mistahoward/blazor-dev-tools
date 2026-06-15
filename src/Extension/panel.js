/**
 * DevTools panel entry point. Opens a long-lived port to the background
 * service worker and includes the inspected tab id in the handshake.
 */
const inspectedTabId = chrome.devtools.inspectedWindow.tabId;

const port = chrome.runtime.connect({
  name: "blazor-devtools-panel",
});

port.postMessage({
  type: "panel:connect",
  tabId: inspectedTabId,
});

port.onDisconnect.addListener(() => {
  // TODO: Handle background service worker disconnect / reconnect.
});

port.onMessage.addListener(() => {
  // TODO: Handle messages from the background relay.
});
