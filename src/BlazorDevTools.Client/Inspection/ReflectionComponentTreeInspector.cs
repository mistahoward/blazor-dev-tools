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

			ComponentNode root = rootIds.Count switch {
				0 => CreateSyntheticRoot([]),
				1 => BuildNode(rootIds[0], nodesById, childrenByParentId, visited, nodeBudget, depth: 0),
				_ => CreateSyntheticRoot(rootIds
					.Select(id => BuildNode(id, nodesById, childrenByParentId, visited, nodeBudget, depth: 1))
					.ToList()),
			};

			return new ComponentTreeUpdatePayload { Root = root };
		} catch (Exception) {
			return null;
		}
	}

	/// <summary>
	/// Recursively builds a <see cref="ComponentNode"/> representing a single component and its subtree, including its parameters, dependency injections, children, and relevant locator information.
	/// This method applies depth and node budget constraints and gracefully creates truncation nodes or error nodes as needed to prevent excessive tree size or stack overflows.
	/// </summary>
	/// <param name="id">The unique identifier of the component node to build.</param>
	/// <param name="nodesById">A mapping from component IDs to their corresponding <see cref="ComponentStateInfo"/>.</param>
	/// <param name="childrenByParentId">A map where each parent component ID maps to a list of its direct child component IDs.</param>
	/// <param name="visited">A set tracking component IDs that have already been visited, preventing cycles.</param>
	/// <param name="nodeBudget">The current <see cref="NodeBudget"/>, limiting the total number of nodes in the tree.</param>
	/// <param name="depth">The current recursion depth, used to enforce max tree depth restrictions.</param>
	/// <returns>
	/// A <see cref="ComponentNode"/> representing the component and its children, or a special truncation node if limits are reached, or an error node if an exception occurs.
	/// </returns>
	private ComponentNode BuildNode(
		int id,
		IReadOnlyDictionary<int, ComponentStateInfo> nodesById,
		IReadOnlyDictionary<int, List<int>> childrenByParentId,
		HashSet<int> visited,
		NodeBudget nodeBudget,
		int depth) {
		if (!visited.Add(id) || !nodeBudget.TryConsume() || !nodesById.TryGetValue(id, out ComponentStateInfo? info) ||
		    depth >= _maxDepth)
			return CreateTruncationNode();

		try {
			string name = GetComponentName(info.Component);
			IReadOnlyList<ComponentParameter> parameters = ExtractParameters(info.Component);
			IReadOnlyList<ComponentInjection> injections = ExtractInjections(info.Component);
			string? locator = BuildLocator(info, nodesById);

			var children = new List<ComponentNode>();
			if (childrenByParentId.TryGetValue(id, out List<int>? childIds)) {
				foreach (int childId in childIds) {
					if (!nodeBudget.HasRemaining) {
						children.Add(CreateTruncationNode());
						break;
					}

					children.Add(BuildNode(
						childId,
						nodesById,
						childrenByParentId,
						visited,
						nodeBudget,
						depth + 1));
				}
			}

			return new ComponentNode {
				Id = id.ToString(),
				Name = name,
				Children = children,
				Parameters = parameters,
				Injections = injections,
				Locator = locator,
			};
		} catch (Exception) {
			return new ComponentNode {
				Id = id.ToString(),
				Name = "(error)",
				Children = [],
			};
		}
	}

	/// <summary>
	/// Creates a synthetic root node to serve as the top-level parent in the component tree.
	/// This is used as a container for the discovered component nodes.
	/// </summary>
	/// <param name="children">A list of child <see cref="ComponentNode"/> instances to attach under the root.</param>
	/// <returns>
	/// A <see cref="ComponentNode"/> representing the synthetic root with the specified children.
	/// </returns>
	private static ComponentNode CreateSyntheticRoot(IReadOnlyList<ComponentNode> children) =>
		new() {
			Id = _syntheticRootId,
			Name = "Root",
			Children = children.ToList(),
		};

	/// <summary>
	/// Creates a truncation node used to indicate that the component tree has been truncated due to budget or depth limits.
	/// </summary>
	/// <remarks>
	/// This node is added as a child when the node processing budget is exceeded, signaling to the consumer that not all components were included.
	/// </remarks>
	/// <returns>
	/// A <see cref="ComponentNode"/> representing a truncation marker in the rendered component tree.
	/// </returns>
	private static ComponentNode CreateTruncationNode() =>
		new() {
			Id = _truncatedNodeId,
			Name = "(...truncated)",
			Children = [],
		};

	/// <summary>
	/// Retrieves the type name of the specified component instance for display in the component tree.
	/// </summary>
	/// <param name="component">The component instance whose type name is to be retrieved.</param>
	/// <returns>
	/// The type name of the component if the instance is non-null; otherwise, <c>(unknown)</c>.
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
	/// Builds a CSS locator string for a given component using its render-tree frames and optionally scopes it under an ancestor id.
	/// </summary>
	/// <param name="info">The <see cref="ComponentStateInfo"/> of the component for which to build the locator.</param>
	/// <param name="nodesById">A dictionary mapping component ids to their corresponding <see cref="ComponentStateInfo"/> nodes.</param>
	/// <returns>
	/// A CSS selector string for the first element in the component, optionally scoped by an ancestor id,
	/// or <see langword="null"/> if render frames are unavailable or empty.
	/// </returns>
	private string? BuildLocator(
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
	/// Traverses ancestor components to locate the nearest component with an element id, for use as a CSS scoping ancestor.
	/// </summary>
	/// <param name="parentId">
	/// The id of the immediate parent component node, or <see langword="null"/> to indicate there is no parent.
	/// </param>
	/// <param name="nodesById">
	/// A dictionary mapping component node ids to their <see cref="ComponentStateInfo"/> objects.
	/// </param>
	/// <returns>
	/// The <c>id</c> attribute of the first ancestor with a top-level render-tree element having an id, or <see langword="null"/> if none found.
	/// </returns>
	private static string? FindAncestorScopeId(
		int? parentId,
		IReadOnlyDictionary<int, ComponentStateInfo> nodesById)
	{
		int? currentParentId = parentId;
		while (currentParentId is int parentComponentId &&
		       nodesById.TryGetValue(parentComponentId, out ComponentStateInfo? parentInfo)) {
			RenderTreeFrame[]? parentFrames =
				BlazorInternalsAccessor.TryGetComponentRenderFrames(parentInfo.ComponentState);
			if (parentFrames is not null &&
			    ComponentCssLocatorBuilder.TryGetElementId(parentFrames.AsSpan()) is string elementId) {
				return elementId;
			}

			currentParentId = parentInfo.ParentId;
		}

		return null;
	}

	/// <summary>
	/// Extracts all parameter properties (annotated with <see cref="ParameterAttribute"/>
	/// or <see cref="CascadingParameterAttribute"/>) from the given component instance and serializes their values.
	/// </summary>
	/// <param name="component">The Blazor component instance from which to extract parameters. May be <c>null</c>.</param>
	/// <returns>
	/// A list of <see cref="ComponentParameter"/> objects representing the parameters and their
	/// serialized values; returns an empty list if <paramref name="component"/> is <c>null</c> or has no parameters.
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
	/// Extracts all injected service properties (annotated with <see cref="InjectAttribute"/>) from the specified component instance.
	/// </summary>
	/// <param name="component">
	/// The Blazor component instance from which to extract injected services. May be <c>null</c>.
	/// </param>
	/// <returns>
	/// A read-only list of <see cref="ComponentInjection"/> objects representing each injected property,
	/// with its name, service type, and the actual implementation type (if available).
	/// Returns an empty list if <paramref name="component"/> is <c>null</c> or has no injections.
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
	/// Discovers and collects all parameter and injection properties annotated with <see cref="ParameterAttribute"/>,
	/// <see cref="CascadingParameterAttribute"/>, or <see cref="InjectAttribute"/> for a given component type.
	/// </summary>
	/// <param name="componentType">
	/// The type of the Blazor component to inspect for annotated properties.
	/// </param>
	/// <returns>
	/// An <see cref="AnnotatedProperties"/> object containing arrays of properties annotated as parameters or injections.
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
	/// Represents a snapshot of a Blazor internal <c>ComponentState</c> instance, including its unique ID, parent state, and associated component instance.
	/// </summary>
	/// <param name="Id">The unique component identifier within the renderer.</param>
	/// <param name="ParentId">The identifier of the parent component state, or <see langword="null"/> if this is a root component.</param>
	/// <param name="ComponentState">A reflected instance of Blazor's internal <c>ComponentState</c> for this component.</param>
	/// <param name="Component">The associated <see cref="ComponentBase"/> instance, or <see langword="null"/> if unavailable.</param>
	private sealed record ComponentStateInfo(
		int Id,
		int? ParentId,
		object ComponentState,
		object? Component);

	/// <summary>
	/// Holds arrays of properties from a component type that are annotated as parameters or injections.
	/// </summary>
	/// <param name="Parameters">
	/// The set of properties annotated with <see cref="ParameterAttribute"/> or <see cref="CascadingParameterAttribute"/>.
	/// </param>
	/// <param name="Injections">
	/// The set of properties annotated with <see cref="InjectAttribute"/>.
	/// </param>
	private sealed record AnnotatedProperties(
		PropertyInfo[] Parameters,
		PropertyInfo[] Injections);

	/// <summary>
	/// Represents a budget for limiting the number of nodes processed in a traversal or capture operation.
	/// </summary>
	/// <param name="maxNodes">The maximum number of nodes that can be processed within this budget.</param>
	private sealed class NodeBudget(int maxNodes)
	{
		private int _remaining = maxNodes;

		/// <summary>
		/// Gets a value indicating whether there is remaining budget to process additional nodes.
		/// </summary>
		public bool HasRemaining => _remaining > 0;

		/// <summary>
		/// Attempts to consume one unit of node budget.
		/// </summary>
		/// <returns>
		/// <see langword="true"/> if there was remaining budget and it was decremented; otherwise, <see langword="false"/>.
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
