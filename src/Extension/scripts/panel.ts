/**
 * DevTools panel entry point. Opens a long-lived port to the background
 * service worker, renders the component tree, and shows selected node details.
 */
import {
  isComponentTreeUpdateMessage,
  type ComponentNode,
} from "../types/protocol.js";
import { renderDetails } from "./panel/detailsView.js";
import {
  sendPickerControl,
  type PickerLocatorEntry,
} from "./panel/pickerControl.js";
import { applyPageHighlight } from "./panel/pageHighlight.js";
import { renderTree } from "./panel/treeView.js";
import { isReservedNodeId, SYNTHETIC_ROOT_ID, TRUNCATED_ID } from "./panel/constants.js";
import {
  isPickerClickRelayMessage,
  isPickerEscapeRelayMessage,
  isPickerHoverRelayMessage,
} from "../types/relay.js";

/** Inspected tab id for this DevTools panel instance. */
const inspectedTabId = chrome.devtools.inspectedWindow.tabId;

/** Delay before attempting to reconnect after a port disconnect. */
const RECONNECT_DELAY_MS = 500;

/** Long-lived runtime port to the background service worker. */
let port: chrome.runtime.Port | null = null;

/** Root of the latest component tree snapshot, if any. */
let currentRoot: ComponentNode | null = null;

/** Id of the currently selected component in the tree. */
let selectedId: string | null = null;

/** Id of the component row currently under the pointer, if any. */
let hoveredId: string | null = null;

/** Component ids whose children are collapsed in the tree view. */
const collapsedIds = new Set<string>();

/** Lookup of selectable component nodes by id from the latest tree. */
const nodeIndex = new Map<string, ComponentNode>();

/** Tree depth per component id, used to disambiguate page-picker matches. */
const depthByNodeId = new Map<string, number>();

/** Whether page element picker (inspect) mode is active. */
let pickerActive = false;

/** Last component id previewed by the page picker, if any. */
let pickerPreviewId: string | null = null;

/** Whether the background service worker port is connected. */
let isConnected = false;

/** Whether a reconnect attempt is already scheduled. */
let reconnectScheduled = false;

/** Id of the last component sent to the page highlight relay. */
let lastHighlightedId: string | null = null;

/** Whether the panel DOM was validated at startup. */
let panelDomReady = false;

/** DOM: tree mount node. */
const treeRootEl = document.getElementById("tree-root");

/** DOM: tree empty-state label. */
const treeEmptyEl = document.getElementById("tree-empty");

/** DOM: details mount node. */
const detailsRootEl = document.getElementById("details-root");

/** DOM: details empty-state label. */
const detailsEmptyEl = document.getElementById("details-empty");

/** DOM: disconnect banner. */
const disconnectBannerEl = document.getElementById("disconnect-banner");

/** DOM: page element picker toggle. */
const pickerToggleEl = document.getElementById(
  "picker-toggle",
) as HTMLButtonElement | null;

/**
 * Validates required panel markup. DevTools can serve a cached {@link panel.html}
 * after an extension reload until the panel tab is closed and reopened.
 *
 * @returns `true` when all required elements exist.
 */
const ensurePanelDom = (): boolean => {
  const missing: string[] = [];

  if (!treeRootEl) {
    missing.push("tree-root");
  }
  if (!treeEmptyEl) {
    missing.push("tree-empty");
  }
  if (!detailsRootEl) {
    missing.push("details-root");
  }
  if (!detailsEmptyEl) {
    missing.push("details-empty");
  }
  if (!disconnectBannerEl) {
    missing.push("disconnect-banner");
  }

  if (missing.length === 0) {
    return true;
  }

  const header = document.querySelector(".panel-header");
  if (header && !header.querySelector("[data-bdt-dom-error]")) {
    const notice = document.createElement("p");
    notice.dataset.bdtDomError = "true";
    notice.className = "disconnect-banner";
    notice.textContent =
      `Panel markup is out of date (missing: ${missing.join(", ")}). ` +
      "Close DevTools completely, reload the extension, then reopen the Blazor panel.";
    header.append(notice);
  }

  return false;
};

/**
 * Clears in-panel tree state so a reconnect or reload does not show a prior snapshot.
 *
 * @returns Nothing.
 */
const resetTreeState = (): void => {
  currentRoot = null;
  selectedId = null;
  hoveredId = null;
  pickerPreviewId = null;
  lastHighlightedId = null;
  nodeIndex.clear();
  depthByNodeId.clear();
  collapsedIds.clear();
  sendPanelHighlight(null);
  render();
};

/**
 * Sends the panel connect handshake to register this tab with the background worker.
 *
 * @returns Nothing.
 */
const sendPanelConnect = (): void => {
  port?.postMessage({
    type: "panel:connect",
    tabId: inspectedTabId,
  });
};

/**
 * Applies or clears the highlight overlay in the inspected page.
 *
 * @param selector - CSS selector to highlight, or {@link null} to clear.
 * @param name - Optional component display name for the overlay label.
 * @returns Nothing.
 */
const sendPanelHighlight = (
  selector: string | null,
  name?: string,
): void => {
  applyPageHighlight(selector, name);
};

