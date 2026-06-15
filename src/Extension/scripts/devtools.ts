/**
 * Registers the Blazor DevTools panel in Chrome DevTools.
 */

/**
 * Called after the Blazor DevTools panel is created in Chrome DevTools.
 *
 * @returns Nothing.
 */
const onPanelCreated = (): void => {
  // Panel created; initialization happens in panel.ts.
};

/**
 * Registers the Blazor DevTools panel with Chrome DevTools.
 *
 * @returns Nothing.
 */
const registerDevToolsPanel = (): void => {
  chrome.devtools.panels.create(
    "Blazor",
    "icons/icon48.png",
    "panel.html",
    onPanelCreated,
  );
};

registerDevToolsPanel();
