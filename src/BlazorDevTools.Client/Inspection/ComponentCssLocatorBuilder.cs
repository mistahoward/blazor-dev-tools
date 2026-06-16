using System.Text;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorDevTools.Client.Inspection;

/// <summary>
/// Builds best-effort CSS selectors from a component's render-tree frames for DOM highlighting.
/// </summary>
internal static class ComponentCssLocatorBuilder {
	/// <summary>
	/// Builds a CSS selector for the component's first top-level element, optionally scoped under an ancestor id.
	/// </summary>
	/// <param name="frames">Render-tree frames for the component.</param>
	/// <param name="ancestorScopeId">Optional ancestor element id used to narrow ambiguous selectors.</param>
	/// <returns>A CSS selector, or <see langword="null"/> when no element frame is available.</returns>
	public static string? BuildLocator(ReadOnlySpan<RenderTreeFrame> frames, string? ancestorScopeId) {
		if (!TryGetFirstElement(frames, out string tag, out string? elementId, out IReadOnlyList<string> classes))
			return null;

		string selector = BuildElementSelector(tag, elementId, classes);

		if (!string.IsNullOrEmpty(ancestorScopeId))
			selector = $"#{EscapeCssIdentifier(ancestorScopeId)} {selector}";

		return selector;
	}

	/// <summary>
	/// Reads the id attribute from the component's first top-level element, if any.
	/// </summary>
	/// <param name="frames">Render-tree frames for the component.</param>
	/// <returns>The element id, or <see langword="null"/> when unavailable.</returns>
	public static string? TryGetElementId(ReadOnlySpan<RenderTreeFrame> frames) =>
		TryGetFirstElement(frames, out _, out string? elementId, out _)
			? elementId
			: null;

	/// <summary>
	/// Attempts to find the first top-level element in the given render-tree frames and extracts
	/// its tag name, id attribute, and list of CSS class names.
	/// </summary>
	/// <param name="frames">The render-tree frames to inspect.</param>
	/// <param name="tag">
	/// When this method returns, contains the element's tag name if an element is found; otherwise, an empty string.
	/// </param>
	/// <param name="elementId">
	/// When this method returns, contains the value of the <c>id</c> attribute if present; otherwise, <see langword="null"/>.
	/// </param>
	/// <param name="classes">
	/// When this method returns, contains a read-only list of CSS class names derived from the element's <c>class</c> attribute.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if a top-level element is found and extracted; otherwise, <see langword="false"/>.
	/// </returns>
	private static bool TryGetFirstElement(
		ReadOnlySpan<RenderTreeFrame> frames,
		out string tag,
		out string? elementId,
		out IReadOnlyList<string> classes) {
		tag = string.Empty;
		elementId = null;
		classes = [];

		if (!TryFindFirstElementFrame(frames, out tag, out int elementIndex))
			return false;

		ReadOnlySpan<RenderTreeFrame> attributeFrames = GetLeadingAttributeFrames(frames, elementIndex + 1);
		elementId = GetAttributeValue(attributeFrames, "id");
		classes = ParseClassNames(GetAttributeValue(attributeFrames, "class"));
		return true;
	}

