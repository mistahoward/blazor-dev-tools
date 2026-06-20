import {
  type DotNetObjectReference,
  isDevToolsRefreshRequest,
} from "./dotnetJsInterop.js";

/** Debounce interval aligned with BlazorDevToolsService._debounceMilliseconds. */
const OBSERVER_DEBOUNCE_MS = 250;

/** Z-index values used by the extension highlight overlay (see highlightOverlay.ts). */
const OVERLAY_Z_INDEX = "2147483646";
const OVERLAY_LABEL_Z_INDEX = "2147483647";

let refreshCallback: (() => void) | null = null;
let dotNetRef: DotNetObjectReference | null = null;
let observer: MutationObserver | null = null;
let debounceTimer: ReturnType<typeof setTimeout> | null = null;

/**
 * Dispatches a Blazor Dev Tools protocol envelope to the page window so the
 * extension content script can forward it to the DevTools panel.
 */
export function dispatch(message: string | object): void {
  let envelope: unknown;
  try {
    envelope = typeof message === "string" ? JSON.parse(message) : message;
  } catch {
    console.debug("[BlazorDevTools] dispatch: invalid JSON");
    return;
  }

  window.postMessage(envelope, window.location.origin);
}

/** Returns whether a DOM node belongs to the extension highlight overlay. */
function isExtensionOverlayNode(node: Node | null): boolean {
  let current: Node | null = node;
  if (current?.nodeType === Node.TEXT_NODE) {
    current = current.parentNode;
  }

  while (current && current !== document.documentElement) {
    if (current instanceof HTMLElement) {
      const { position, zIndex, pointerEvents } = current.style;
      if (
        position === "fixed" &&
        pointerEvents === "none" &&
        (zIndex === OVERLAY_Z_INDEX || zIndex === OVERLAY_LABEL_Z_INDEX)
      ) {
        return true;
      }
    }

    current = current.parentNode;
  }

  return false;
}

/**
 * Schedules a debounced OnRenderActivity call to .NET.
 * Complements DevToolsInitializer per-render refresh for descendant re-renders.
 */
function scheduleRenderActivity(): void {
  if (debounceTimer !== null) {
    clearTimeout(debounceTimer);
  }

  debounceTimer = setTimeout(() => {
    debounceTimer = null;
    if (dotNetRef === null) {
      return;
    }

    dotNetRef
      .invokeMethodAsync<void>("OnRenderActivity")
      .catch((e: unknown) =>
        console.debug("[BlazorDevTools] OnRenderActivity failed", e),
      );
  }, OBSERVER_DEBOUNCE_MS);
}

/** Starts observing DOM mutations that may indicate a descendant component re-render. */
function startObserving(): void {
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
    characterData: true,
  });
}

/** Stops the DOM mutation observer and clears any pending debounce timer. */
export function stopObserving(): void {
  if (debounceTimer !== null) {
    clearTimeout(debounceTimer);
    debounceTimer = null;
  }

  observer?.disconnect();
  observer = null;
}

/**
 * Registers a .NET callback invoked when the DevTools panel requests a tree refresh,
 * and starts a debounced DOM observer for descendant re-render detection.
 */
export function registerRefreshCallback(netRef: DotNetObjectReference): void {
  stopObserving();

  dotNetRef = netRef;
  refreshCallback = () => {
    netRef.invokeMethodAsync<void>("OnRefreshRequested");
  };

  startObserving();
}

window.addEventListener("message", (event: MessageEvent) => {
  if (event.source !== window || event.origin !== window.location.origin) {
    return;
  }

  if (isDevToolsRefreshRequest(event.data)) {
    refreshCallback?.();
  }
});
