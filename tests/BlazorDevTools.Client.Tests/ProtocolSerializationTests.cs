using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorDevTools.Client.Protocol;
using Xunit;

namespace BlazorDevTools.Client.Tests;

public sealed class ProtocolSerializationTests
{
    [Fact]
    public void FlatPayload_Serializes_WithManyNodes()
    {
        List<ComponentNode> nodes = [];
        for (int i = 0; i < 80; i++)
        {
            nodes.Add(new ComponentNode
            {
                Id = i.ToString(),
                Name = $"Node{i}",
                ParentId = i == 0 ? null : (i - 1).ToString(),
            });
        }

        DevToolsEnvelope<ComponentTreeUpdatePayload> envelope = CreateEnvelope(nodes);

        string json = JsonSerializer.Serialize(envelope, DevToolsJsonSerializerOptions.Envelope);

        Assert.Contains("\"nodes\"", json);
        Assert.DoesNotContain("JsonException", json);
    }

    [Fact]
    public void StringArg_SurvivesInteropMaxDepth32()
    {
        List<ComponentNode> nodes = BuildDeepFlatChain(80);
        DevToolsEnvelope<ComponentTreeUpdatePayload> envelope = CreateEnvelope(nodes);

        string json = JsonSerializer.Serialize(envelope, DevToolsJsonSerializerOptions.Envelope);

        JsonSerializerOptions interopLike = CreateInteropLikeOptions();
        string reSerialized = JsonSerializer.Serialize(json, interopLike);

        Assert.False(string.IsNullOrWhiteSpace(reSerialized));
    }

    [Fact]
    public void NestedPayload_ExceedsInteropMaxDepth32()
    {
        NestedLegacyNode root = BuildNestedLegacyChain(20);
        DevToolsEnvelope<NestedLegacyPayload> envelope = new()
        {
            Protocol = DevToolsProtocol.Name,
            Version = 1,
            Type = DevToolsMessageType.ComponentTreeUpdate,
            Payload = new NestedLegacyPayload { Root = root },
        };

        JsonSerializerOptions interopLike = CreateInteropLikeOptions();

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Serialize(envelope, interopLike));
    }

    [Fact]
    public void FlatPayload_JsonDepth_StaysBelowInteropLimit()
    {
        List<ComponentNode> nodes = BuildDeepFlatChain(80);
        DevToolsEnvelope<ComponentTreeUpdatePayload> envelope = CreateEnvelope(nodes);

        string json = JsonSerializer.Serialize(envelope, DevToolsJsonSerializerOptions.Envelope);
        int depth = GetJsonMaxDepth(json);

        Assert.True(depth < 32, $"Expected JSON depth under 32, got {depth}.");
    }

    private static DevToolsEnvelope<ComponentTreeUpdatePayload> CreateEnvelope(
        IReadOnlyList<ComponentNode> nodes) =>
        new()
        {
            Protocol = DevToolsProtocol.Name,
            Version = DevToolsProtocol.Version,
            Type = DevToolsMessageType.ComponentTreeUpdate,
            Payload = new ComponentTreeUpdatePayload { Nodes = nodes },
        };

    private static List<ComponentNode> BuildDeepFlatChain(int count)
    {
        var nodes = new List<ComponentNode>(count);
        for (int i = 0; i < count; i++)
        {
            nodes.Add(new ComponentNode
            {
                Id = i.ToString(),
                Name = $"Node{i}",
                ParentId = i == 0 ? null : (i - 1).ToString(),
            });
        }

        return nodes;
    }

    private static NestedLegacyNode BuildNestedLegacyChain(int depth)
    {
        NestedLegacyNode current = new()
        {
            Id = depth.ToString(),
            Name = $"Node{depth}",
            Children = [],
        };

        for (int i = depth - 1; i >= 0; i--)
        {
            current = new NestedLegacyNode
            {
                Id = i.ToString(),
                Name = $"Node{i}",
                Children = [current],
            };
        }

        return current;
    }

    private static JsonSerializerOptions CreateInteropLikeOptions() =>
        new()
        {
            MaxDepth = 32,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
        };

    private static int GetJsonMaxDepth(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return GetElementMaxDepth(document.RootElement, 1);
    }

    private static int GetElementMaxDepth(JsonElement element, int currentDepth)
    {
        int maxDepth = currentDepth;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    maxDepth = Math.Max(maxDepth, GetElementMaxDepth(property.Value, currentDepth + 1));
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    maxDepth = Math.Max(maxDepth, GetElementMaxDepth(item, currentDepth + 1));
                }

                break;
        }

        return maxDepth;
    }

    private sealed class NestedLegacyPayload
    {
        public required NestedLegacyNode Root { get; init; }
    }

    private sealed class NestedLegacyNode
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required List<NestedLegacyNode> Children { get; init; }
    }
}