/**
 * Highlights the page element for a component id, or clears when id is null.
 *
 * @param id - Component id to highlight, or {@link null} to clear.
 * @returns Nothing.
 */
const setPageHighlightForId = (id: string | null): void => {
  if (id === lastHighlightedId) {
    return;
  }

  if (id === null) {
    lastHighlightedId = null;
    sendPanelHighlight(null);
    return;
  }

  const node = nodeIndex.get(id);
  if (!node) {
    return;
  }

  lastHighlightedId = id;
  sendPanelHighlight(node.locator ?? null, node.name);
};

/**
 * Restores the page highlight to the current selection, or clears when none applies.
 *
 * @returns Nothing.
 */
const restoreSelectionHighlight = (): void => {
  if (selectedId !== null) {
    const node = nodeIndex.get(selectedId);
    if (node?.locator) {
      if (lastHighlightedId === selectedId) {
        return;
      }

      lastHighlightedId = selectedId;
      sendPanelHighlight(node.locator, node.name);
      return;
    }
  }

  if (lastHighlightedId !== null) {
    lastHighlightedId = null;
    sendPanelHighlight(null);
  }
};

/**
 * Rebuilds the node index from the current tree, excluding truncated placeholders.
 *
 * @param root - Root node of the component tree.
 * @returns Nothing.
 */
const rebuildNodeIndex = (root: ComponentNode): void => {
  nodeIndex.clear();
  depthByNodeId.clear();

  const walk = (node: ComponentNode, depth: number): void => {
    if (node.id !== TRUNCATED_ID) {
      nodeIndex.set(node.id, node);
      depthByNodeId.set(node.id, depth);
    }
    for (const child of node.children ?? []) {
      walk(child, depth + 1);
    }
  };

  const topLevelNodes =
    root.id === SYNTHETIC_ROOT_ID ? (root.children ?? []) : [root];

  for (const node of topLevelNodes) {
    walk(node, 0);
  }
};

/**
 * Removes selection and collapse state for ids no longer present in the tree.
 *
 * @returns Nothing.
 */
const pruneStaleState = (): void => {
  if (selectedId !== null && !nodeIndex.has(selectedId)) {
    if (lastHighlightedId !== null) {
      lastHighlightedId = null;
      sendPanelHighlight(null);
    }

    selectedId = null;
  }

  if (hoveredId !== null && !nodeIndex.has(hoveredId)) {
    hoveredId = null;
  }

  if (pickerPreviewId !== null && !nodeIndex.has(pickerPreviewId)) {
    pickerPreviewId = null;
  }

  for (const id of collapsedIds) {
    if (!nodeIndex.has(id)) {
      collapsedIds.delete(id);
    }
  }
};

/**
 * Builds locator entries for reverse-matching DOM elements during page picker mode.
 *
 * @returns Locator entries sorted by tree depth ascending.
 */
const buildPickerLocators = (): PickerLocatorEntry[] => {
  const entries: PickerLocatorEntry[] = [];

  for (const [id, node] of nodeIndex) {
    if (isReservedNodeId(id) || !node.locator) {
      continue;
    }

    entries.push({
      id,
      selector: node.locator,
      depth: depthByNodeId.get(id) ?? 0,
    });
  }

  entries.sort((left, right) => left.depth - right.depth);
  return entries;
};

/**
 * Updates the picker toggle button to reflect active state.
 *
 * @returns Nothing.
 */
const syncPickerToggleUi = (): void => {
  if (!pickerToggleEl) {
    return;
  }

  pickerToggleEl.classList.toggle("picker-toggle--active", pickerActive);
  pickerToggleEl.setAttribute("aria-pressed", pickerActive ? "true" : "false");
};

/**
 * Selects a component by id and updates the tree, details, and page highlight.
 *
 * @param id - Component id to select.
 * @returns Nothing.
 */
const selectComponent = (id: string): void => {
  if (isReservedNodeId(id)) {
    return;
  }

  selectedId = id;
  hoveredId = null;
  render();
  setPageHighlightForId(id);
};

/**
 * Sends the current picker state and locators to the content script.
 *
 * @returns Nothing.
 */
const syncPickerToContentScript = (): void => {
  sendPickerControl(
    port,
    inspectedTabId,
    pickerActive,
    buildPickerLocators(),
  );
};

/**
 * Handles hover preview events from the content-script page picker.
 *
 * @param componentId - Matched component id, or {@link null} when none.
 * @returns Nothing.
 */
const handlePickerHover = (componentId: string | null): void => {
  if (!pickerActive) {
    return;
  }

  if (componentId) {
    pickerPreviewId = componentId;
    setPageHighlightForId(componentId);
    return;
  }

  pickerPreviewId = null;
  if (lastHighlightedId !== null) {
    lastHighlightedId = null;
    sendPanelHighlight(null);
  }
};

/**
 * Handles pick (click) events from the content-script page picker.
 *
 * @param componentId - Matched component id, or {@link null} when none.
 * @returns Nothing.
 */
const handlePickerClick = (componentId: string | null): void => {
  if (!pickerActive) {
    return;
  }

  if (!componentId) {
    return;
  }

  selectComponent(componentId);
  setPickerActive(false);
};

