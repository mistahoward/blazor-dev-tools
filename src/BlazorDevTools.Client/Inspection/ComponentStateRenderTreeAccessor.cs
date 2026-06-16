using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorDevTools.Client.Inspection;

/// <summary>
/// Accesses internal <c>ComponentState.CurrentRenderTree</c> via <see cref="UnsafeAccessorAttribute"/>.
/// Standard reflection cannot read internal members of another assembly.
/// </summary>
internal static class ComponentStateRenderTreeAccessor
{
    /// <summary>
    /// Reads the render-tree frames for a reflected component-state instance.
    /// </summary>
    /// <param name="componentState">A component-state object from the renderer snapshot.</param>
    /// <returns>The component's render-tree frames, or <see langword="null"/> when unavailable.</returns>
    public static RenderTreeFrame[]? TryGetRenderFrames(ComponentState componentState)
    {
        try
        {
            RenderTreeBuilder builder = GetCurrentRenderTree(componentState);
            ArrayRange<RenderTreeFrame> range = builder.GetFrames();
            if (range.Count <= 0)
                return [];

            if (range.Count == range.Array.Length)
                return range.Array;

            var snapshot = new RenderTreeFrame[range.Count];
            Array.Copy(range.Array, snapshot, range.Count);
            return snapshot;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_CurrentRenderTree")]
    private static extern RenderTreeBuilder GetCurrentRenderTree(ComponentState componentState);
}
