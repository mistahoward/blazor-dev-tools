using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorDevTools.Client.Inspection;

/// <summary>
/// Version-tolerant, cached reflection into Blazor renderer internals.
/// All lookups fail closed: missing members disable the feature gracefully.
/// </summary>
internal sealed class BlazorInternalsAccessor
{
    /// <summary>
    /// Initializes reflection member caches on first use.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(ComponentBase))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(RenderHandle))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(Renderer))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties, "Microsoft.AspNetCore.Components.Rendering.ComponentState", "Microsoft.AspNetCore.Components")]
    public BlazorInternalsAccessor()
    {
    }

    private static readonly string[] RenderHandleFieldNames = ["_renderHandle"];
    private static readonly string[] RendererFieldNames = ["_renderer"];
    private static readonly string[] ComponentStateByIdFieldNames = ["_componentStateById"];
    private static readonly string[] ComponentIdPropertyNames = ["ComponentId"];
    private static readonly string[] ComponentPropertyNames = ["Component"];
    private static readonly string[] ParentStatePropertyNames = ["ParentComponentState"];

    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldInfo? _renderHandleField;
    private static FieldInfo? _rendererField;
    private static FieldInfo? _componentStateByIdField;
    private static PropertyInfo? _componentIdProperty;
    private static PropertyInfo? _componentProperty;
    private static PropertyInfo? _parentStateProperty;
    private static bool _membersResolved;
    private static bool _membersAvailable;

    /// <summary>
    /// Resolves the <see cref="Renderer"/> associated with <paramref name="host"/>.
    /// </summary>
    /// <param name="host">A live component instance whose render handle is attached.</param>
    /// <returns>The renderer, or <see langword="null"/> when internals are unavailable.</returns>
    public Renderer? TryGetRenderer(ComponentBase host)
    {
        if (!EnsureMembersAvailable())
        {
            return null;
        }

        try
        {
            object? boxedHandle = _renderHandleField!.GetValue(host);
            if (boxedHandle is null)
            {
                return null;
            }

            return _rendererField!.GetValue(boxedHandle) as Renderer;
        }
        catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"[BlazorDevTools] TryGetRenderer failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Copies component-state entries from the renderer into a detached list safe to enumerate.
    /// </summary>
    /// <param name="renderer">The renderer whose component states should be snapshotted.</param>
    /// <returns>A copied list of component-state objects, or <see langword="null"/> on failure.</returns>
    public IReadOnlyList<object>? TrySnapshotComponentStates(Renderer renderer)
    {
        if (!EnsureMembersAvailable())
        {
            return null;
        }

        try
        {
            object? dictionaryObject = _componentStateByIdField!.GetValue(renderer);
            if (dictionaryObject is not System.Collections.IDictionary dictionary)
            {
                return null;
            }

            var snapshot = new List<object>(dictionary.Count);
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (entry.Value is not null)
                {
                    snapshot.Add(entry.Value);
                }
            }

            return snapshot;
        }
        catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"[BlazorDevTools] TrySnapshotComponentStates failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads the component id from a reflected component-state instance.
    /// </summary>
    /// <param name="componentState">A component-state object from the renderer snapshot.</param>
    /// <returns>The component id, or <see langword="null"/> when unavailable.</returns>
    public int? TryGetComponentId(object componentState)
    {
        if (!EnsureMembersAvailable())
        {
            return null;
        }

        try
        {
            object? value = _componentIdProperty!.GetValue(componentState);
            return value is int id ? id : null;
        }
        catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"[BlazorDevTools] TryGetComponentId failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads the component instance from a reflected component-state object.
    /// </summary>
    /// <param name="componentState">A component-state object from the renderer snapshot.</param>
    /// <returns>The component instance, or <see langword="null"/> when unavailable.</returns>
    public object? TryGetComponent(object componentState)
    {
        if (!EnsureMembersAvailable())
        {
            return null;
        }

        try
        {
            return _componentProperty!.GetValue(componentState);
        }
        catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"[BlazorDevTools] TryGetComponent failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads the render-tree frames for a reflected component-state instance.
    /// </summary>
    /// <param name="componentState">A component-state object from the renderer snapshot.</param>
    /// <returns>The component's render-tree frames, or <see langword="null"/> when unavailable.</returns>
    public RenderTreeFrame[]? TryGetComponentRenderFrames(object componentState)
    {
        if (!EnsureMembersAvailable())
        {
            return null;
        }

        return componentState is ComponentState state
            ? ComponentStateRenderTreeAccessor.TryGetRenderFrames(state)
            : null;
    }

    /// <summary>
    /// Reads the parent component-state reference from a reflected component-state object.
    /// </summary>
    /// <param name="componentState">A component-state object from the renderer snapshot.</param>
    /// <returns>The parent component-state object, or <see langword="null"/> for roots.</returns>
    public object? TryGetParentState(object componentState)
    {
        if (!EnsureMembersAvailable())
        {
            return null;
        }

        try
        {
            return _parentStateProperty!.GetValue(componentState);
        }
        catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException)
        {
            Debug.WriteLine($"[BlazorDevTools] TryGetParentState failed: {ex.Message}");
            return null;
        }
    }

    private static bool EnsureMembersAvailable()
    {
        if (_membersResolved)
        {
            return _membersAvailable;
        }

        _membersResolved = true;

        _renderHandleField = ResolveField(typeof(ComponentBase), RenderHandleFieldNames);
        _rendererField = ResolveField(typeof(RenderHandle), RendererFieldNames);
        _componentStateByIdField = ResolveField(typeof(Renderer), ComponentStateByIdFieldNames);

        Type? componentStateType = ResolveComponentStateType(_componentStateByIdField);
        if (componentStateType is not null)
        {
            _componentIdProperty = ResolveProperty(componentStateType, ComponentIdPropertyNames);
            _componentProperty = ResolveProperty(componentStateType, ComponentPropertyNames);
            _parentStateProperty = ResolveProperty(componentStateType, ParentStatePropertyNames);
        }

        _membersAvailable =
            _renderHandleField is not null &&
            _rendererField is not null &&
            _componentStateByIdField is not null &&
            _componentIdProperty is not null &&
            _componentProperty is not null &&
            _parentStateProperty is not null;

        if (!_membersAvailable)
        {
            Debug.WriteLine("[BlazorDevTools] Blazor internals reflection disabled: one or more members could not be resolved.");
        }

        return _membersAvailable;
    }

    private static Type? ResolveComponentStateType(FieldInfo? componentStateByIdField)
    {
        // Preferred: derive the ComponentState type from the Dictionary<int, ComponentState>
        // value type. This is resilient to namespace/assembly moves across Blazor versions.
        if (componentStateByIdField?.FieldType is { IsGenericType: true } fieldType)
        {
            Type[] arguments = fieldType.GetGenericArguments();
            if (arguments.Length == 2)
            {
                return arguments[1];
            }
        }

        // Fallback: probe known fully-qualified names across versions.
        return typeof(Renderer).Assembly.GetType("Microsoft.AspNetCore.Components.Rendering.ComponentState")
            ?? typeof(Renderer).Assembly.GetType("Microsoft.AspNetCore.Components.RenderTree.ComponentState");
    }

    private static FieldInfo? ResolveField(Type type, IReadOnlyList<string> candidateNames)
    {
        foreach (string name in candidateNames)
        {
            FieldInfo? field = type.GetField(name, InstanceFlags);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static PropertyInfo? ResolveProperty(Type type, IReadOnlyList<string> candidateNames)
    {
        foreach (string name in candidateNames)
        {
            PropertyInfo? property = type.GetProperty(name, InstanceFlags);
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }
}
