using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Detangle.Rendering.Typesetting;

// Path is System.IO.Path under this project's implicit usings; the radical sign needs
// the shape.
using Shape = Avalonia.Controls.Shapes.Path;

namespace Detangle.Rendering.Controls;

/// <summary>
/// Sets parsed math as Avalonia controls.
/// <para>
/// Not a typesetting engine — a careful approximation of one. Fractions stack over a
/// rule, radicals get a drawn sign with an overbar, scripts are offset by a fraction of
/// the current size, and delimiters are scaled to the height of what they hold. That is
/// enough for the notation a wiki contains, and it is set in the reading face rather than
/// dumped as source.
/// </para>
/// </summary>
internal sealed class MathRenderer(DocumentTheme theme)
{
    /// <summary>Builds a control for a fragment of TeX.</summary>
    /// <param name="source">The TeX, without delimiters.</param>
    /// <param name="fontSize">The surrounding text size.</param>
    /// <param name="isBlock">True for display math, which is set a size larger.</param>
    public Control Render(string source, double fontSize, bool isBlock)
    {
        MathNode node;

        try
        {
            node = TexParser.Parse(source);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            return Source(source, fontSize);
        }

        Control content = Build(node, isBlock ? fontSize * 1.3 : fontSize);

        content.VerticalAlignment = VerticalAlignment.Center;

        return content;
    }

    private Control Build(MathNode node, double size) => node switch
    {
        MathRow row => Row(row, size),
        MathAtom atom => Atom(atom, size),
        MathFraction fraction => Fraction(fraction, size),
        MathRadical radical => Radical(radical, size),
        MathScripts scripts => Scripts(scripts, size),
        MathFenced fenced => Fenced(fenced, size),
        MathSpace space => new Border { Width = Math.Max(0, space.Width * size * 0.17) },
        MathUnknown unknown => Source(unknown.Source, size),
        _ => new Control(),
    };

    private Control Row(MathRow row, double size)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        for (int i = 0; i < row.Children.Count; i++)
        {
            Control child = Build(row.Children[i], size);

            // A relation breathes; a variable next to a variable does not. This is the
            // one spacing rule TeX readers notice the absence of.
            if (i > 0 && NeedsSpace(row.Children[i - 1], row.Children[i]))
            {
                child.Margin = new Thickness(size * 0.22, 0, 0, 0);
            }

            panel.Children.Add(child);
        }

