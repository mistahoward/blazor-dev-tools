/**
 * Reserved component node ids mirrored from the C# tree inspector.
 * @see BlazorDevTools.Client.Inspection.ReflectionComponentTreeInspector
 */

/** Synthetic wrapper when the renderer has zero or multiple root components. */
export const SYNTHETIC_ROOT_ID = "__bdt_root__";

/** Placeholder id for depth- or budget-truncated nodes (not unique across siblings). */
export const TRUNCATED_ID = "__bdt_truncated__";

/**
 * Returns whether a node id is reserved and must not be selected, toggled, or indexed.
 *
 * @param id - Component node id from the protocol payload.
 * @returns `true` when the id is a synthetic root or truncation sentinel.
 */
export const isReservedNodeId = (id: string): boolean =>
  id === SYNTHETIC_ROOT_ID || id === TRUNCATED_ID;
