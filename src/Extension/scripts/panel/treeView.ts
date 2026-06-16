/**
 * Renders the Blazor component tree as an expandable, selectable list.
 */
import type { ComponentNode } from "../../types/protocol.js";
import type { RenderTreeOptions } from "../../types/treeView.js";
import {
  isReservedNodeId,
  SYNTHETIC_ROOT_ID,
  TRUNCATED_ID,
} from "./constants.js";

/** Whether delegated interaction listeners are attached to the container. */
const containersWithDelegation = new WeakSet<HTMLElement>();

/** Latest render options per tree container (updated on each render). */
const optionsByContainer = new WeakMap<HTMLElement, RenderTreeOptions>();

/** Last row id that received a hover callback, per tree container. */
const lastHoveredIdByContainer = new WeakMap<HTMLElement, string | null>();

/**
 * Renders a component tree into the given container.
 *
 * @param container - DOM element that receives tree rows (typically `#tree-root`).
 * @param root - Root node from a component tree update payload.
 * @param opts - Selection, collapse state, and interaction callbacks.
 * @returns Nothing.
 */
export const renderTree = (
  container: HTMLElement,
  root: ComponentNode,
  opts: RenderTreeOptions,
): void => {
  optionsByContainer.set(container, opts);
  lastHoveredIdByContainer.set(container, null);

  if (!containersWithDelegation.has(container)) {
    container.addEventListener("click", (event) => {
      const currentOpts = optionsByContainer.get(container);
      if (currentOpts)
        handleTreeClick(event, container, currentOpts);
    });
    container.addEventListener("mouseover", (event) => {
      const currentOpts = optionsByContainer.get(container);
      if (currentOpts?.onHover)
        handleTreeHover(event, container, currentOpts);
    });
    container.addEventListener("mouseleave", () => {
      const currentOpts = optionsByContainer.get(container);
      if (!currentOpts?.onHover)
        return;

      lastHoveredIdByContainer.set(container, null);
      clearHoverClass(container);
      currentOpts.onHover(null);
    });
    containersWithDelegation.add(container);
  }

  container.replaceChildren();

  const topLevelNodes =
    root.id === SYNTHETIC_ROOT_ID ? (root.children ?? []) : [root];

  for (const node of topLevelNodes)
    appendNodeRows(container, node, 0, opts);
};

/**
 * Handles delegated click events on tree rows.
 *
 * @param event - Click event from the tree container.
 * @param container - Tree mount element used to resolve current options.
 * @param opts - Active render options with callbacks.
 * @returns Nothing.
 */
const handleTreeClick = (
  event: Event,
  container: HTMLElement,
  opts: RenderTreeOptions,
): void => {
  const target = event.target as HTMLElement;
  const row = target.closest<HTMLElement>("[data-bdt-row]");
  if (!row || !container.contains(row))
    return;

  const nodeId = row.dataset.bdtId;
  if (!nodeId || isReservedNodeId(nodeId) || nodeId === TRUNCATED_ID)
    return;

  const toggleButton = target.closest<HTMLElement>("[data-bdt-action='toggle']");
  if (toggleButton) {
    event.preventDefault();
    event.stopPropagation();
    opts.onToggle(nodeId);
    return;
  }

  const label = target.closest<HTMLElement>("[data-bdt-action='select']");
  if (label) {
    event.preventDefault();
    opts.onSelect(nodeId);
  }
};

/**
 * Clears hover styling from all rows in the tree container.
 *
 * @param container - Tree mount element.
 * @returns Nothing.
 */
const clearHoverClass = (container: HTMLElement): void => {
  for (const row of container.querySelectorAll<HTMLElement>(".tree-row--hover"))
    row.classList.remove("tree-row--hover");
};

/**
 * Applies hover styling to a single row and clears it from others.
 *
 * @param container - Tree mount element.
 * @param activeRow - Row that should receive hover styling.
 * @returns Nothing.
 */
const updateHoverClass = (container: HTMLElement, activeRow: HTMLElement): void => {
  clearHoverClass(container);

  if (!activeRow.classList.contains("tree-row--truncated"))
    activeRow.classList.add("tree-row--hover");
};

/**
 * Handles delegated mouseover events on tree rows for hover preview.
 *
 * @param event - Mouseover event from the tree container.
 * @param container - Tree mount element used to resolve current options.
 * @param opts - Active render options with callbacks.
 * @returns Nothing.
 */
const handleTreeHover = (
  event: Event,
  container: HTMLElement,
  opts: RenderTreeOptions,
): void => {
  const target = event.target as HTMLElement;
  const row = target.closest<HTMLElement>("[data-bdt-row]");

  if (!row || !container.contains(row)) {
    const lastHoveredId = lastHoveredIdByContainer.get(container) ?? null;
    if (lastHoveredId !== null) {
      lastHoveredIdByContainer.set(container, null);
      clearHoverClass(container);
      opts.onHover?.(null);
    }

    return;
  }

  const nodeId = row.dataset.bdtId;
  if (!nodeId || isReservedNodeId(nodeId) || nodeId === TRUNCATED_ID)
    return;

  const lastHoveredId = lastHoveredIdByContainer.get(container) ?? null;
  if (nodeId === lastHoveredId)
    return;

  lastHoveredIdByContainer.set(container, nodeId);
  updateHoverClass(container, row);
  opts.onHover?.(nodeId);
};

/**
 * Appends a node and its descendants to the tree container.
 *
 * @param container - Tree mount element.
 * @param node - Component node to render.
 * @param depth - Nesting depth for indentation.
 * @param opts - Render options.
 * @returns Nothing.
 */
const appendNodeRows = (
  container: HTMLElement,
  node: ComponentNode,
  depth: number,
  opts: RenderTreeOptions,
): void => {
  const isTruncated = node.id === TRUNCATED_ID;
  const children = node.children ?? [];
  const hasChildren = children.length > 0;
  const isCollapsed = opts.collapsedIds.has(node.id);
  const isSelected = opts.selectedId === node.id && !isTruncated;
  const isHovered = opts.hoveredId === node.id && !isTruncated;

  const row = document.createElement("div");
  row.className = "tree-row";
  row.dataset.bdtRow = "true";
  row.dataset.bdtId = node.id;
  row.setAttribute("role", "treeitem");
  row.style.paddingLeft = `${depth * 14 + 4}px`;

  if (isTruncated) {
    row.classList.add("tree-row--truncated");
    row.setAttribute("aria-disabled", "true");
  }

  if (isSelected) {
    row.classList.add("selected");
    row.setAttribute("aria-selected", "true");
  } else
    row.setAttribute("aria-selected", "false");

  if (isHovered)
    row.classList.add("tree-row--hover");

  const toggle = document.createElement("button");
  toggle.type = "button";
  toggle.className = "tree-toggle";
  toggle.dataset.bdtAction = "toggle";
  toggle.setAttribute("aria-label", isCollapsed ? "Expand" : "Collapse");

  if (!hasChildren || isTruncated) {
    toggle.classList.add("tree-toggle--leaf");
    toggle.textContent = "";
  } else
    toggle.textContent = isCollapsed ? "\u25B6" : "\u25BC";

  const label = document.createElement("span");
  label.className = "tree-label";
  label.dataset.bdtAction = "select";
  label.textContent = node.name;

  row.appendChild(toggle);
  row.appendChild(label);
  container.appendChild(row);

  if (hasChildren && !isCollapsed && !isTruncated)
    for (const child of children)
      appendNodeRows(container, child, depth + 1, opts);
};
