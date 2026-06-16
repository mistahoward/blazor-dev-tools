/**
 * Dispatches a Blazor Dev Tools protocol envelope to the page window so the
 * extension content script can forward it to the DevTools panel.
 * @param {object} message - DevTools envelope (protocol, version, type, payload).
 */
export function dispatch(message) {
  window.postMessage(message, window.location.origin);
}

  /** @type {(() => void) | null} */
let refreshCallback = null;

/**
 * Registers a .NET callback invoked when the DevTools panel requests a tree refresh.
 * @param {object} dotNetRef - .NET object reference with an OnRefreshRequested method.
 */
export function registerRefreshCallback(dotNetRef) {
  refreshCallback = () => {
    dotNetRef.invokeMethodAsync("OnRefreshRequested");
  };
}

window.addEventListener("message", (event) => {
  if (event.source !== window || event.origin !== window.location.origin) {
    return;
  }

  const data = event.data;
  if (
    typeof data === "object" &&
    data !== null &&
    data.type === "blazorDevTools:requestRefresh"
  ) {
    refreshCallback?.();
  }
});
