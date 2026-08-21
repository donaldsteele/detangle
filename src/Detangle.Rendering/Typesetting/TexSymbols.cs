namespace Detangle.Rendering.Typesetting;

/// <summary>
/// The TeX commands this renderer knows, mapped to the characters that draw them.
/// <para>
/// Deliberately a list of what wikis actually contain rather than an attempt at TeX. A
/// language model writing about transformers reaches for greek letters, a handful of
/// relations and the occasional integral; anything outside that is shown as its source,
/// which is more honest than a wrong glyph.
/// </para>
/// </summary>
internal static class TexSymbols
{
    /// <summary>Commands that map to one character, with how it should be set.</summary>
    public static readonly Dictionary<string, (string Text, MathStyle Style)> Map = new(StringComparer.Ordinal)
    {
        // Greek, lower case. Set italic, the way TeX sets them.
        ["alpha"] = ("α", MathStyle.Variable),
        ["beta"] = ("β", MathStyle.Variable),
        ["gamma"] = ("γ", MathStyle.Variable),
        ["delta"] = ("δ", MathStyle.Variable),
        ["epsilon"] = ("ε", MathStyle.Variable),
        ["varepsilon"] = ("ε", MathStyle.Variable),
        ["zeta"] = ("ζ", MathStyle.Variable),
        ["eta"] = ("η", MathStyle.Variable),
        ["theta"] = ("θ", MathStyle.Variable),
        ["iota"] = ("ι", MathStyle.Variable),
        ["kappa"] = ("κ", MathStyle.Variable),
        ["lambda"] = ("λ", MathStyle.Variable),
        ["mu"] = ("μ", MathStyle.Variable),
        ["nu"] = ("ν", MathStyle.Variable),
        ["xi"] = ("ξ", MathStyle.Variable),
        ["pi"] = ("π", MathStyle.Variable),
        ["rho"] = ("ρ", MathStyle.Variable),
        ["sigma"] = ("σ", MathStyle.Variable),
        ["tau"] = ("τ", MathStyle.Variable),
        ["upsilon"] = ("υ", MathStyle.Variable),
        ["phi"] = ("φ", MathStyle.Variable),
        ["varphi"] = ("φ", MathStyle.Variable),
        ["chi"] = ("χ", MathStyle.Variable),
        ["psi"] = ("ψ", MathStyle.Variable),
        ["omega"] = ("ω", MathStyle.Variable),

        // Greek, upper case. Upright, again following TeX.
        ["Gamma"] = ("Γ", MathStyle.Upright),
        ["Delta"] = ("Δ", MathStyle.Upright),
        ["Theta"] = ("Θ", MathStyle.Upright),
        ["Lambda"] = ("Λ", MathStyle.Upright),
        ["Xi"] = ("Ξ", MathStyle.Upright),
        ["Pi"] = ("Π", MathStyle.Upright),
        ["Sigma"] = ("Σ", MathStyle.Upright),
        ["Upsilon"] = ("Υ", MathStyle.Upright),
        ["Phi"] = ("Φ", MathStyle.Upright),
        ["Psi"] = ("Ψ", MathStyle.Upright),
        ["Omega"] = ("Ω", MathStyle.Upright),

        // Relations and operators.
        ["times"] = ("×", MathStyle.Upright),
        ["div"] = ("÷", MathStyle.Upright),
        ["cdot"] = ("·", MathStyle.Upright),
        ["pm"] = ("±", MathStyle.Upright),
        ["mp"] = ("∓", MathStyle.Upright),
        ["leq"] = ("≤", MathStyle.Upright),
        ["le"] = ("≤", MathStyle.Upright),
        ["geq"] = ("≥", MathStyle.Upright),
        ["ge"] = ("≥", MathStyle.Upright),
        ["neq"] = ("≠", MathStyle.Upright),
        ["ne"] = ("≠", MathStyle.Upright),
        ["approx"] = ("≈", MathStyle.Upright),
        ["equiv"] = ("≡", MathStyle.Upright),
        ["sim"] = ("∼", MathStyle.Upright),
        ["propto"] = ("∝", MathStyle.Upright),
        ["ll"] = ("≪", MathStyle.Upright),
        ["gg"] = ("≫", MathStyle.Upright),

        // Arrows.
        ["to"] = ("→", MathStyle.Upright),
        ["rightarrow"] = ("→", MathStyle.Upright),
        ["leftarrow"] = ("←", MathStyle.Upright),
        ["leftrightarrow"] = ("↔", MathStyle.Upright),
        ["Rightarrow"] = ("⇒", MathStyle.Upright),
        ["Leftarrow"] = ("⇐", MathStyle.Upright),
        ["mapsto"] = ("↦", MathStyle.Upright),

        // Sets and logic.
        ["in"] = ("∈", MathStyle.Upright),
        ["notin"] = ("∉", MathStyle.Upright),
        ["subset"] = ("⊂", MathStyle.Upright),
        ["subseteq"] = ("⊆", MathStyle.Upright),
        ["supset"] = ("⊃", MathStyle.Upright),
        ["cup"] = ("∪", MathStyle.Upright),
        ["cap"] = ("∩", MathStyle.Upright),
        ["emptyset"] = ("∅", MathStyle.Upright),
        ["forall"] = ("∀", MathStyle.Upright),
        ["exists"] = ("∃", MathStyle.Upright),
        ["neg"] = ("¬", MathStyle.Upright),
        ["land"] = ("∧", MathStyle.Upright),
        ["lor"] = ("∨", MathStyle.Upright),

        // Miscellany that turns up constantly in machine-learning notes.
        ["infty"] = ("∞", MathStyle.Upright),
        ["partial"] = ("∂", MathStyle.Variable),
        ["nabla"] = ("∇", MathStyle.Upright),
        // Transpose, written as a capital T rather than U+22A4. In machine-learning notes
        // 	op always means transpose, and the real character is missing from most text
        // faces - including the one this application ships - where it draws as a box.
        ["top"] = ("T", MathStyle.Upright),
        ["bot"] = ("⊥", MathStyle.Upright),
        ["perp"] = ("⊥", MathStyle.Upright),
        ["angle"] = ("∠", MathStyle.Upright),
        ["circ"] = ("∘", MathStyle.Upright),
        ["star"] = ("⋆", MathStyle.Upright),
        ["ast"] = ("∗", MathStyle.Upright),
        ["oplus"] = ("⊕", MathStyle.Upright),
        ["otimes"] = ("⊗", MathStyle.Upright),
        ["odot"] = ("⊙", MathStyle.Upright),
        ["ldots"] = ("…", MathStyle.Upright),
        ["dots"] = ("…", MathStyle.Upright),
        ["cdots"] = ("⋯", MathStyle.Upright),
        ["vdots"] = ("⋮", MathStyle.Upright),
        ["prime"] = ("′", MathStyle.Upright),
        ["degree"] = ("°", MathStyle.Upright),
        ["hbar"] = ("ℏ", MathStyle.Variable),
        ["ell"] = ("ℓ", MathStyle.Variable),
        ["Re"] = ("ℜ", MathStyle.Upright),
        ["Im"] = ("ℑ", MathStyle.Upright),
        ["aleph"] = ("ℵ", MathStyle.Upright),

        // Large operators. Drawn a size up, the way TeX does in display style.
        ["sum"] = ("∑", MathStyle.Large),
        ["prod"] = ("∏", MathStyle.Large),
        ["coprod"] = ("∐", MathStyle.Large),
        ["int"] = ("∫", MathStyle.Large),
        ["iint"] = ("∬", MathStyle.Large),
        ["oint"] = ("∮", MathStyle.Large),
        ["bigcup"] = ("⋃", MathStyle.Large),
        ["bigcap"] = ("⋂", MathStyle.Large),
        ["bigoplus"] = ("⨁", MathStyle.Large),
        ["lim"] = ("lim", MathStyle.Text),
        ["max"] = ("max", MathStyle.Text),
        ["min"] = ("min", MathStyle.Text),
        ["arg"] = ("arg", MathStyle.Text),
        ["exp"] = ("exp", MathStyle.Text),
        ["log"] = ("log", MathStyle.Text),
        ["ln"] = ("ln", MathStyle.Text),
        ["sin"] = ("sin", MathStyle.Text),
        ["cos"] = ("cos", MathStyle.Text),
        ["tan"] = ("tan", MathStyle.Text),
        ["det"] = ("det", MathStyle.Text),
        ["dim"] = ("dim", MathStyle.Text),
        ["softmax"] = ("softmax", MathStyle.Text),
    };

    /// <summary>Delimiters <c>\left</c> and <c>\right</c> accept.</summary>
    public static readonly Dictionary<string, string> Delimiters = new(StringComparer.Ordinal)
    {
        ["("] = "(",
        [")"] = ")",
        ["["] = "[",
        ["]"] = "]",
        ["\\{"] = "{",
        ["\\}"] = "}",
        ["|"] = "|",
        ["\\|"] = "‖",
        ["\\langle"] = "⟨",
        ["\\rangle"] = "⟩",
        ["\\lceil"] = "⌈",
        ["\\rceil"] = "⌉",
        ["\\lfloor"] = "⌊",
        ["\\rfloor"] = "⌋",
        ["."] = string.Empty,
    };

    /// <summary>Spacing commands, in thin-space multiples.</summary>
    public static readonly Dictionary<string, double> Spaces = new(StringComparer.Ordinal)
    {
        [","] = 1,
        [":"] = 1.4,
        [";"] = 1.8,
        ["!"] = -1,
        ["quad"] = 4,
        ["qquad"] = 8,
        [" "] = 2,
    };
}