/**
 * Enables or disables page element picker mode.
 *
 * @param active - Whether inspect mode should be active.
 * @returns Nothing.
 */
const setPickerActive = (active: boolean): void => {
  if (pickerActive === active) {
    return;
  }

  pickerActive = active;
  pickerPreviewId = null;
  syncPickerToggleUi();
  syncPickerToContentScript();

  if (!active) {
    restoreSelectionHighlight();
  }
};

/**
 * Renders the tree and details panes from current panel state.
 *
 * @returns Nothing.
 */
const render = (): void => {
  if (!panelDomReady) {
    return;
  }

  const hasTree = currentRoot !== null;

  treeEmptyEl!.hidden = hasTree;
  treeRootEl!.hidden = !hasTree;
  disconnectBannerEl!.hidden = isConnected;

  if (!hasTree || !currentRoot) {
    treeRootEl!.replaceChildren();
    detailsRootEl!.hidden = true;
    detailsEmptyEl!.hidden = false;
    detailsRootEl!.replaceChildren();
    return;
  }

  const scrollTop = treeRootEl!.scrollTop;

  renderTree(treeRootEl!, currentRoot, {
    selectedId,
    hoveredId,
    collapsedIds,
    onSelect: (id) => {
      if (isReservedNodeId(id)) {
        if (lastHighlightedId !== null) {
          lastHighlightedId = null;
          sendPanelHighlight(null);
        }

        return;
      }

      if (selectedId === id && lastHighlightedId === id) {
        return;
      }

      if (pickerActive) {
        setPickerActive(false);
      }

      selectComponent(id);
    },
    onHover: (id) => {
      if (pickerActive) {
        return;
      }

      hoveredId = id;

      if (id === null) {
        restoreSelectionHighlight();
        return;
      }

      if (isReservedNodeId(id)) {
        return;
      }

      const node = nodeIndex.get(id);
      if (!node?.locator) {
        restoreSelectionHighlight();
        return;
      }

      setPageHighlightForId(id);
    },
    onToggle: (id) => {
      if (isReservedNodeId(id)) {
        return;
      }
      if (collapsedIds.has(id)) {
        collapsedIds.delete(id);
      } else {
        collapsedIds.add(id);
      }
      render();
    },
  });

  treeRootEl!.scrollTop = scrollTop;

  const selectedNode =
    selectedId !== null ? (nodeIndex.get(selectedId) ?? null) : null;

  detailsEmptyEl!.hidden = selectedNode !== null;
  detailsRootEl!.hidden = selectedNode === null;

  if (selectedNode) {
    renderDetails(detailsRootEl!, selectedNode);
  } else {
    detailsRootEl!.replaceChildren();
  }
};

/**
 * Applies a component tree update from the background relay.
 *
 * @param root - Root node from the update payload.
 * @returns Nothing.
 */
const applyTreeUpdate = (root: ComponentNode): void => {
  currentRoot = root;
  rebuildNodeIndex(root);
  pruneStaleState();
  render();

  if (pickerActive) {
    syncPickerToContentScript();
  }

  if (selectedId !== null) {
    setPageHighlightForId(selectedId);
  }
};

/**
 * Handles domain envelopes relayed from the background service worker.
 *
 * @param message - Message payload received on the panel runtime port.
 * @returns Nothing.
 */
const handlePanelMessage = (message: unknown): void => {
  if (isComponentTreeUpdateMessage(message)) {
    applyTreeUpdate(message.payload.root);
    return;
  }

  if (isPickerHoverRelayMessage(message)) {
    handlePickerHover(message.componentId);
    return;
  }

  if (isPickerClickRelayMessage(message)) {
    handlePickerClick(message.componentId);
    return;
  }

  if (isPickerEscapeRelayMessage(message)) {
    setPickerActive(false);
  }
};

/**
 * Schedules a reconnect attempt after the background port drops.
 *
 * @returns Nothing.
 */
const scheduleReconnect = (): void => {
  if (reconnectScheduled) {
    return;
  }

  reconnectScheduled = true;
  isConnected = false;
  if (pickerActive) {
    pickerActive = false;
    syncPickerToggleUi();
  }
  render();

  window.setTimeout(() => {
    reconnectScheduled = false;
    connectPort();
  }, RECONNECT_DELAY_MS);
};

/**
 * Opens (or reopens) the runtime port and registers panel listeners.
 *
 * @returns Nothing.
 */
const connectPort = (): void => {
  if (!panelDomReady) {
    return;
  }

  try {
    port = chrome.runtime.connect({
      name: "blazor-devtools-panel",
    });
  } catch {
    scheduleReconnect();
    return;
  }

  isConnected = true;
  resetTreeState();

  port.onMessage.addListener(handlePanelMessage);
  port.onDisconnect.addListener(scheduleReconnect);
  sendPanelConnect();
};

panelDomReady = ensurePanelDom();
if (panelDomReady) {
  pickerToggleEl?.addEventListener("click", () => {
    setPickerActive(!pickerActive);
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && pickerActive) {
      setPickerActive(false);
    }
  });

  connectPort();
}
