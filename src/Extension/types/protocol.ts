/**
 * Standardized JSON messaging protocol between the Chrome extension and the
 * Blazor Dev Tools client library. Chrome-internal relay messages (e.g.
 * panel:connect) are intentionally separate from these domain envelopes.
 */

/** Protocol identifier sent on every domain message. */
export const PROTOCOL_NAME = "blazor-devtools" as const;

/** Current protocol version. Increment when breaking envelope or payload shapes change. */
export const PROTOCOL_VERSION = 1 as const;

/** Runtime message type discriminators (usable from plain JS after compile). */
export const MessageType = {
  ComponentTreeUpdate: "componentTreeUpdate",
  ComponentSelection: "componentSelection",
  ComponentPropsUpdate: "componentPropsUpdate",
} as const;

/** Union of all domain message type string values. */
export type MessageTypeValue =
  (typeof MessageType)[keyof typeof MessageType];

/** A node in the nested Blazor component tree. */
export interface ComponentNode {
  /** Stable identifier for this component instance. */
  id: string;
  /** Display name of the component type. */
  name: string;
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

/** Payload for a full component tree snapshot. */
export interface ComponentTreeUpdatePayload {
  /** Root of the component tree. */
  root: ComponentNode;
}

/** Payload when the user selects a component in the DevTools panel. */
export interface ComponentSelectionPayload {
  /** Identifier of the selected component. */
  componentId: string;
}

/** Payload with parameters and injections for a specific component. */
export interface ComponentPropsUpdatePayload {
  /** Identifier of the component whose props are described. */
  componentId: string;
  /** Component parameters and their values. */
  parameters: ComponentParameter[];
  /** Services injected into the component. */
  injections: ComponentInjection[];
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

/** Envelope carrying a component selection event. */
export type ComponentSelectionMessage = DevToolsEnvelope<
  typeof MessageType.ComponentSelection,
  ComponentSelectionPayload
>;

/** Envelope carrying component parameters and injections. */
export type ComponentPropsUpdateMessage = DevToolsEnvelope<
  typeof MessageType.ComponentPropsUpdate,
  ComponentPropsUpdatePayload
>;

/** Discriminated union of all domain DevTools messages. */
export type DevToolsMessage =
  | ComponentTreeUpdateMessage
  | ComponentSelectionMessage
  | ComponentPropsUpdateMessage;

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
    typeof message.type === "string" &&
    message.payload !== undefined
  );
};
