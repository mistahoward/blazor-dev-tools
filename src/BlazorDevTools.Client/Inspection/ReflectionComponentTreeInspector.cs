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
internal sealed class ReflectionComponentTreeInspector(
    BlazorInternalsAccessor accessor,
    ParameterValueSerializer valueSerializer) : IComponentTreeInspector
{
    private const int MaxDepth = 64;
    private const int MaxNodes = 500;
    private const string SyntheticRootId = "__bdt_root__";
    private const string TruncatedNodeId = "__bdt_truncated__";

    private static readonly BindingFlags PropertyFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly ConcurrentDictionary<Type, AnnotatedProperties> PropertyCache = new();

    /// <inheritdoc />
    public ComponentTreeUpdatePayload? CaptureTree(ComponentBase host)
    {
        if (host is null)
        {
            return null;
        }

        Microsoft.AspNetCore.Components.RenderTree.Renderer? renderer = accessor.TryGetRenderer(host);
        if (renderer is null)
        {
            return null;
        }

        IReadOnlyList<object>? snapshot = accessor.TrySnapshotComponentStates(renderer);
        if (snapshot is null || snapshot.Count == 0)
        {
            return null;
        }

        try
        {
            var nodesById = new Dictionary<int, ComponentStateInfo>(snapshot.Count);
            foreach (object componentState in snapshot)
            {
                int? id = accessor.TryGetComponentId(componentState);
                if (id is null)
                {
                    continue;
                }

                object? component = accessor.TryGetComponent(componentState);

                // The DevTools host component is always present; hide it from the tree.
                if (IsInfrastructureComponent(component))
                {
                    continue;
                }

                object? parentState = accessor.TryGetParentState(componentState);
                int? parentId = parentState is null ? null : accessor.TryGetComponentId(parentState);

                nodesById[id.Value] = new ComponentStateInfo(
                    Id: id.Value,
                    ParentId: parentId,
                    ComponentState: componentState,
                    Component: component);
            }

            if (nodesById.Count == 0)
            {
                return null;
            }

            var childrenByParentId = new Dictionary<int, List<int>>();
            var rootIds = new List<int>();

            foreach (ComponentStateInfo info in nodesById.Values)
            {
                if (info.ParentId is null || !nodesById.ContainsKey(info.ParentId.Value))
                {
                    rootIds.Add(info.Id);
                    continue;
                }

                if (!childrenByParentId.TryGetValue(info.ParentId.Value, out List<int>? siblings))
                {
                    siblings = [];
                    childrenByParentId[info.ParentId.Value] = siblings;
                }

                siblings.Add(info.Id);
            }

            var visited = new HashSet<int>();
            var nodeBudget = new NodeBudget(MaxNodes);

            ComponentNode root = rootIds.Count switch
            {
                0 => CreateSyntheticRoot([]),
                1 => BuildNode(rootIds[0], nodesById, childrenByParentId, visited, nodeBudget, depth: 0),
                _ => CreateSyntheticRoot(rootIds
                    .Select(id => BuildNode(id, nodesById, childrenByParentId, visited, nodeBudget, depth: 1))
                    .ToList()),
            };

            return new ComponentTreeUpdatePayload { Root = root };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ComponentNode BuildNode(
        int id,
        IReadOnlyDictionary<int, ComponentStateInfo> nodesById,
        IReadOnlyDictionary<int, List<int>> childrenByParentId,
        HashSet<int> visited,
        NodeBudget nodeBudget,
        int depth)
    {
        if (!visited.Add(id) || !nodeBudget.TryConsume())
        {
            return CreateTruncationNode();
        }

        if (!nodesById.TryGetValue(id, out ComponentStateInfo? info))
        {
            return CreateTruncationNode();
        }

        if (depth >= MaxDepth)
        {
            return CreateTruncationNode();
        }

        try
        {
            string name = GetComponentName(info.Component);
            IReadOnlyList<ComponentParameter> parameters = ExtractParameters(info.Component);
            IReadOnlyList<ComponentInjection> injections = ExtractInjections(info.Component);
            string? locator = BuildLocator(info, nodesById);

            var children = new List<ComponentNode>();
            if (childrenByParentId.TryGetValue(id, out List<int>? childIds))
            {
                foreach (int childId in childIds)
                {
                    if (!nodeBudget.HasRemaining)
                    {
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

            return new ComponentNode
            {
                Id = id.ToString(),
                Name = name,
                Children = children,
                Parameters = parameters,
                Injections = injections,
                Locator = locator,
            };
        }
        catch (Exception)
        {
            return new ComponentNode
            {
                Id = id.ToString(),
                Name = "(error)",
                Children = [],
            };
        }
    }

    private static ComponentNode CreateSyntheticRoot(IReadOnlyList<ComponentNode> children) =>
        new()
        {
            Id = SyntheticRootId,
            Name = "Root",
            Children = children.ToList(),
        };

    private static ComponentNode CreateTruncationNode() =>
        new()
        {
            Id = TruncatedNodeId,
            Name = "(...truncated)",
            Children = [],
        };

    private static string GetComponentName(object? component) =>
        component?.GetType().Name ?? "(unknown)";

    /// <summary>
    /// Determines whether a component is Blazor Dev Tools' own infrastructure and
    /// should be excluded from the inspected tree.
    /// </summary>
    private static bool IsInfrastructureComponent(object? component) =>
        component is DevToolsInitializer;

    private string? BuildLocator(
        ComponentStateInfo info,
        IReadOnlyDictionary<int, ComponentStateInfo> nodesById)
    {
        RenderTreeFrame[]? frames = accessor.TryGetComponentRenderFrames(info.ComponentState);
        if (frames is null || frames.Length == 0)
        {
            return null;
        }

        string? ancestorScopeId = FindAncestorScopeId(info.ParentId, nodesById);
        return ComponentCssLocatorBuilder.BuildLocator(frames.AsSpan(), ancestorScopeId);
    }

    private string? FindAncestorScopeId(
        int? parentId,
        IReadOnlyDictionary<int, ComponentStateInfo> nodesById)
    {
        int? currentParentId = parentId;
        while (currentParentId is int parentComponentId &&
               nodesById.TryGetValue(parentComponentId, out ComponentStateInfo? parentInfo))
        {
            RenderTreeFrame[]? parentFrames = accessor.TryGetComponentRenderFrames(parentInfo.ComponentState);
            if (parentFrames is not null &&
                ComponentCssLocatorBuilder.TryGetElementId(parentFrames.AsSpan()) is string elementId)
            {
                return elementId;
            }

            currentParentId = parentInfo.ParentId;
        }

        return null;
    }

    private IReadOnlyList<ComponentParameter> ExtractParameters(object? component)
    {
        if (component is null)
        {
            return [];
        }

        AnnotatedProperties annotated = PropertyCache.GetOrAdd(component.GetType(), DiscoverAnnotatedProperties);
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

    private static IReadOnlyList<ComponentInjection> ExtractInjections(object? component)
    {
        if (component is null)
        {
            return [];
        }

        AnnotatedProperties annotated = PropertyCache.GetOrAdd(component.GetType(), DiscoverAnnotatedProperties);
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
            {
                break;
            }

            foreach (PropertyInfo property in type.GetProperties(PropertyFlags))
            {
                if (!property.CanRead || !seen.Add(property.Name))
                {
                    continue;
                }

                if (property.GetCustomAttribute<ParameterAttribute>() is not null ||
                    property.GetCustomAttribute<CascadingParameterAttribute>() is not null)
                {
                    parameters.Add(property);
                }
                else if (property.GetCustomAttribute<InjectAttribute>() is not null)
                {
                    injections.Add(property);
                }
            }
        }

        return new AnnotatedProperties(parameters.ToArray(), injections.ToArray());
    }

    private sealed record ComponentStateInfo(
        int Id,
        int? ParentId,
        object ComponentState,
        object? Component);

    private sealed record AnnotatedProperties(
        PropertyInfo[] Parameters,
        PropertyInfo[] Injections);

    private sealed class NodeBudget(int maxNodes)
    {
        private int _remaining = maxNodes;

        public bool HasRemaining => _remaining > 0;

        public bool TryConsume()
        {
            if (_remaining <= 0)
            {
                return false;
            }

            _remaining--;
            return true;
        }
    }
}
