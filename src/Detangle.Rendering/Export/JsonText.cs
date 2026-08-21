using System.Globalization;
using System.Text;

namespace Detangle.Rendering.Export;

/// <summary>
/// The little bit of JSON the export writes.
/// <para>
/// Written by hand rather than through a serializer: the shape is three strings per
/// page, and the export has to keep working under trimming, where reflection-based
/// serialization is exactly the thing that stops working — silently, at run time, in
/// somebody else's build.
/// </para>
/// </summary>
internal static class JsonText
{
    /// <summary>Writes one JSON string literal, escaped.</summary>
    public static string Quote(string? value)
    {
        var builder = new StringBuilder((value?.Length ?? 0) + 2).Append('"');

        foreach (char c in value ?? string.Empty)
        {
            _ = c switch
            {
                '"' => builder.Append("\\\""),
                '\\' => builder.Append("\\\\"),
                '\n' => builder.Append("\\n"),
                '\r' => builder.Append("\\r"),
                '\t' => builder.Append("\\t"),

                // U+2028 and U+2029 are valid JSON and invalid JavaScript source, which is
                // how a paragraph separator pasted into a note becomes a syntax error in
                // somebody's browser three weeks later.
                < ' ' or (char)0x2028 or (char)0x2029 =>
                    builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"),

                _ => builder.Append(c),
            };
        }

        return builder.Append('"').ToString();
    }
}
