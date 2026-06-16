/**
 * Highlights DOM elements in the inspected page via chrome.devtools.inspectedWindow.eval.
 * DevTools panels should use this instead of relaying through the service worker.
 */

/** Script that installs the page-world highlight API once per inspected page. */
const INSTALL_HIGHLIGHT_API = String.raw`
(function () {
  if (window.__bdtPageHighlight) {
    return;
  }

  let overlayEl = null;
  let labelEl = null;
  let activeSelector = null;
  let activeName = undefined;
  let repositionScheduled = false;
  let resizeObserver = null;

  const clearHighlight = () => {
    activeSelector = null;
    activeName = undefined;
    repositionScheduled = false;
    if (resizeObserver) {
      resizeObserver.disconnect();
      resizeObserver = null;
    }
    document.removeEventListener("scroll", onReposition, true);
    window.removeEventListener("resize", onReposition);
    if (overlayEl) {
      overlayEl.remove();
      overlayEl = null;
    }
    if (labelEl) {
      labelEl.remove();
      labelEl = null;
    }
  };

  const ensureOverlayElements = () => {
    if (overlayEl && labelEl) {
      return;
    }
    overlayEl = document.createElement("div");
    overlayEl.style.cssText =
      "position:fixed;pointer-events:none;box-sizing:border-box;border:2px solid #1a73e8;" +
      "background:color-mix(in srgb,#1a73e8 12%,transparent);z-index:2147483646;display:none;";
    labelEl = document.createElement("div");
    labelEl.style.cssText =
      "position:fixed;pointer-events:none;box-sizing:border-box;padding:2px 6px;" +
      "font:11px/1.4 sans-serif;color:#fff;background:#1a73e8;border-radius:2px;z-index:2147483647;display:none;";
    document.body.append(overlayEl, labelEl);
  };

  const scheduleReposition = () => {
    if (repositionScheduled) {
      return;
    }
    repositionScheduled = true;
    requestAnimationFrame(() => {
      repositionScheduled = false;
      repositionOverlay();
    });
  };

  const observeTarget = (target) => {
    if (!resizeObserver) {
      resizeObserver = new ResizeObserver(() => scheduleReposition());
    }
    resizeObserver.disconnect();
    resizeObserver.observe(target);
  };

  const repositionOverlay = () => {
    if (!activeSelector || !overlayEl || !labelEl) {
      return;
    }

    let target = null;
    try {
      target = document.querySelector(activeSelector);
    } catch {
      target = null;
    }

    if (!target || !target.isConnected) {
      clearHighlight();
      return;
    }

    observeTarget(target);
    const rect = target.getBoundingClientRect();
    if (rect.width <= 0 && rect.height <= 0) {
      overlayEl.style.display = "none";
      labelEl.style.display = "none";
      return;
    }

    overlayEl.style.display = "block";
    overlayEl.style.top = rect.top + "px";
    overlayEl.style.left = rect.left + "px";
    overlayEl.style.width = rect.width + "px";
    overlayEl.style.height = rect.height + "px";

    if (activeName) {
      labelEl.textContent = activeName;
      labelEl.style.display = "block";
      labelEl.style.top = Math.max(0, rect.top - 20) + "px";
      labelEl.style.left = rect.left + "px";
    } else {
      labelEl.style.display = "none";
    }
  };

  const onReposition = () => scheduleReposition();

  window.__bdtPageHighlight = {
    apply(selector, name) {
      if (!selector) {
        clearHighlight();
        return;
      }
      activeSelector = selector;
      activeName = name || undefined;
      ensureOverlayElements();
      document.removeEventListener("scroll", onReposition, true);
      window.removeEventListener("resize", onReposition);
      document.addEventListener("scroll", onReposition, { capture: true, passive: true });
      window.addEventListener("resize", onReposition, { passive: true });
      scheduleReposition();
    },
    clear() {
      clearHighlight();
    },
  };
})();
`;

/**
 * Applies or clears the highlight overlay in the inspected page.
 *
 * @param selector - CSS selector to highlight, or {@link null} to clear.
 * @param name - Optional component display name for the overlay label.
 * @returns Nothing.
 */
export const applyPageHighlight = (
  selector: string | null,
  name?: string,
): void => {
  const safeSelector = selector === null ? "null" : JSON.stringify(selector);
  const safeName = JSON.stringify(name ?? "");

  const invokeScript =
    selector === null
      ? "window.__bdtPageHighlight.clear()"
      : `window.__bdtPageHighlight.apply(${safeSelector}, ${safeName})`;

  const script = `${INSTALL_HIGHLIGHT_API}\n${invokeScript};`;

  chrome.devtools.inspectedWindow.eval(script, () => {
    // Ignore eval errors (e.g. page navigated away).
  });
};
