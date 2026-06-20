// scripts/dotnetJsInterop.ts
function isDevToolsRefreshRequest(data) {
  return typeof data === "object" && data !== null && data.type === "blazorDevTools:requestRefresh";
}

// scripts/blazorDevToolsInterop.ts
var OBSERVER_DEBOUNCE_MS = 250;
var OVERLAY_Z_INDEX = "2147483646";
var OVERLAY_LABEL_Z_INDEX = "2147483647";
var refreshCallback = null;
var dotNetRef = null;
var observer = null;
var debounceTimer = null;
function dispatch(message) {
  let envelope;
  try {
    envelope = typeof message === "string" ? JSON.parse(message) : message;
  } catch {
    console.debug("[BlazorDevTools] dispatch: invalid JSON");
    return;
  }
  window.postMessage(envelope, window.location.origin);
}
function isExtensionOverlayNode(node) {
  let current = node;
  if (current?.nodeType === Node.TEXT_NODE) {
    current = current.parentNode;
  }
  while (current && current !== document.documentElement) {
    if (current instanceof HTMLElement) {
      const { position, zIndex, pointerEvents } = current.style;
      if (position === "fixed" && pointerEvents === "none" && (zIndex === OVERLAY_Z_INDEX || zIndex === OVERLAY_LABEL_Z_INDEX)) {
        return true;
      }
    }
    current = current.parentNode;
  }
  return false;
}
function scheduleRenderActivity() {
  if (debounceTimer !== null) {
    clearTimeout(debounceTimer);
  }
  debounceTimer = setTimeout(() => {
    debounceTimer = null;
    if (dotNetRef === null) {
      return;
    }
    dotNetRef.invokeMethodAsync("OnRenderActivity").catch(
      (e) => console.debug("[BlazorDevTools] OnRenderActivity failed", e)
    );
  }, OBSERVER_DEBOUNCE_MS);
}
function startObserving() {
  const root = document.documentElement;
  if (!root || observer !== null) {
    return;
  }
  observer = new MutationObserver((mutations) => {
    for (const mutation of mutations) {
      if (isExtensionOverlayNode(mutation.target)) {
        continue;
      }
      scheduleRenderActivity();
      return;
    }
  });
  observer.observe(root, {
    childList: true,
    subtree: true,
    characterData: true
  });
}
function stopObserving() {
  if (debounceTimer !== null) {
    clearTimeout(debounceTimer);
    debounceTimer = null;
  }
  observer?.disconnect();
  observer = null;
}
function registerRefreshCallback(netRef) {
  stopObserving();
  dotNetRef = netRef;
  refreshCallback = () => {
    netRef.invokeMethodAsync("OnRefreshRequested");
  };
  startObserving();
}
window.addEventListener("message", (event) => {
  if (event.source !== window || event.origin !== window.location.origin) {
    return;
  }
  if (isDevToolsRefreshRequest(event.data)) {
    refreshCallback?.();
  }
});
export {
  dispatch,
  registerRefreshCallback,
  stopObserving
};
