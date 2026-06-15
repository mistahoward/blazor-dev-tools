/**
 * Dispatches a Blazor Dev Tools protocol envelope to the page window so the
 * extension content script can forward it to the DevTools panel.
 * @param {object} message - DevTools envelope (protocol, version, type, payload).
 */
export function dispatch(message) {
  window.postMessage(message, window.location.origin);
}