        return panel;
    }

    private static bool NeedsSpace(MathNode left, MathNode right) =>
        IsRelation(left) || IsRelation(right);

    private static bool IsRelation(MathNode node) =>
        node is MathAtom { Style: MathStyle.Upright, Text: "=" or "≤" or "≥" or "≠" or "≈" or "≡" or "<" or ">" or "+" or "−" or "-" or "→" or "←" or "↔" or "⇒" or "∈" or "∼" or "∝" };

    private Control Atom(MathAtom atom, double size)
    {
        bool isLarge = atom.Style == MathStyle.Large;

        return new TextBlock
        {
            Text = atom.Text,
            FontSize = isLarge ? size * 1.45 : size,
            FontFamily = atom.Style == MathStyle.Text ? theme.FontFamily : theme.MathFontFamily,
            FontStyle = atom.Style == MathStyle.Variable ? FontStyle.Italic : FontStyle.Normal,
            Foreground = theme.Foreground,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
    }

    private Control Fraction(MathFraction fraction, double size)
    {
        // TeX does not shrink a fraction in display style, and shrinking one here made
        // every nested radical and script compound down to something unreadable.
        double inner = size;

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(size * 0.12, 0),
        };

        Control numerator = Build(fraction.Numerator, inner);
        Control denominator = Build(fraction.Denominator, inner);

        numerator.HorizontalAlignment = HorizontalAlignment.Center;
        denominator.HorizontalAlignment = HorizontalAlignment.Center;

        panel.Children.Add(numerator);

        panel.Children.Add(new Border
        {
            Height = Math.Max(1, size * 0.06),
            Background = theme.Foreground,
            Margin = new Thickness(0, size * 0.12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });

        panel.Children.Add(denominator);

        return panel;
    }

    private Control Radical(MathRadical radical, double size)
    {
        Control radicand = Build(radical.Radicand, size);

        // The sign is drawn rather than typed: the glyph in a text font is a fixed height
        // and will not cover a fraction, which is exactly where radicals turn up.
        var sign = new Shape
        {
            Data = StreamGeometry.Parse("M0,0.55 L0.28,0.55 L0.52,1 L0.85,0 L1.6,0"),
            Stroke = theme.Foreground,
            StrokeThickness = Math.Max(1, size * 0.055),
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Fill,
            Width = size * 0.5,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, -size * 0.04, 0),
        };

        var body = new Border
        {
            BorderBrush = theme.Foreground,
            BorderThickness = new Thickness(0, Math.Max(1, size * 0.055), 0, 0),
            Padding = new Thickness(size * 0.1, size * 0.06, size * 0.06, 0),
            Child = radicand,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (radical.Index is { } index)
        {
            Control degree = Build(index, size * 0.62);
            degree.Margin = new Thickness(0, 0, -size * 0.22, size * 0.5);
            degree.VerticalAlignment = VerticalAlignment.Bottom;

            panel.Children.Add(degree);
        }

        panel.Children.Add(sign);
        panel.Children.Add(body);

        return panel;
    }

    private Control Scripts(MathScripts scripts, double size)
    {
        double scriptSize = Math.Max(7, size * 0.72);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Control nucleus = Build(scripts.Nucleus, size);
        nucleus.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(nucleus);

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(size * 0.04, 0, 0, 0),
        };

        // The scripts are lifted and dropped by margin rather than by baseline, which
        // Avalonia does not expose for arbitrary controls. The offsets are proportional
        // to the size so they hold when a fraction nests inside a script.
        if (scripts.Superscript is { } superscript)
        {
            Control raised = Build(superscript, scriptSize);
            raised.Margin = new Thickness(0, 0, 0, scripts.Subscript is null ? size * 0.42 : size * 0.1);
            raised.HorizontalAlignment = HorizontalAlignment.Left;

            stack.Children.Add(raised);
        }

        if (scripts.Subscript is { } subscript)
        {
            Control lowered = Build(subscript, scriptSize);
            lowered.Margin = new Thickness(0, scripts.Superscript is null ? size * 0.4 : size * 0.1, 0, 0);
            lowered.HorizontalAlignment = HorizontalAlignment.Left;

            stack.Children.Add(lowered);
        }

        panel.Children.Add(stack);

        return panel;
    }

    private Control Fenced(MathFenced fenced, double size)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (fenced.Open.Length > 0)
        {
            panel.Children.Add(Delimiter(fenced.Open, size));
        }

        Control body = Build(fenced.Body, size);
        body.Margin = new Thickness(size * 0.06, 0);
        panel.Children.Add(body);

        if (fenced.Close.Length > 0)
        {
            panel.Children.Add(Delimiter(fenced.Close, size));
        }

        return panel;
    }

    /// <summary>
    /// A delimiter that grows with its content. Avalonia has no glyph stretching, so the
    /// bracket is a text block scaled vertically — which is what the growing forms in a
    /// maths font are anyway.
    /// </summary>
    private Control Delimiter(string text, double size) => new Viewbox
    {
        Stretch = Stretch.Fill,
        Width = size * 0.36,
        VerticalAlignment = VerticalAlignment.Stretch,
        Child = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = theme.MathFontFamily,
            Foreground = theme.Foreground,
            TextWrapping = TextWrapping.NoWrap,
        },
    };

    /// <summary>
    /// Math this renderer could not set, shown as its own source. Marked so the reader
    /// can tell it apart from notation that was understood.
    /// </summary>
    private Control Source(string source, double size) => new Border
    {
        Background = theme.CodeBackground,
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(5, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = DocumentRenderer.Wrappable(source),
            FontFamily = theme.CodeFontFamily,
            FontSize = size * 0.9,
            Foreground = theme.Muted,
            TextWrapping = TextWrapping.NoWrap,
        },
    };
}
