using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using BlazorDevTools.Client.Protocol;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorDevTools.Client.Inspection;

/// <summary>
/// Reflects into the Blazor renderer to produce a <see cref="ComponentTreeUpdatePayload"/>.
/// </summary>
internal sealed class ReflectionComponentTreeInspector(ParameterValueSerializer valueSerializer)
	: IComponentTreeInspector {
	private const int _maxDepth = 64;
	private const int _maxNodes = 500;
	private const string _syntheticRootId = "__bdt_root__";
	private const string _truncatedNodeId = "__bdt_truncated__";

	private static readonly BindingFlags _propertyFlags =
		BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly ConcurrentDictionary<Type, AnnotatedProperties> _propertyCache = new();

	/// <inheritdoc />
	public ComponentTreeUpdatePayload? CaptureTree(ComponentBase host) {
		Microsoft.AspNetCore.Components.RenderTree.Renderer? renderer = BlazorInternalsAccessor.TryGetRenderer(host);
		if (renderer is null)
			return null;

		IReadOnlyList<object>? snapshot = BlazorInternalsAccessor.TrySnapshotComponentStates(renderer);
		if (snapshot is null || snapshot.Count == 0)
			return null;

		try {
			var nodesById = new Dictionary<int, ComponentStateInfo>(snapshot.Count);
			foreach (object componentState in snapshot) {
				int? id = BlazorInternalsAccessor.TryGetComponentId(componentState);
				if (id is null) {
					continue;
				}

				object? component = BlazorInternalsAccessor.TryGetComponent(componentState);

				if (IsInfrastructureComponent(component))
					continue;

				object? parentState = BlazorInternalsAccessor.TryGetParentState(componentState);
				int? parentId = parentState is null ? null : BlazorInternalsAccessor.TryGetComponentId(parentState);

				nodesById[id.Value] = new ComponentStateInfo(
					Id: id.Value,
					ParentId: parentId,
					ComponentState: componentState,
					Component: component);
			}

			if (nodesById.Count == 0)
				return null;

			var childrenByParentId = new Dictionary<int, List<int>>();
			var rootIds = new List<int>();

			foreach (ComponentStateInfo info in nodesById.Values) {
				if (info.ParentId is null || !nodesById.ContainsKey(info.ParentId.Value)) {
					rootIds.Add(info.Id);
					continue;
				}

				if (!childrenByParentId.TryGetValue(info.ParentId.Value, out List<int>? siblings)) {
					siblings = [];
					childrenByParentId[info.ParentId.Value] = siblings;
				}

				siblings.Add(info.Id);
			}

			var visited = new HashSet<int>();
			var nodeBudget = new NodeBudget(_maxNodes);
			var flatNodes = new List<ComponentNode>();

			switch (rootIds.Count) {
				case 0:
					flatNodes.Add(CreateSyntheticRootNode());
					break;
				case 1:
					EmitFlatSubtree(
						rootIds[0],
						parentId: null,
						nodesById,
						childrenByParentId,
						visited,
						nodeBudget,
						depth: 0,
						flatNodes);
					break;
				default:
					flatNodes.Add(CreateSyntheticRootNode());
					foreach (int rootId in rootIds) {
						if (!nodeBudget.HasRemaining) {
							flatNodes.Add(CreateFlatTruncationNode(_syntheticRootId));
							break;
						}

						EmitFlatSubtree(
							rootId,
							parentId: _syntheticRootId,
							nodesById,
							childrenByParentId,
							visited,
							nodeBudget,
							depth: 1,
							flatNodes);
					}

					break;
			}

			return new ComponentTreeUpdatePayload { Nodes = flatNodes };
		} catch (Exception) {
			return null;
		}
	}

	/// <summary>
	/// Emits a component subtree as flat nodes using depth-first traversal from a root id.
	/// </summary>
	private void EmitFlatSubtree(
		int id,
		string? parentId,
		IReadOnlyDictionary<int, ComponentStateInfo> nodesById,
		IReadOnlyDictionary<int, List<int>> childrenByParentId,
		HashSet<int> visited,
		NodeBudget nodeBudget,
		int depth,
		List<ComponentNode> output) {
		if (!visited.Add(id) || !nodeBudget.TryConsume() || !nodesById.TryGetValue(id, out ComponentStateInfo? info) ||
		    depth >= _maxDepth) {
			output.Add(CreateFlatTruncationNode(parentId));
			return;
		}

		try {
			string nodeId = id.ToString();
			output.Add(BuildNodePayload(info, parentId, nodesById));

			if (childrenByParentId.TryGetValue(id, out List<int>? childIds)) {
				foreach (int childId in childIds) {
					if (!nodeBudget.HasRemaining) {
						output.Add(CreateFlatTruncationNode(nodeId));
						break;
					}

					EmitFlatSubtree(
						childId,
						nodeId,
						nodesById,
						childrenByParentId,
						visited,
						nodeBudget,
						depth + 1,
						output);
				}
			}
		} catch (Exception) {
			output.Add(new ComponentNode {
				Id = id.ToString(),
				Name = "(error)",
				ParentId = parentId,
			});
		}
	}


	/// <summary>
	/// Constructs a <see cref="ComponentNode"/> payload for a given <see cref="ComponentStateInfo"/>.
	/// </summary>
	/// <param name="info">The component state information.</param>
	/// <param name="parentId">The string identifier of the parent component node, or <c>null</c> if this is a root node.</param>
	/// <param name="nodesById">A dictionary of all component state infos indexed by their integer identifier.</param>
	/// <returns>
	/// A <see cref="ComponentNode"/> representing the current component, containing extracted parameters,
	/// injected services, and a best-effort CSS locator.
	/// </returns>
	private ComponentNode BuildNodePayload(
		ComponentStateInfo info,
		string? parentId,
		IReadOnlyDictionary<int, ComponentStateInfo> nodesById) =>
		new() {
			Id = info.Id.ToString(),
			Name = GetComponentName(info.Component),
			ParentId = parentId,
			Parameters = ExtractParameters(info.Component),
			Injections = ExtractInjections(info.Component),
			Locator = BuildLocator(info, nodesById),
		};

	/// <summary>
	/// Creates a synthetic root <see cref="ComponentNode"/> representing the root of the component tree.
	/// </summary>
	/// <returns>
	/// A <see cref="ComponentNode"/> instance with a synthetic root identifier and the name "Root".
	/// </returns>
	private static ComponentNode CreateSyntheticRootNode() =>
		new() {
			Id = _syntheticRootId,
			Name = "Root",
		};

	private static ComponentNode CreateFlatTruncationNode(string? parentId) =>
		new() {
			Id = _truncatedNodeId,
			Name = "(...truncated)",
			ParentId = parentId,
		};

	/// <summary>
	/// Gets the simple type name of a component instance, or "(unknown)" if the component is null.
	/// </summary>
	/// <param name="component">The component instance whose type name to retrieve.</param>
	/// <returns>
	/// The simple (unqualified) type name of the component, or "(unknown)" if <paramref name="component"/> is <c>null</c>.
	/// </returns>
	private static string GetComponentName(object? component) =>
		component?.GetType().Name ?? "(unknown)";

	/// <summary>
	/// Determines whether a component is Blazor Dev Tools' own infrastructure and
	/// should be excluded from the inspected tree.
	/// </summary>
	private static bool IsInfrastructureComponent(object? component) =>
		component is DevToolsInitializer;

	/// <summary>
	/// Builds a CSS locator string for a given component by analyzing its render frames and ancestor scope.
	/// </summary>
	/// <param name="info">The <see cref="ComponentStateInfo"/> of the component for which to build a locator.</param>
	/// <param name="nodesById">
	/// A read-only dictionary mapping component IDs to their corresponding <see cref="ComponentStateInfo"/>,
	/// used to look up ancestor information for scope determination.
	/// </param>
	/// <returns>
	/// A CSS locator string that uniquely identifies the component's DOM root, or <c>null</c>
	/// if a locator cannot be constructed (e.g., if render frames are not available).
	/// </returns>
	private static string? BuildLocator(
		ComponentStateInfo info,
		IReadOnlyDictionary<int, ComponentStateInfo> nodesById)
	{
		RenderTreeFrame[]? frames = BlazorInternalsAccessor.TryGetComponentRenderFrames(info.ComponentState);
		if (frames is null || frames.Length == 0)
			return null;

		string? ancestorScopeId = FindAncestorScopeId(info.ParentId, nodesById);
		return ComponentCssLocatorBuilder.BuildLocator(frames.AsSpan(), ancestorScopeId);
	}

	/// <summary>
	/// Traverses the ancestor chain of a component to find the nearest ancestor with a DOM element <c>id</c> (scope identifier).
	/// </summary>
	/// <param name="parentId">The ID of the parent component whose ancestors to search.</param>
	/// <param name="nodesById">
	/// A read-only dictionary mapping component IDs to their corresponding <see cref="ComponentStateInfo"/>.
	/// </param>
	/// <returns>
	/// A <see cref="string"/> containing the ancestor element's <c>id</c> attribute if found; otherwise, <c>null</c>.
	/// </returns>
	private static string? FindAncestorScopeId(
		int? parentId,
		IReadOnlyDictionary<int, ComponentStateInfo> nodesById)
	{
		int? currentParentId = parentId;
		while (currentParentId is int parentComponentId &&
		       nodesById.TryGetValue(parentComponentId, out ComponentStateInfo? parentInfo))
		{
			RenderTreeFrame[]? parentFrames =
				BlazorInternalsAccessor.TryGetComponentRenderFrames(parentInfo.ComponentState);
			if (parentFrames is not null &&
			    ComponentCssLocatorBuilder.TryGetElementId(parentFrames.AsSpan()) is string elementId)
			{
				return elementId;
			}

			currentParentId = parentInfo.ParentId;
		}

		return null;
	}

	/// <summary>
	/// Extracts the public parameters (including <see cref="ParameterAttribute"/> and <see cref="CascadingParameterAttribute"/>)
	/// from the specified component instance, serializing their current values.
	/// </summary>
	/// <param name="component">
	/// The component instance from which to extract parameters. If <c>null</c>, an empty list is returned.
	/// </param>
	/// <returns>
	/// A list of <see cref="ComponentParameter"/> objects representing the parameters and their serialized values.
	/// If a parameter value cannot be read or serialized, the <c>Value</c> will be set to a JSON string "<error>".
	/// </returns>
	private List<ComponentParameter> ExtractParameters(object? component)
	{
		if (component is null)
			return [];

		AnnotatedProperties annotated = _propertyCache.GetOrAdd(component.GetType(), DiscoverAnnotatedProperties);
		var parameters = new List<ComponentParameter>(annotated.Parameters.Length);

		foreach (PropertyInfo property in annotated.Parameters)
		{
			try
			{
				object? value = property.GetValue(component);
				parameters.Add(new ComponentParameter
				{
					Name = property.Name,
					TypeName = property.PropertyType.FullName ?? property.PropertyType.Name,
					Value = valueSerializer.Serialize(value),
				});
			}
			catch (Exception)
			{
				parameters.Add(new ComponentParameter
				{
					Name = property.Name,
					TypeName = property.PropertyType.FullName ?? property.PropertyType.Name,
					Value = JsonSerializer.SerializeToElement("<error>"),
				});
			}
		}

		return parameters;
	}

	/// <summary>
	/// Extracts information about the injected services for the specified component instance.
	/// </summary>
	/// <param name="component">
	/// The component instance from which to extract injected services. If <c>null</c>, an empty list is returned.
	/// </param>
	/// <returns>
	/// A list of <see cref="ComponentInjection"/> objects representing the injected services in the component.
	/// Each item includes the property name, the declared service type, and the resolved implementation type (class name at runtime).
	/// If the implementation type cannot be determined due to an error, <c>ImplementationType</c> will be <c>null</c>.
	/// </returns>
	private static IReadOnlyList<ComponentInjection> ExtractInjections(object? component)
	{
		if (component is null)
			return [];

		AnnotatedProperties annotated = _propertyCache.GetOrAdd(component.GetType(), DiscoverAnnotatedProperties);
		var injections = new List<ComponentInjection>(annotated.Injections.Length);

		foreach (PropertyInfo property in annotated.Injections)
		{
			try
			{
				object? value = property.GetValue(component);
				injections.Add(new ComponentInjection
				{
					Name = property.Name,
					ServiceType = property.PropertyType.FullName ?? property.PropertyType.Name,
					ImplementationType = value?.GetType().FullName,
				});
			}
			catch (Exception)
			{
				injections.Add(new ComponentInjection
				{
					Name = property.Name,
					ServiceType = property.PropertyType.FullName ?? property.PropertyType.Name,
					ImplementationType = null,
				});
			}
		}

		return injections;
	}

	/// <summary>
	/// Discovers and categorizes the properties of a given component type that are annotated
	/// with <see cref="ParameterAttribute"/>, <see cref="CascadingParameterAttribute"/>, or <see cref="InjectAttribute"/>.
	/// </summary>
	/// <param name="componentType">
	/// The <see cref="Type"/> of the component to inspect for annotated properties.
	/// </param>
	/// <returns>
	/// An <see cref="AnnotatedProperties"/> object containing arrays of properties categorized as parameters or injections.
	/// </returns>
	private static AnnotatedProperties DiscoverAnnotatedProperties(Type componentType)
	{
		var parameters = new List<PropertyInfo>();
		var injections = new List<PropertyInfo>();
		var seen = new HashSet<string>(StringComparer.Ordinal);

		for (Type? type = componentType;
		     type is not null && type != typeof(object);
		     type = type.BaseType)
		{
			if (type == typeof(ComponentBase))
				break;

			foreach (PropertyInfo property in type.GetProperties(_propertyFlags))
			{
				if (!property.CanRead || !seen.Add(property.Name))
					continue;

				if (property.GetCustomAttribute<ParameterAttribute>() is not null ||
				    property.GetCustomAttribute<CascadingParameterAttribute>() is not null)
					parameters.Add(property);
				else if (property.GetCustomAttribute<InjectAttribute>() is not null)
					injections.Add(property);
			}
		}

		return new AnnotatedProperties(parameters.ToArray(), injections.ToArray());
	}

	/// <summary>
	/// Represents the state information of a component within the component tree.
	/// </summary>
	/// <param name="Id">The unique identifier of the component node.</param>
	/// <param name="ParentId">The unique identifier of the parent component node, or <c>null</c> if this is a root node.</param>
	/// <param name="ComponentState">The runtime state object of the component, typically containing parameters and internal data.</param>
	/// <param name="Component">The instance of the component (if available); may be <c>null</c> if inspection is limited.</param>
	private sealed record ComponentStateInfo(
		int Id,
		int? ParentId,
		object ComponentState,
		object? Component);

	/// <summary>
	/// Represents categorized properties of a Blazor component discovered by reflection.
	/// </summary>
	/// <param name="Parameters">
	/// An array of <see cref="PropertyInfo"/> objects representing properties annotated as parameters,
	/// i.e., those marked with <see cref="ParameterAttribute"/> or <see cref="CascadingParameterAttribute"/>.
	/// </param>
	/// <param name="Injections">
	/// An array of <see cref="PropertyInfo"/> objects representing properties annotated with <see cref="InjectAttribute"/>.
	/// </param>
	private sealed record AnnotatedProperties(
		PropertyInfo[] Parameters,
		PropertyInfo[] Injections);

	/// <summary>
	/// Represents a simple budget for limiting the number of component nodes that may be processed.
	/// Used to enforce an upper bound during component tree reflection to prevent
	/// performance issues or excessive depth traversal.
	/// </summary>
	/// <remarks>
	/// The budget is decremented each time a node is consumed. When depleted, no further nodes should be traversed.
	/// </remarks>
	private sealed class NodeBudget
	{
		private int _remaining;

		/// <summary>
		/// Initializes a new instance of the <see cref="NodeBudget"/> class with the specified maximum number of nodes.
		/// </summary>
		/// <param name="maxNodes">The maximum number of nodes that may be processed.</param>
		public NodeBudget(int maxNodes)
		{
			_remaining = maxNodes;
		}

		/// <summary>
		/// Gets a value indicating whether there is remaining budget to process at least one more node.
		/// </summary>
		public bool HasRemaining => _remaining > 0;

		/// <summary>
		/// Attempts to consume one unit from the node budget.
		/// </summary>
		/// <returns>
		/// <c>true</c> if a unit was successfully consumed and there is remaining budget; otherwise, <c>false</c>.
		/// </returns>
		public bool TryConsume()
		{
			if (_remaining <= 0)
				return false;

			_remaining--;
			return true;
		}
	}
}
