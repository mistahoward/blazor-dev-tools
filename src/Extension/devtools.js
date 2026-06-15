/**
 * Registers the Blazor DevTools panel in Chrome DevTools.
 */
chrome.devtools.panels.create(
  "Blazor",
  "icons/icon48.png",
  "panel.html",
  () => {
    // Panel created; initialization happens in panel.js.
  }
);
