import type { ComponentNode, FlatComponentNode } from "../../types/protocol.js";
import { SYNTHETIC_ROOT_ID } from "./constants.js";

/**
 * Rebuilds a nested component tree from a flat protocol v2 node list.
 *
 * @param flat - Flat nodes from a component tree update payload.
 * @returns Root node suitable for the panel tree UI.
 */
export const nestFlatNodes = (flat: FlatComponentNode[]): ComponentNode => {
  if (flat.length === 0) {
    return {
      id: SYNTHETIC_ROOT_ID,
      name: "Root",
      children: [],
    };
  }

  const byId = new Map<string, FlatComponentNode>();
  for (const node of flat) {
    byId.set(node.id, node);
  }

  const childrenByParentId = new Map<string, FlatComponentNode[]>();
  const rootCandidates: FlatComponentNode[] = [];

  for (const node of flat) {
    const parentId = node.parentId ?? null;
    if (parentId === null || !byId.has(parentId)) {
      rootCandidates.push(node);
      continue;
    }

    const siblings = childrenByParentId.get(parentId);
    if (siblings) {
      siblings.push(node);
    } else {
      childrenByParentId.set(parentId, [node]);
    }
  }

  const visited = new Set<string>();

  const build = (node: FlatComponentNode): ComponentNode => {
    const childSources = childrenByParentId.get(node.id) ?? [];
    const children: ComponentNode[] = [];

    for (const child of childSources) {
      if (visited.has(child.id)) {
        continue;
      }

      visited.add(child.id);
      children.push(build(child));
    }

    return {
      id: node.id,
      name: node.name,
      children,
      parameters: node.parameters,
      injections: node.injections,
      locator: node.locator,
    };
  };

  if (rootCandidates.length === 1) {
    const root = rootCandidates[0];
    visited.add(root.id);
    return build(root);
  }

  const syntheticChildren: ComponentNode[] = [];
  for (const root of rootCandidates) {
    if (visited.has(root.id)) {
      continue;
    }

    visited.add(root.id);
    syntheticChildren.push(build(root));
  }

  return {
    id: SYNTHETIC_ROOT_ID,
    name: "Root",
    children: syntheticChildren,
  };
};
