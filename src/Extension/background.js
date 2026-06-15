/**
 * Background service worker relay between the DevTools panel and content script.
 * Scaffolding only — no protocol handling yet.
 */
/** @type {Map<number, chrome.runtime.Port>} */
const panelPortsByTabId = new Map();

chrome.runtime.onConnect.addListener((port) => {
  if (port.name !== "blazor-devtools-panel") {
    return;
  }

  let tabId;

  port.onMessage.addListener((message) => {
    if (message?.type === "panel:connect" && typeof message.tabId === "number") {
      tabId = message.tabId;
      panelPortsByTabId.set(tabId, port);
      return;
    }

    // TODO: Relay panel messages to the content script for tabId.
  });

  port.onDisconnect.addListener(() => {
    if (typeof tabId === "number") {
      panelPortsByTabId.delete(tabId);
    }
  });
});

chrome.runtime.onMessage.addListener(() => {
  // TODO: Relay content-script messages back to the panel port.
  return false;
});