	/// <summary>
	/// Attempts to locate the first top-level element frame within the provided render-tree frames.
	/// </summary>
	/// <param name="frames">A span of <see cref="RenderTreeFrame"/> objects representing the render tree to inspect.</param>
	/// <param name="tag">
	/// When this method returns, contains the tag name of the first element frame found,
	/// or an empty string if no element frame is found.
	/// </param>
	/// <param name="elementIndex">
	/// When this method returns, contains the index of the first element frame found in <paramref name="frames"/>,
	/// or -1 if no element frame is found.
	/// </param>
	/// <returns>
	/// <see langword="true"/> if a top-level element frame is found; otherwise, <see langword="false"/>.
	/// </returns>
	private static bool TryFindFirstElementFrame(
		ReadOnlySpan<RenderTreeFrame> frames,
		out string tag,
		out int elementIndex) {
		tag = string.Empty;
		elementIndex = -1;

		for (int i = 0; i < frames.Length; i++) {
			ref readonly RenderTreeFrame frame = ref frames[i];
			if (frame.FrameType != RenderTreeFrameType.Element)
				continue;

			tag = frame.ElementName;
			if (string.IsNullOrEmpty(tag))
				return false;

			elementIndex = i;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Retrieves a span of consecutive <see cref="RenderTreeFrame"/> entries starting from
	/// <paramref name="startIndex"/>, containing all leading attribute frames.
	/// </summary>
	/// <param name="frames">The span of frames representing a render tree segment.</param>
	/// <param name="startIndex">The index from which to begin inspecting for attribute frames.</param>
	/// <returns>
	/// A <see cref="ReadOnlySpan{RenderTreeFrame}"/> containing all contiguous frames of type
	/// <see cref="RenderTreeFrameType.Attribute"/> starting at <paramref name="startIndex"/>.
	/// </returns>
	private static ReadOnlySpan<RenderTreeFrame> GetLeadingAttributeFrames(
		ReadOnlySpan<RenderTreeFrame> frames,
		int startIndex) {
		int end = startIndex;
		while (end < frames.Length && frames[end].FrameType == RenderTreeFrameType.Attribute) {
			end++;
		}

		return frames.Slice(startIndex, end - startIndex);
	}

	/// <summary>
	/// Retrieves the string value of an attribute from a span of attribute <see cref="RenderTreeFrame"/>s
	/// by matching the specified attribute name.
	/// </summary>
	/// <param name="attributeFrames">A span of render tree frames representing attributes.</param>
	/// <param name="name">The attribute name to match.</param>
	/// <returns>
	/// The string value of the matched attribute, or <c>null</c> if the attribute is not found or its value is empty.
	/// </returns>
	private static string? GetAttributeValue(ReadOnlySpan<RenderTreeFrame> attributeFrames, string name) =>
		attributeFrames
			.ToArray()
			.FirstOrDefault(frame => string.Equals(frame.AttributeName, name, StringComparison.Ordinal))
			.AttributeValue is { } rawValue
			? CoerceAttributeString(rawValue)
			: null;

	/// <summary>
	/// Converts an attribute value to its string representation for CSS selector construction.
	/// </summary>
	/// <param name="value">The attribute value to coerce to a string.</param>
	/// <returns>
	/// The string representation of the attribute value, or <c>null</c> if the value is <c>null</c> or the string is empty.
	/// </returns>
	private static string? CoerceAttributeString(object value) {
		string? text = value as string ?? value.ToString();
		return string.IsNullOrEmpty(text) ? null : text;
	}

	/// <summary>
	/// Parses a space-delimited string of CSS class names into a list of individual class names.
	/// </summary>
	/// <param name="classValue">The string containing one or more space-separated CSS class names.</param>
	/// <returns>
	/// A <see cref="List{String}"/> containing each class name as a separate item,
	/// or an empty list if <paramref name="classValue"/> is <c>null</c> or empty.
	/// </returns>
	private static List<string> ParseClassNames(string? classValue) =>
		string.IsNullOrEmpty(classValue)
			? []
			: classValue
				.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.ToList();

	/// <summary>
	/// Constructs a CSS selector string for an HTML element using its tag name, element ID, and CSS classes.
	/// </summary>
	/// <param name="tag">The HTML tag name of the element (e.g., <c>div</c>, <c>span</c>).</param>
	/// <param name="elementId">The value of the element's <c>id</c> attribute, or <c>null</c> if not present.</param>
	/// <param name="classes">A read-only list of CSS class names for the element, may be empty.</param>
	/// <returns>
	/// A string representing the CSS selector for the element:
	/// <list type="bullet">
	///   <item>
	///     <description>
	///       <c>#{id}</c> if <paramref name="elementId"/> is provided (with CSS escaping applied).
	///     </description>
	///   </item>
	///   <item>
	///     <description>
	///       <c>tag.class1.class2...</c> if any classes are present.
	///     </description>
	///   </item>
	///   <item>
	///     <description>
	///       The plain <paramref name="tag"/> if neither <paramref name="elementId"/> nor <paramref name="classes"/> are provided.
	///     </description>
	///   </item>
	/// </list>
	/// </returns>
	private static string BuildElementSelector(
		string tag,
		string? elementId,
		IReadOnlyList<string> classes) {
		if (!string.IsNullOrEmpty(elementId)) {
			return $"#{EscapeCssIdentifier(elementId)}";
		}

		if (classes.Count <= 0)
			return tag;

		var builder = new StringBuilder(tag);
		foreach (string className in classes) {
			builder.Append('.');
			builder.Append(EscapeCssIdentifier(className));
		}

		return builder.ToString();
	}

	/// <summary>
	/// Escapes a string to ensure it is a valid CSS identifier segment,
	/// prefixing any non-alphanumeric and non-<c>-</c>/<c>_</c> characters with a backslash (<c>\</c>).
	/// Suitable for use in CSS selectors for IDs or class names.
	/// </summary>
	/// <param name="value">The raw CSS identifier string to escape.</param>
	/// <returns>
	/// An escaped string that is safe to use in a CSS selector.
	/// Returns the original string if <paramref name="value"/> is <c>null</c> or empty.
	/// </returns>
	private static string EscapeCssIdentifier(string value) {
		if (string.IsNullOrEmpty(value)) {
			return value;
		}

		var builder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			if (!char.IsLetterOrDigit(c) && c is not ('-' or '_'))
				builder.Append('\\');

			builder.Append(c);
		}

		return builder.ToString();
	}
}
