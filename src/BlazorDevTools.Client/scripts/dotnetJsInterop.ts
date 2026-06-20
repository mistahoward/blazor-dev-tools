/**
 * Minimal .NET JS interop surface used by the Blazor Dev Tools bridge module.
 */
export interface DotNetObjectReference {
  invokeMethodAsync<T>(methodName: string, ...args: unknown[]): Promise<T>;
}

/** DevTools panel refresh request posted to the page window. */
export interface DevToolsRefreshRequest {
  type: "blazorDevTools:requestRefresh";
}

export function isDevToolsRefreshRequest(
  data: unknown,
): data is DevToolsRefreshRequest {
  return (
    typeof data === "object" &&
    data !== null &&
    (data as DevToolsRefreshRequest).type === "blazorDevTools:requestRefresh"
  );
}
