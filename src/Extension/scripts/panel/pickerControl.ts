/**
 * Panel-side control messages for the content-script page element picker.
 */
import type { PickerLocatorEntry } from "../../types/relay.js";

/**
 * Sends picker enable/disable state and locators to the content script via the background worker.
 *
 * @param port - DevTools panel runtime port.
 * @param tabId - Inspected tab identifier.
 * @param active - Whether picker mode should be active.
 * @param locators - Component locators for reverse matching.
 * @returns Nothing.
 */
export const sendPickerControl = (
  port: chrome.runtime.Port | null,
  tabId: number,
  active: boolean,
  locators: readonly PickerLocatorEntry[],
): void => {
  port?.postMessage({
    type: "panel:picker",
    tabId,
    active,
    locators: [...locators],
  });
};

export type { PickerLocatorEntry };
