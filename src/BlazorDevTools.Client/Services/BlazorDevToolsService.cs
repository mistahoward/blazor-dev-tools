using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorDevTools.Client.Inspection;
using BlazorDevTools.Client.Options;
using BlazorDevTools.Client.Protocol;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Routing;
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
    IOptions<BlazorDevToolsOptions> options) : IBlazorDevToolsService, IAsyncDisposable
{
    private const int _debounceMilliseconds = 250;

    /// <summary>
    /// Preconfigured <see cref="JsonSerializerOptions"/> for serializing <see cref="DevToolsEnvelope{TPayload}"/> messages.
    /// Uses camelCase property naming and ignores default values when writing JSON.
    /// </summary>
    private static readonly JsonSerializerOptions _envelopeSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Preconfigured <see cref="JsonSerializerOptions"/> for hashing protocol payloads deterministically.
    /// Uses camelCase property naming and ignores default values when serializing the payload for computing a hash.
    /// </summary>
    private static readonly JsonSerializerOptions _payloadHashOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
            await CaptureAndDispatchAsync(CancellationToken.None);
            IsInitialized = true;
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
    /// </summary>
    [JSInvokable]
    public void OnRefreshRequested()
    {
        _lastPayloadHash = null;
        RequestRefresh();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;

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

    /// <summary>
    /// Registers a .NET object reference with the provided JavaScript module to enable
    /// the DevTools panel to invoke refresh requests from JavaScript.
    /// This is called once to establish the refresh callback between .NET and JS.
    /// </summary>
    /// <param name="module">The JavaScript module in which to register the refresh callback.</param>
    /// <returns>A task that completes when the registration has finished.</returns>
    private async Task RegisterRefreshCallbackAsync(IJSObjectReference module)
    {
        if (_dotNetRef is not null)
            return;

        _dotNetRef = DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("registerRefreshCallback", _dotNetRef);
    }

    /// <summary>
    /// Ensures that a subscription to the <see cref="NavigationManager.LocationChanged"/> event is active.
    /// Subscribes to location changes if not already subscribed, so that navigation events trigger refreshes.
    /// </summary>
    private void EnsureLocationSubscription()
    {
        if (_locationSubscribed)
            return;

        navigationManager.LocationChanged += OnLocationChanged;
        _locationSubscribed = true;
    }

    /// <summary>
    /// Handles the <see cref="NavigationManager.LocationChanged"/> event by triggering a refresh request.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="e">The <see cref="LocationChangedEventArgs"/> containing event data.</param>
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => RequestRefresh();

    /// <summary>
    /// Schedules a debounced component tree capture operation.
    /// Cancels any pending capture (if already scheduled), resets the debounce timer,
    /// and requests a new capture after the debounce interval expires.
    /// Thread-safe guard to ensure only one capture triggers after the last schedule call.
    /// </summary>
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
    /// Awaits a debounce interval before capturing and dispatching the component tree,
    /// unless the operation is cancelled. Intended to be triggered by UI or navigation activity.
    /// </summary>
    /// <param name="token">A cancellation token for preempting the pending capture.</param>
    /// <returns>A task that completes when the debounce delay elapses and the capture completes, or is cancelled.</returns>
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
    /// Captures the current Blazor component tree, serializes it, and dispatches it to the JavaScript DevTools extension.
    /// Ensures thread-safety, avoids redundant dispatches using change hashing, and debounces repeated refresh requests.
    /// </summary>
    /// <param name="token">A cancellation token to abort the ongoing capture or dispatch operation.</param>
    /// <returns>A task that completes when the capture and dispatch process finishes or is cancelled.</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Only dispatches an updated component tree if a payload hash differs from the previous dispatch.</item>
    /// <item>Handles multiple concurrent trigger attempts using a semaphore.</item>
    /// <item>Catches and swallows interop and circuit disconnect exceptions during the dispatch process.</item>
    /// </list>
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

                JsonElement serializedEnvelope = JsonSerializer.SerializeToElement(envelope, _envelopeSerializerOptions);
                IJSObjectReference module = await _moduleTask.Value;
                await module.InvokeVoidAsync("dispatch", token, serializedEnvelope);
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
        finally
        {
            _captureGate.Release();
        }
    }

    /// <summary>
    /// Captures a snapshot of the component tree for the specified host component,
    /// ensuring the operation runs on the renderer's dispatcher thread if available.
    /// </summary>
    /// <param name="host">The root component whose tree is to be captured.</param>
    /// <returns>
    /// A <see cref="Task{TResult}"/> with a <see cref="ComponentTreeUpdatePayload"/> containing the tree snapshot,
    /// or <c>null</c> if capture fails or an exception occurs.
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
    /// Computes a SHA-256 hash of the serialized <see cref="ComponentTreeUpdatePayload"/>.
    /// </summary>
    /// <param name="payload">The component tree payload to hash.</param>
    /// <returns>
    /// A hexadecimal string representation of the SHA-256 hash for the given payload.
    /// </returns>
    private static string ComputePayloadHash(ComponentTreeUpdatePayload payload)
    {
        string json = JsonSerializer.Serialize(payload, _payloadHashOptions);
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hashBytes);
    }
}
