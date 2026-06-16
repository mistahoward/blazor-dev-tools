using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorDevTools.Client.Inspection;
using BlazorDevTools.Client.Protocol;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
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
    BlazorInternalsAccessor internalsAccessor) : IBlazorDevToolsService, IAsyncDisposable
{
    private const int DebounceMilliseconds = 250;

    private static readonly JsonSerializerOptions EnvelopeSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions PayloadHashOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
        "import", "./_content/BlazorDevTools.Client/blazorDevToolsInterop.js").AsTask());

    private readonly object _debounceLock = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);

    private ComponentBase? _host;
    private CancellationTokenSource? _debounceCts;
    private DotNetObjectReference<BlazorDevToolsService>? _dotNetRef;
    private bool _disposed;
    private bool _locationSubscribed;
    private bool _pendingRefresh;
    private string? _lastPayloadHash;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsInitialized { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync(ComponentBase host)
    {
        if (!IsEnabled || IsInitialized)
        {
            return;
        }

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
        {
            return;
        }

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

    private async Task RegisterRefreshCallbackAsync(IJSObjectReference module)
    {
        if (_dotNetRef is not null)
        {
            return;
        }

        _dotNetRef = DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("registerRefreshCallback", _dotNetRef);
    }

    private void EnsureLocationSubscription()
    {
        if (_locationSubscribed)
        {
            return;
        }

        navigationManager.LocationChanged += OnLocationChanged;
        _locationSubscribed = true;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => RequestRefresh();

    private void ScheduleDebouncedCapture()
    {
        lock (_debounceLock)
        {
            if (_disposed)
            {
                return;
            }

            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            CancellationToken token = _debounceCts.Token;
            _ = DebouncedCaptureAsync(token);
        }
    }

    private async Task DebouncedCaptureAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, token);
            await CaptureAndDispatchAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Debounce superseded or service disposed.
        }
    }

    private async Task CaptureAndDispatchAsync(CancellationToken token)
    {
        if (_disposed || token.IsCancellationRequested || _host is null)
        {
            return;
        }

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
                {
                    break;
                }

                ComponentTreeUpdatePayload? payload = await CaptureTreeOnDispatcherAsync(_host);
                if (payload is null || _disposed || token.IsCancellationRequested)
                {
                    continue;
                }

                string payloadHash = ComputePayloadHash(payload);
                if (payloadHash == _lastPayloadHash)
                {
                    continue;
                }

                _lastPayloadHash = payloadHash;

                DevToolsEnvelope<ComponentTreeUpdatePayload> envelope = new()
                {
                    Protocol = DevToolsProtocol.Name,
                    Version = DevToolsProtocol.Version,
                    Type = DevToolsMessageType.ComponentTreeUpdate,
                    Payload = payload,
                };

                JsonElement serializedEnvelope = JsonSerializer.SerializeToElement(envelope, EnvelopeSerializerOptions);
                IJSObjectReference module = await _moduleTask.Value;
                await module.InvokeVoidAsync("dispatch", serializedEnvelope);
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

    private async Task<ComponentTreeUpdatePayload?> CaptureTreeOnDispatcherAsync(ComponentBase host)
    {
        try
        {
            Microsoft.AspNetCore.Components.RenderTree.Renderer? renderer = internalsAccessor.TryGetRenderer(host);
            if (renderer is null)
            {
                return treeInspector.CaptureTree(host);
            }

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

    private static string ComputePayloadHash(ComponentTreeUpdatePayload payload)
    {
        string json = JsonSerializer.Serialize(payload, PayloadHashOptions);
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hashBytes);
    }
}
