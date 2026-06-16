using System.Text;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorDevTools.Client.Inspection;

/// <summary>
/// Builds best-effort CSS selectors from a component's render-tree frames for DOM highlighting.
/// </summary>
internal static class ComponentCssLocatorBuilder
{
    /// <summary>
    /// Builds a CSS selector for the component's first top-level element, optionally scoped under an ancestor id.
    /// </summary>
    /// <param name="frames">Render-tree frames for the component.</param>
    /// <param name="ancestorScopeId">Optional ancestor element id used to narrow ambiguous selectors.</param>
    /// <returns>A CSS selector, or <see langword="null"/> when no element frame is available.</returns>
    public static string? BuildLocator(ReadOnlySpan<RenderTreeFrame> frames, string? ancestorScopeId)
    {
        if (!TryGetFirstElement(frames, out string tag, out string? elementId, out IReadOnlyList<string> classes))
        {
            return null;
        }

        string selector = BuildElementSelector(tag, elementId, classes);

        if (!string.IsNullOrEmpty(ancestorScopeId))
        {
            selector = $"#{EscapeCssIdentifier(ancestorScopeId)} {selector}";
        }

        return selector;
    }

    /// <summary>
    /// Reads the id attribute from the component's first top-level element, if any.
    /// </summary>
    /// <param name="frames">Render-tree frames for the component.</param>
    /// <returns>The element id, or <see langword="null"/> when unavailable.</returns>
    public static string? TryGetElementId(ReadOnlySpan<RenderTreeFrame> frames)
    {
        return TryGetFirstElement(frames, out _, out string? elementId, out _)
            ? elementId
            : null;
    }

    private static bool TryGetFirstElement(
        ReadOnlySpan<RenderTreeFrame> frames,
        out string tag,
        out string? elementId,
        out IReadOnlyList<string> classes)
    {
        tag = string.Empty;
        elementId = null;
        classes = [];

        for (int i = 0; i < frames.Length; i++)
        {
            ref readonly RenderTreeFrame frame = ref frames[i];
            if (frame.FrameType != RenderTreeFrameType.Element)
            {
                continue;
            }

            tag = frame.ElementName;
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            var classList = new List<string>();
            for (int j = i + 1; j < frames.Length; j++)
            {
                ref readonly RenderTreeFrame attributeFrame = ref frames[j];
                if (attributeFrame.FrameType != RenderTreeFrameType.Attribute)
                {
                    break;
                }

                if (string.Equals(attributeFrame.AttributeName, "id", StringComparison.Ordinal))
                {
                    string? idValue = attributeFrame.AttributeValue as string ??
                                      attributeFrame.AttributeValue?.ToString();
                    if (!string.IsNullOrEmpty(idValue))
                    {
                        elementId = idValue;
                    }
                }
                else if (string.Equals(attributeFrame.AttributeName, "class", StringComparison.Ordinal))
                {
                    string? classValue = attributeFrame.AttributeValue as string ??
                                         attributeFrame.AttributeValue?.ToString();
                    if (!string.IsNullOrEmpty(classValue))
                    {
                        foreach (string className in classValue.Split(
                                     ' ',
                                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            classList.Add(className);
                        }
                    }
                }
            }

            classes = classList;
            return true;
        }

        return false;
    }

    private static string BuildElementSelector(
        string tag,
        string? elementId,
        IReadOnlyList<string> classes)
    {
        if (!string.IsNullOrEmpty(elementId))
        {
            return $"#{EscapeCssIdentifier(elementId)}";
        }

        if (classes.Count > 0)
        {
            var builder = new StringBuilder(tag);
            foreach (string className in classes)
            {
                builder.Append('.');
                builder.Append(EscapeCssIdentifier(className));
            }

            return builder.ToString();
        }

        return tag;
    }

    private static string EscapeCssIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('\\');
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
