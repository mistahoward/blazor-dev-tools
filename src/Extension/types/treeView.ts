/** Options controlling tree rendering and interaction. */
export interface RenderTreeOptions {
  /** Id of the currently selected component, if any. */
  selectedId: string | null;
  /** Id of the row currently under the pointer, if any. */
  hoveredId?: string | null;
  /** Set of component ids whose children are collapsed. */
  collapsedIds: ReadonlySet<string>;
  /** Called when the user selects a component row. */
  onSelect: (id: string) => void;
  /** Called when the user toggles expand/collapse on a row. */
  onToggle: (id: string) => void;
  /** Called when the pointer enters or leaves a component row. */
  onHover?: (id: string | null) => void;
}
