/**
 * Standardized JSON messaging protocol between the Chrome extension and the
 * Blazor Dev Tools client library. Chrome-internal relay messages (e.g.
 * panel:connect) are intentionally separate from these domain envelopes.
 */

/** Protocol identifier sent on every domain message. */
export const PROTOCOL_NAME = "blazor-devtools" as const;

/** Current protocol version. Increment when breaking envelope or payload shapes change. */
export const PROTOCOL_VERSION = 2 as const;

/** Runtime message type discriminators (usable from plain JS after compile). */
export const MessageType = {
  ComponentTreeUpdate: "componentTreeUpdate",
} as const;

/** Union of all domain message type string values. */
export type MessageTypeValue =
  (typeof MessageType)[keyof typeof MessageType];

/** A node in the flat Blazor component tree wire format (protocol v2). */
export interface FlatComponentNode {
  /** Stable identifier for this component instance. */
  id: string;
  /** Display name of the component type. */
  name: string;
  /** Parent component id; omitted on the wire when this node is a root. */
  parentId?: string | null;
  /**
   * Component parameters and cascading values. Optional on the wire when empty.
   */
  parameters?: ComponentParameter[];
  /**
   * Services injected into this component. Optional on the wire when empty.
   */
  injections?: ComponentInjection[];
  /**
   * Best-effort CSS selector for the component's first rendered element.
   * Omitted on the wire when unavailable.
   */
  locator?: string | null;
}

/** A nested component node used by the panel tree UI after flat-to-tree assembly. */
export interface ComponentNode extends Omit<FlatComponentNode, "parentId"> {
  /** Child components rendered by this component. */
  children: ComponentNode[];
}

/** A single component parameter (route/query/cascading/etc.). */
export interface ComponentParameter {
  /** Parameter property name. */
  name: string;
  /** CLR or declared type name. */
  typeName: string;
  /** Serialized parameter value (any JSON value). */
  value: unknown;
}

/** A single injected service on a component. */
export interface ComponentInjection {
  /** Injection property name. */
  name: string;
  /** Declared service type. */
  serviceType: string;
  /** Concrete implementation type, if resolved. */
  implementationType: string | null;
}

/** Payload for a full component tree snapshot (protocol v2 flat wire format). */
export interface ComponentTreeUpdatePayload {
  /** All component nodes linked by parentId. */
  nodes: FlatComponentNode[];
}

/** Base envelope wrapping all domain messages. */
export interface DevToolsEnvelope<
  TType extends MessageTypeValue,
  TPayload,
> {
  /** Protocol identifier; must be {@link PROTOCOL_NAME}. */
  protocol: typeof PROTOCOL_NAME;
  /** Protocol version; must match {@link PROTOCOL_VERSION} for this schema. */
  version: typeof PROTOCOL_VERSION;
  /** Message discriminator. */
  type: TType;
  /** Type-specific payload body. */
  payload: TPayload;
}

/** Envelope carrying a component tree update. */
export type ComponentTreeUpdateMessage = DevToolsEnvelope<
  typeof MessageType.ComponentTreeUpdate,
  ComponentTreeUpdatePayload
>;

/** Discriminated union of all domain DevTools messages. */
export type DevToolsMessage = ComponentTreeUpdateMessage;

/**
 * Determines whether a value is a valid Blazor Dev Tools domain protocol envelope.
 *
 * @param value - The value to inspect, typically from `postMessage` or extension messaging.
 * @returns `true` when {@link value} matches the {@link DevToolsMessage} wire shape.
 */
export const isDevToolsMessage = (value: unknown): value is DevToolsMessage => {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const message = value as DevToolsMessage;
  return (
    message.protocol === PROTOCOL_NAME &&
    message.version === PROTOCOL_VERSION &&
    typeof message.type === "string" &&
    message.payload !== undefined
  );
};

/**
 * Determines whether a value is a component tree update envelope with a valid nodes array.
 *
 * @param value - The value received on the DevTools panel port.
 * @returns `true` when {@link value} is a {@link ComponentTreeUpdateMessage} with `payload.nodes`.
 */
export const isComponentTreeUpdateMessage = (
  value: unknown,
): value is ComponentTreeUpdateMessage => {
  if (!isDevToolsMessage(value)) {
    return false;
  }

  if (value.type !== MessageType.ComponentTreeUpdate) {
    return false;
  }

  const payload = value.payload as ComponentTreeUpdatePayload;
  return (
    typeof payload === "object" &&
    payload !== null &&
    Array.isArray(payload.nodes)
  );
};
