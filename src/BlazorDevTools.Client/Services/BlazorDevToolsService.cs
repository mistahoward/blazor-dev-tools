using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorDevTools.Client.Inspection;
using BlazorDevTools.Client.Options;
using BlazorDevTools.Client.Protocol;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace BlazorDevTools.Client.Services;

/// <summary>
/// Bridges the Blazor runtime to the Chrome DevTools extension via
/// <c>window.postMessage</c> and the standardized JSON protocol.
/// </summary>
internal sealed class BlazorDevToolsService(
    IJSRuntime jsRuntime,
    NavigationManager navigationManager,
    IComponentTreeInspector treeInspector,
    IOptions<BlazorDevToolsOptions> options,
    ILogger<BlazorDevToolsService> logger) : IBlazorDevToolsService, IAsyncDisposable
{
    private const int _debounceMilliseconds = 250;

    private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
        "import", "./_content/BlazorDevTools.Client/blazorDevToolsInterop.js").AsTask());

    private readonly Lock _debounceLock = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);

    private ComponentBase? _host;
    private CancellationTokenSource? _debounceCts;
    private DotNetObjectReference<BlazorDevToolsService>? _dotNetRef;
    private bool _disposed;
    private bool _locationSubscribed;
    private bool _pendingRefresh;
    private string? _lastPayloadHash;

    /// <inheritdoc />
    public bool IsEnabled => options.Value.Enabled == true;

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync(ComponentBase host)
    {
        if (!IsEnabled || IsInitialized)
            return;

        _host = host;
        EnsureLocationSubscription();

        try
        {
            IJSObjectReference module = await _moduleTask.Value;
            await RegisterRefreshCallbackAsync(module);
            IsInitialized = true;
            await CaptureAndDispatchAsync(CancellationToken.None);
        }
        catch (JSException)
        {
            // JS interop unavailable (e.g. static prerender); caller may retry.
        }
        catch (InvalidOperationException)
        {
            // Circuit not yet interactive; caller may retry.
        }
    }

    /// <inheritdoc />
    public void RequestRefresh()
    {
        if (_disposed || _host is null)
            return;

        ScheduleDebouncedCapture();
    }

    /// <summary>
    /// Invoked from JavaScript when the DevTools panel requests a fresh tree snapshot.
    /// Clears the payload hash so the next capture is always dispatched even if the tree is unchanged.
    /// </summary>
    [JSInvokable]
    public void OnRefreshRequested()
    {
        _lastPayloadHash = null;
        RequestRefresh();
    }

    /// <summary>
    /// Invoked from JavaScript when the DOM mutates after a descendant component re-render.
    /// Requests a refresh without clearing the payload hash so unchanged trees are deduplicated.
    /// Complements <see cref="DevToolsInitializer"/> navigation and per-render refresh paths.
    /// </summary>
    [JSInvokable]
    public void OnRenderActivity() => RequestRefresh();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        if (_moduleTask.IsValueCreated)
        {
            try
            {
                IJSObjectReference module = await _moduleTask.Value;
                await module.InvokeVoidAsync("stopObserving");
            }
            catch (JSException)
            {
                // Circuit disconnected or interop unavailable during teardown.
            }
            catch (JSDisconnectedException)
            {
                // Circuit disconnected during teardown.
            }
            catch (ObjectDisposedException)
            {
                // Module or circuit already disposed.
            }
            catch (InvalidOperationException)
            {
                // Circuit not yet interactive or already torn down.
            }
        }

        _dotNetRef?.Dispose();
        _dotNetRef = null;

        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        if (_locationSubscribed)
        {
            navigationManager.LocationChanged -= OnLocationChanged;
            _locationSubscribed = false;
        }

        if (_moduleTask.IsValueCreated)
        {
            IJSObjectReference module = await _moduleTask.Value;
            await module.DisposeAsync();
        }

        _captureGate.Dispose();
    }

    private async Task RegisterRefreshCallbackAsync(IJSObjectReference module)
    {
        if (_dotNetRef is not null)
            return;

        _dotNetRef = DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("registerRefreshCallback", _dotNetRef);
    }

    /// <summary>
    /// Ensures that the NavigationManager.LocationChanged event is subscribed exactly once,
    /// so that component tree updates are triggered on navigation events.
    /// </summary>
    private void EnsureLocationSubscription()
    {
        if (_locationSubscribed)
            return;

        navigationManager.LocationChanged += OnLocationChanged;
        _locationSubscribed = true;
    }

    /// <summary>
    /// Handles navigation location changes by requesting a refresh of the component tree snapshot.
    /// </summary>
    /// <param name="sender">The event source, typically the <see cref="NavigationManager"/>.</param>
    /// <param name="e">The <see cref="LocationChangedEventArgs"/> associated with the navigation event.</param>
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => RequestRefresh();

    /// <summary>
    /// Schedules a debounced capture of the component tree.
    /// Cancels any pending capture operation and starts a new one using a delay,
    /// ensuring rapid calls are coalesced into a single operation.
    /// </summary>
    /// <remarks>
    /// This method acquires a lock on <see cref="_debounceLock"/> to coordinate concurrent calls.
    /// It is no-op if the service has been disposed.
    /// </remarks>
    private void ScheduleDebouncedCapture()
    {
        lock (_debounceLock)
        {
            if (_disposed)
                return;

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            CancellationToken token = _debounceCts.Token;
            _ = DebouncedCaptureAsync(token);
        }
    }

    /// <summary>
    /// Waits for the debounce interval and then triggers a capture and dispatch of the component tree,
    /// unless the operation is cancelled (e.g., by a new debounce call or service disposal).
    /// </summary>
    /// <param name="token">A <see cref="CancellationToken"/> that cancels the debounce wait and capture dispatch.</param>
    /// <returns>A task that completes when the capture and dispatch has run or the operation is cancelled.</returns>
    private async Task DebouncedCaptureAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_debounceMilliseconds, token);
            await CaptureAndDispatchAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Debounce superseded or service disposed.
        }
    }

    /// <summary>
    /// Captures the current component tree and dispatches it to the JavaScript side if it has changed since the last dispatch.
    /// </summary>
    /// <param name="token">
    /// A <see cref="CancellationToken"/> that can be used to cancel the operation.
    /// </param>
    /// <remarks>
    /// <para>
    /// Ensures only one capture operation runs at a time by acquiring the <see cref="_captureGate"/> semaphore.
    /// If another capture is already running, sets <see cref="_pendingRefresh"/> to ensure a follow-up occurs after the current operation.
    /// </para>
    /// <para>
    /// For each iteration, the method:
    /// <list type="number">
    /// <item>Clears <see cref="_pendingRefresh"/> and verifies the service is not disposed and the token is not canceled.</item>
    /// <item>Invokes <see cref="CaptureTreeOnDispatcherAsync"/> to obtain the component tree payload via the appropriate renderer dispatcher.</item>
    /// <item>Ignores the update if the payload is <c>null</c>, or nothing has changed since the last dispatch (compared by hash).</item>
    /// <item>Serializes a <see cref="DevToolsEnvelope{ComponentTreeUpdatePayload}"/> and dispatches it to the JS interop module.</item>
    /// </list>
    /// If <see cref="_pendingRefresh"/> is set during capture, the operation loops to process the next pending update unless cancellation or disposal occurs.
    /// </para>
    /// <para>
    /// JS interop or serialization exceptions are logged and do not throw. The semaphore is always released in <c>finally</c>.
    /// </para>
    /// </remarks>
    private async Task CaptureAndDispatchAsync(CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested || _host is null)
            return;

        if (!await _captureGate.WaitAsync(0, token))
        {
            _pendingRefresh = true;
            return;
        }

        try
        {
            do
            {
                _pendingRefresh = false;

                if (_disposed || token.IsCancellationRequested || _host is null)
                    break;

                ComponentTreeUpdatePayload? payload = await CaptureTreeOnDispatcherAsync(_host);
                if (payload is null || _disposed || token.IsCancellationRequested)
                    continue;

                string payloadHash = ComputePayloadHash(payload);
                if (payloadHash == _lastPayloadHash)
                    continue;

                _lastPayloadHash = payloadHash;

                DevToolsEnvelope<ComponentTreeUpdatePayload> envelope = new()
                {
                    Protocol = DevToolsProtocol.Name,
                    Version = DevToolsProtocol.Version,
                    Type = DevToolsMessageType.ComponentTreeUpdate,
                    Payload = payload,
                };

                string json = JsonSerializer.Serialize(envelope, DevToolsJsonSerializerOptions.Envelope);
                IJSObjectReference module = await _moduleTask.Value;
                await module.InvokeVoidAsync("dispatch", token, json);
            }
            while (_pendingRefresh && !_disposed && !token.IsCancellationRequested);
        }
        catch (JSException)
        {
            // JS interop unavailable; ignore refresh failures after init.
        }
        catch (InvalidOperationException)
        {
            // Circuit disconnected.
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Blazor Dev Tools: failed to serialize component tree snapshot.");
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Blazor Dev Tools: failed to serialize component tree snapshot.");
        }
        finally
        {
            _captureGate.Release();
        }
    }

    /// <summary>
    /// Captures a snapshot of the Blazor component tree for the specified <paramref name="host"/> component,
    /// ensuring execution occurs on the correct renderer dispatcher if available.
    /// </summary>
    /// <param name="host">The root <see cref="ComponentBase"/> from which to start capturing the component tree.</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> producing a <see cref="ComponentTreeUpdatePayload"/> representing the captured tree,
    /// or <c>null</c> if the operation fails or an exception occurs.
    /// </returns>
    private async Task<ComponentTreeUpdatePayload?> CaptureTreeOnDispatcherAsync(ComponentBase host)
    {
        try
        {
            Renderer? renderer = BlazorInternalsAccessor.TryGetRenderer(host);
            if (renderer is null)
                return treeInspector.CaptureTree(host);

            ComponentTreeUpdatePayload? payload = null;
            await renderer.Dispatcher.InvokeAsync(() =>
            {
                payload = treeInspector.CaptureTree(host);
            });

            return payload;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Computes a deterministic SHA-256 hash of the serialized <see cref="ComponentTreeUpdatePayload"/>.
    /// </summary>
    /// <param name="payload">The <see cref="ComponentTreeUpdatePayload"/> to hash.</param>
    /// <returns>
    /// A hexadecimal string representation of the SHA-256 hash of the payload,
    /// encoded as JSON using <see cref="DevToolsJsonSerializerOptions.PayloadHash"/>.
    /// </returns>
    private static string ComputePayloadHash(ComponentTreeUpdatePayload payload)
    {
        string json = JsonSerializer.Serialize(payload, DevToolsJsonSerializerOptions.PayloadHash);
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hashBytes);
    }
}
