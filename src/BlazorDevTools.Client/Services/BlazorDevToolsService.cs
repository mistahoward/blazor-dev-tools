using BlazorDevTools.Client.Protocol;
using Microsoft.JSInterop;

namespace BlazorDevTools.Client.Services;

/// <summary>
/// Bridges the Blazor runtime to the Chrome DevTools extension via
/// <c>window.postMessage</c> and the standardized JSON protocol.
/// </summary>
internal sealed class BlazorDevToolsService(IJSRuntime jsRuntime) : IBlazorDevToolsService, IAsyncDisposable {
	private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
		"import", "./_content/BlazorDevTools.Client/blazorDevToolsInterop.js").AsTask());

	/// <inheritdoc />
	public bool IsEnabled => true;

	/// <inheritdoc />
	public bool IsInitialized { get; private set; }

	/// <inheritdoc />
	public async Task InitializeAsync() {
		if (!IsEnabled || IsInitialized) {
			return;
		}

		try {
			DevToolsEnvelope<ComponentTreeUpdatePayload> envelope = CreateMockComponentTreeUpdate();
			IJSObjectReference module = await _moduleTask.Value;
			await module.InvokeVoidAsync("dispatch", envelope);
			IsInitialized = true;
		} catch (JSException) {
			// JS interop unavailable (e.g. static prerender); caller may retry.
		} catch (InvalidOperationException) {
			// Circuit not yet interactive; caller may retry.
		}
	}

	/// <inheritdoc />
	public async ValueTask DisposeAsync() {
		if (_moduleTask.IsValueCreated) {
			IJSObjectReference module = await _moduleTask.Value;
			await module.DisposeAsync();
		}
	}

	private static DevToolsEnvelope<ComponentTreeUpdatePayload> CreateMockComponentTreeUpdate() =>
		new() {
			Protocol = DevToolsProtocol.Name,
			Version = DevToolsProtocol.Version,
			Type = DevToolsMessageType.ComponentTreeUpdate,
			Payload = new ComponentTreeUpdatePayload {
				Root = new ComponentNode {
					Id = "root",
					Name = "App",
					Children = [
						new ComponentNode {
							Id = "child-1",
							Name = "MockComponent",
						},
					],
				},
			},
		};
}
