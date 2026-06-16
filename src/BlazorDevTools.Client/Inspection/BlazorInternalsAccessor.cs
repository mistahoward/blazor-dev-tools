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
internal static class BlazorInternalsAccessor {
	private static readonly string[] _renderHandleFieldNames = ["_renderHandle"];
	private static readonly string[] _rendererFieldNames = ["_renderer"];
	private static readonly string[] _componentStateByIdFieldNames = ["_componentStateById"];
	private static readonly string[] _componentIdPropertyNames = ["ComponentId"];
	private static readonly string[] _componentPropertyNames = ["Component"];
	private static readonly string[] _parentStatePropertyNames = ["ParentComponentState"];

	private static readonly BindingFlags _instanceFlags =
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
	/// Initializes reflection member caches on first use.
	/// </summary>
	[DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(ComponentBase))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(RenderHandle))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(Renderer))]
	[DynamicDependency(
		DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties,
		"Microsoft.AspNetCore.Components.Rendering.ComponentState", "Microsoft.AspNetCore.Components")]
	static BlazorInternalsAccessor() {
	}

	/// <summary>
	/// Resolves the <see cref="Renderer"/> associated with <paramref name="host"/>.
	/// </summary>
	/// <param name="host">A live component instance whose render handle is attached.</param>
	/// <returns>The renderer, or <see langword="null"/> when internals are unavailable.</returns>
	public static Renderer? TryGetRenderer(ComponentBase host) {
		if (!EnsureMembersAvailable()) {
			return null;
		}

		try {
			object? boxedHandle = _renderHandleField!.GetValue(host);
			if (boxedHandle is null) {
				return null;
			}

			return _rendererField!.GetValue(boxedHandle) as Renderer;
		} catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException) {
			Debug.WriteLine($"[BlazorDevTools] TryGetRenderer failed: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Copies component-state entries from the renderer into a detached list safe to enumerate.
	/// </summary>
	/// <param name="renderer">The renderer whose component states should be snapshotted.</param>
	/// <returns>A copied list of component-state objects, or <see langword="null"/> on failure.</returns>
	public static IReadOnlyList<object>? TrySnapshotComponentStates(Renderer renderer) {
		if (!EnsureMembersAvailable())
			return null;

		try {
			object? dictionaryObject = _componentStateByIdField!.GetValue(renderer);
			if (dictionaryObject is not System.Collections.IDictionary dictionary)
				return null;

			var snapshot = new List<object>(dictionary.Count);
			foreach (System.Collections.DictionaryEntry entry in dictionary)
				if (entry.Value is not null)
					snapshot.Add(entry.Value);

			return snapshot;
		} catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException) {
			Debug.WriteLine($"[BlazorDevTools] TrySnapshotComponentStates failed: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Reads the component id from a reflected component-state instance.
	/// </summary>
	/// <param name="componentState">A component-state object from the renderer snapshot.</param>
	/// <returns>The component id, or <see langword="null"/> when unavailable.</returns>
	public static int? TryGetComponentId(object componentState) {
		if (!EnsureMembersAvailable())
			return null;

		try {
			object? value = _componentIdProperty!.GetValue(componentState);
			return value is int id ? id : null;
		} catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException) {
			Debug.WriteLine($"[BlazorDevTools] TryGetComponentId failed: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Reads the component instance from a reflected component-state object.
	/// </summary>
	/// <param name="componentState">A component-state object from the renderer snapshot.</param>
	/// <returns>The component instance, or <see langword="null"/> when unavailable.</returns>
	public static object? TryGetComponent(object componentState) {
		if (!EnsureMembersAvailable())
			return null;

		try {
			return _componentProperty!.GetValue(componentState);
		} catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException) {
			Debug.WriteLine($"[BlazorDevTools] TryGetComponent failed: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Reads the render-tree frames for a reflected component-state instance.
	/// </summary>
	/// <param name="componentState">A component-state object from the renderer snapshot.</param>
	/// <returns>The component's render-tree frames, or <see langword="null"/> when unavailable.</returns>
	public static RenderTreeFrame[]? TryGetComponentRenderFrames(object componentState) {
		if (!EnsureMembersAvailable())
			return null;

		return componentState is ComponentState state
			? ComponentStateRenderTreeAccessor.TryGetRenderFrames(state)
			: null;
	}

	/// <summary>
	/// Reads the parent component-state reference from a reflected component-state object.
	/// </summary>
	/// <param name="componentState">A component-state object from the renderer snapshot.</param>
	/// <returns>The parent component-state object, or <see langword="null"/> for roots.</returns>
	public static object? TryGetParentState(object componentState) {
		if (!EnsureMembersAvailable())
			return null;

		try {
			return _parentStateProperty!.GetValue(componentState);
		} catch (Exception ex) when (ex is TargetException or InvalidOperationException or ArgumentException) {
			Debug.WriteLine($"[BlazorDevTools] TryGetParentState failed: {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Ensures that all necessary reflected members required for accessing Blazor internals are available.
	/// This method resolves and caches field and property information for the target Blazor framework types
	/// using possible candidate names, to be resilient across framework versions.
	/// </summary>
	/// <remarks>
	/// This method will only attempt reflection once per application lifetime. Once resolved, member availability
	/// is cached. If any of the critical members cannot be resolved, Blazor Dev Tools functionality is disabled.
	/// </remarks>
	/// <returns>
	/// <see langword="true"/> if all required internal Blazor members have been resolved and are available;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	private static bool EnsureMembersAvailable() {
		if (_membersResolved)
			return _membersAvailable;

		_membersResolved = true;

		_renderHandleField = ResolveField(typeof(ComponentBase), _renderHandleFieldNames);
		_rendererField = ResolveField(typeof(RenderHandle), _rendererFieldNames);
		_componentStateByIdField = ResolveField(typeof(Renderer), _componentStateByIdFieldNames);

		Type? componentStateType = ResolveComponentStateType(_componentStateByIdField);
		if (componentStateType is not null) {
			_componentIdProperty = ResolveProperty(componentStateType, _componentIdPropertyNames);
			_componentProperty = ResolveProperty(componentStateType, _componentPropertyNames);
			_parentStateProperty = ResolveProperty(componentStateType, _parentStatePropertyNames);
		}

		_membersAvailable =
			_renderHandleField is not null &&
			_rendererField is not null &&
			_componentStateByIdField is not null &&
			_componentIdProperty is not null &&
			_componentProperty is not null &&
			_parentStateProperty is not null;

		if (!_membersAvailable)
			Debug.WriteLine(
				"[BlazorDevTools] Blazor internals reflection disabled: one or more members could not be resolved.");

		return _membersAvailable;
	}

	/// <summary>
	/// Attempts to resolve the <c>ComponentState</c> type used internally by the Blazor <see cref="Renderer"/>.
	/// </summary>
	/// <param name="componentStateByIdField">
	/// The <see cref="FieldInfo"/> pointing to the field holding the component state mapping, typically a
	/// <c>Dictionary&lt;int, ComponentState&gt;</c>.
	/// </param>
	/// <returns>
	/// The <see cref="Type"/> corresponding to the internal <c>ComponentState</c> class if resolved successfully;
	/// otherwise, <see langword="null"/>.
	/// </returns>
	/// <remarks>
	/// <para>
	/// This method first attempts to determine the <c>ComponentState</c> type by examining the value type of the
	/// dictionary field, which is resilient to namespace or assembly changes between Blazor versions.
	/// </para>
	/// <para>
	/// If this approach fails, it falls back to probing known fully-qualified type names that
	/// have been used for <c>ComponentState</c> across supported versions of Blazor.
	/// </para>
	/// </remarks>
	private static Type? ResolveComponentStateType(FieldInfo? componentStateByIdField) {
		if (componentStateByIdField?.FieldType is not { IsGenericType: true } fieldType)
			return typeof(Renderer).Assembly.GetType("Microsoft.AspNetCore.Components.Rendering.ComponentState")
			       ?? typeof(Renderer).Assembly.GetType("Microsoft.AspNetCore.Components.RenderTree.ComponentState");

		Type[] arguments = fieldType.GetGenericArguments();
		if (arguments.Length == 2)
			return arguments[1];

		return typeof(Renderer).Assembly.GetType("Microsoft.AspNetCore.Components.Rendering.ComponentState")
		       ?? typeof(Renderer).Assembly.GetType("Microsoft.AspNetCore.Components.RenderTree.ComponentState");
	}

	/// <summary>
	/// Attempts to resolve a <see cref="FieldInfo"/> from the given <paramref name="type"/> using a list of possible field names.
	/// </summary>
	/// <param name="type">The <see cref="Type"/> to inspect for the field.</param>
	/// <param name="candidateNames">A collection of candidate field names to probe.</param>
	/// <returns>
	/// The first <see cref="FieldInfo"/> found with a matching name using the specified binding flags, or <c>null</c> if none are found.
	/// </returns>
	private static FieldInfo? ResolveField(Type type, IReadOnlyList<string> candidateNames) => candidateNames
		.Select(name => type.GetField(name, _instanceFlags)).OfType<FieldInfo>().FirstOrDefault();

	/// <summary>
	/// Attempts to resolve a <see cref="PropertyInfo"/> from the specified <paramref name="type"/>
	/// using a list of possible property names.
	/// </summary>
	/// <param name="type">The <see cref="Type"/> to inspect for the property.</param>
	/// <param name="candidateNames">A collection of candidate property names to probe.</param>
	/// <returns>
	/// The first <see cref="PropertyInfo"/> found with a matching name using the specified binding flags,
	/// or <c>null</c> if none are found.
	/// </returns>
	private static PropertyInfo? ResolveProperty(Type type, IReadOnlyList<string> candidateNames) =>
		candidateNames.Select(name => type.GetProperty(name, _instanceFlags))
			.OfType<PropertyInfo>()
			.FirstOrDefault();
}
