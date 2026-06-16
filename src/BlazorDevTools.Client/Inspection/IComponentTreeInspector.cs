using BlazorDevTools.Client.Protocol;
using Microsoft.AspNetCore.Components;

namespace BlazorDevTools.Client.Inspection;

/// <summary>
/// Captures the live Blazor component tree from the renderer associated with a host component.
/// </summary>
public interface IComponentTreeInspector
{
    /// <summary>
    /// Builds a component tree snapshot from the full renderer state reachable via <paramref name="host"/>.
    /// </summary>
    /// <param name="host">
    /// A live <see cref="ComponentBase"/> instance used only to reach the shared renderer;
    /// the returned tree includes all component states in that renderer, not a subtree of <paramref name="host"/>.
    /// </param>
    /// <returns>
    /// A <see cref="ComponentTreeUpdatePayload"/> when reflection succeeds, or <see langword="null"/>
    /// when internals are unavailable or the snapshot cannot be taken safely.
    /// </returns>
    ComponentTreeUpdatePayload? CaptureTree(ComponentBase host);
}
