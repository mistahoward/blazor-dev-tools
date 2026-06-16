/**
 * Background service worker relay between the DevTools panel and content script.
 */
import { isDevToolsMessage, type DevToolsMessage } from "../types/protocol.js";
import {
  type ContentHighlightMessage,
  type ContentPickerControlMessage,
  type ContentRefreshMessage,
  isContentPickerClickMessage,
  isContentPickerEscapeMessage,
  isContentPickerHoverMessage,
} from "../types/contentRelay.js";
import {
  isPanelConnectMessage,
  isPanelHighlightMessage,
  isPanelPickerControlMessage,
  type PickerClickRelayMessage,
  type PickerEscapeRelayMessage,
  type PickerHoverRelayMessage,
} from "../types/relay.js";

/** Long-lived DevTools panel ports keyed by inspected tab id. */
const panelPortsByTabId = new Map<number, chrome.runtime.Port>();

/** Last domain envelope per tab, buffered until a panel connects. */
const lastEnvelopeByTabId = new Map<number, DevToolsMessage>();

/**
 * Builds the session storage key for a tab's last envelope.
 *
 * @param tabId - Inspected tab identifier.
 * @returns Storage key string.
 */
const sessionStorageKey = (tabId: number): string => `bdt:lastEnvelope:${tabId}`;

/**
 * Clears the buffered envelope for a tab (in-memory and session storage).
 *
 * @param tabId - Inspected tab identifier.
 * @returns Nothing.
 */
const clearBufferedEnvelope = async (tabId: number): Promise<void> => {
  lastEnvelopeByTabId.delete(tabId);

  try {
    await chrome.storage?.session?.remove(sessionStorageKey(tabId));
  } catch {
    // Session storage unavailable; in-memory buffer still cleared.
  }
};

/**
 * Persists the latest envelope for a tab so it survives service worker restarts.
 *
 * @param tabId - Inspected tab identifier.
 * @param message - Domain envelope to persist.
 * @returns Nothing.
 */
const persistEnvelope = (tabId: number, message: DevToolsMessage): void => {
  lastEnvelopeByTabId.set(tabId, message);

  // Session storage is best-effort; missing permission or API must never break relay.
  try {
    chrome.storage?.session
      ?.set({ [sessionStorageKey(tabId)]: message })
      .catch(() => {
        // Session storage unavailable; in-memory buffer still applies for this SW lifetime.
      });
  } catch {
    // chrome.storage unavailable in this context; in-memory buffer still applies.
  }
};

/**
 * Loads a persisted envelope for a tab from session storage.
 *
 * @param tabId - Inspected tab identifier.
 * @returns The persisted envelope, if any.
 */
const loadPersistedEnvelope = async (
  tabId: number,
): Promise<DevToolsMessage | undefined> => {
  try {
    const result = await chrome.storage?.session?.get(sessionStorageKey(tabId));
    const message = result?.[sessionStorageKey(tabId)];
    return isDevToolsMessage(message) ? message : undefined;
  } catch {
    return undefined;
  }
};

/**
 * Relays a domain message to the DevTools panel port for the given tab.
 * Buffers the message when no panel is connected yet.
 *
 * @param tabId - Inspected tab identifier from the content script sender.
 * @param message - Domain protocol envelope to forward to the panel.
 * @returns Nothing.
 */
const relayToPanel = (tabId: number, message: DevToolsMessage): void => {
  persistEnvelope(tabId, message);

  const port = panelPortsByTabId.get(tabId);
  if (!port) {
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
 * Relays a picker event from the content script to the DevTools panel port.
 *
 * @param tabId - Inspected tab identifier from the content script sender.
 * @param message - Picker hover, click, or escape relay payload.
 * @returns Nothing.
 */
const relayPickerEventToPanel = (
  tabId: number,
  message: PickerHoverRelayMessage | PickerClickRelayMessage | PickerEscapeRelayMessage,
): void => {
  const port = panelPortsByTabId.get(tabId);
  if (!port) {
    return;
  }

  try {
    port.postMessage(message);
  } catch (error) {
    console.debug(
      "[Blazor Dev Tools] Failed to post picker event to panel for tab",
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
const flushBufferedEnvelope = async (
  tabId: number,
  port: chrome.runtime.Port,
): Promise<void> => {
  let buffered = lastEnvelopeByTabId.get(tabId);
  if (!buffered) {
    buffered = await loadPersistedEnvelope(tabId);
    if (buffered)
      lastEnvelopeByTabId.set(tabId, buffered);
  }

  if (!buffered)
    return;

  try {
    port.postMessage(buffered);
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
  if (port.name !== "blazor-devtools-panel")
    return;

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
      const refreshMessage: ContentRefreshMessage = { type: "bdt:requestRefresh" };
      chrome.tabs.sendMessage(tabId, refreshMessage).catch(() => {
        // Content script may not be ready yet; buffered envelope may still apply.
      });
      void flushBufferedEnvelope(tabId, port);
      return;
    }

    if (isPanelHighlightMessage(message)) {
      const highlightMessage: ContentHighlightMessage = {
        type: "bdt:highlight",
        selector: message.selector,
        name: message.name,
      };
      chrome.tabs
        .sendMessage(message.tabId, highlightMessage)
        .catch(() => {
          // Content script may not be ready yet.
        });
      return;
    }

    if (isPanelPickerControlMessage(message)) {
      const pickerMessage: ContentPickerControlMessage = {
        type: "bdt:picker",
        active: message.active,
        locators: message.locators,
      };
      chrome.tabs
        .sendMessage(message.tabId, pickerMessage)
        .catch(() => {
          // Content script may not be ready yet.
        });
      return;
    }
  };

  /**
   * Cleans up panel port registration when the DevTools panel disconnects.
   *
   * @returns Nothing.
   */
  const onPanelDisconnect = (): void => {
    if (typeof tabId === "number") {
      panelPortsByTabId.delete(tabId);
      const disablePickerMessage: ContentPickerControlMessage = {
        type: "bdt:picker",
        active: false,
        locators: [],
      };
      chrome.tabs
        .sendMessage(tabId, disablePickerMessage)
        .catch(() => {
          // Content script may not be ready yet.
        });
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
  if (typeof tabId !== "number") {
    return false;
  }

  if (isDevToolsMessage(message)) {
    relayToPanel(tabId, message);
    return false;
  }

  if (isContentPickerHoverMessage(message)) {
    relayPickerEventToPanel(tabId, {
      type: "picker:hover",
      componentId: message.componentId,
    });
    return false;
  }

  if (isContentPickerClickMessage(message)) {
    relayPickerEventToPanel(tabId, {
      type: "picker:click",
      componentId: message.componentId,
    });
    return false;
  }

  if (isContentPickerEscapeMessage(message)) {
    relayPickerEventToPanel(tabId, { type: "picker:escape" });
    return false;
  }

  return false;
};

chrome.runtime.onConnect.addListener(handlePanelConnect);
chrome.runtime.onMessage.addListener(handleContentScriptMessage);

/**
 * Clears buffered envelopes when a tab navigates so stale Development snapshots
 * are not replayed after a Production reload on the same tab.
 */
chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (changeInfo.status === "loading") {
    void clearBufferedEnvelope(tabId);
  }
});
