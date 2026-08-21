namespace Detangle.Rendering.Typesetting;

/// <summary>How a piece of math should be set.</summary>
public enum MathStyle
{
    /// <summary>A variable: italic, the default for a bare letter.</summary>
    Variable,

    /// <summary>An operator, digit or punctuation mark: upright.</summary>
    Upright,

    /// <summary>Words inside <c>\text</c> or <c>\mathrm</c>: upright, at body weight.</summary>
    Text,

    /// <summary>A large operator such as a sum or an integral.</summary>
    Large,
}

/// <summary>One node of parsed math.</summary>
public abstract record MathNode;

/// <summary>A run of nodes set side by side.</summary>
/// <param name="Children">The nodes, in order.</param>
public sealed record MathRow(IReadOnlyList<MathNode> Children) : MathNode;

/// <summary>A symbol, letter or word.</summary>
/// <param name="Text">The characters to draw, already mapped out of TeX.</param>
/// <param name="Style">How to set them.</param>
public sealed record MathAtom(string Text, MathStyle Style) : MathNode;

/// <summary>A fraction.</summary>
/// <param name="Numerator">Above the rule.</param>
/// <param name="Denominator">Below it.</param>
public sealed record MathFraction(MathNode Numerator, MathNode Denominator) : MathNode;

/// <summary>A radical.</summary>
/// <param name="Radicand">What is under the sign.</param>
/// <param name="Index">The root index, for <c>\sqrt[3]{x}</c>.</param>
public sealed record MathRadical(MathNode Radicand, MathNode? Index = null) : MathNode;

/// <summary>A nucleus with a superscript, a subscript, or both.</summary>
/// <param name="Nucleus">What the scripts attach to.</param>
/// <param name="Superscript">Raised, when present.</param>
/// <param name="Subscript">Lowered, when present.</param>
public sealed record MathScripts(
    MathNode Nucleus,
    MathNode? Superscript = null,
    MathNode? Subscript = null) : MathNode;

/// <summary>Content between delimiters that grow to fit it.</summary>
/// <param name="Open">The opening delimiter; empty for <c>\left.</c>.</param>
/// <param name="Close">The closing delimiter; empty for <c>\right.</c>.</param>
/// <param name="Body">What sits between them.</param>
public sealed record MathFenced(string Open, string Close, MathNode Body) : MathNode;

/// <summary>Horizontal space, in multiples of a thin space.</summary>
/// <param name="Width">How many thin spaces wide.</param>
public sealed record MathSpace(double Width) : MathNode;

/// <summary>
/// Something the parser did not understand, kept verbatim.
/// <para>
/// Math that cannot be set is shown as its own source rather than dropped or guessed at,
/// which is the same promise the link resolver makes: the reader is told what could not
/// be worked out instead of being shown something plausible.
/// </para>
/// </summary>
/// <param name="Source">The TeX, exactly as written.</param>
public sealed record MathUnknown(string Source) : MathNode;
