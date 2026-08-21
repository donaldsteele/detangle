using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Detangle.Core.Graph;

namespace Detangle.Rendering.Controls;

/// <summary>Raised when the reader activates a node.</summary>
/// <param name="Node">The node clicked.</param>
public sealed record GraphNodeEventArgs(GraphNode Node);

/// <summary>
/// The graph view (plan.md section 6.4): a force-directed picture of the vault's links,
/// drawn directly rather than as a control per node.
/// <para>
/// Five thousand nodes as five thousand controls is five thousand measure and arrange
/// passes per frame, which no layout system survives. Everything here is one custom
/// draw: edges go into a single geometry, nodes are ellipses, and labels are drawn as
/// glyphs rather than as text blocks — no layout is involved, which also keeps this
/// control clear of the text-measure trap the document renderer had to work around.
/// </para>
/// </summary>
public sealed class GraphCanvas : Control
{
    private const double MinimumScale = 0.05;
    private const double MaximumScale = 6;

    private readonly DispatcherTimer _timer;
    private readonly DocumentTheme _theme;

    private GraphModel _model = GraphModel.Empty;
    private ForceLayout? _layout;
    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;
    private int _hovered = -1;
    private int _dragging = -1;
    private Point _lastPointer;
    private bool _panning;
    private bool _moved;
    private bool _needsFit;

    /// <summary>Creates a graph canvas.</summary>
    /// <param name="theme">Colours; defaults to the light palette.</param>
    public GraphCanvas(DocumentTheme? theme = null)
    {
        _theme = theme ?? DocumentTheme.Light;
        ClipToBounds = true;
        Focusable = true;

        // Sixteen milliseconds is the frame the plan's thirty-per-second floor is measured
        // against; the timer stops itself as soon as the layout has cooled, so a settled
        // graph costs nothing.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, OnTick);
    }

    /// <summary>Raised when a node is clicked.</summary>
    public event EventHandler<GraphNodeEventArgs>? NodeActivated;

    /// <summary>Raised when the node under the pointer changes; the node is null on exit.</summary>
    public event EventHandler<GraphNode?>? NodeHovered;

    /// <summary>The graph being shown.</summary>
    public GraphModel Model => _model;

    /// <summary>The node under the pointer, if any.</summary>
    public GraphNode? Hovered => _hovered >= 0 && _hovered < _model.Nodes.Count ? _model.Nodes[_hovered] : null;

    /// <summary>True while the simulation is still moving.</summary>
    public bool IsSimulating => _layout is { IsSettled: false };

    /// <summary>Shows a graph, restarting the simulation.</summary>
    public void Show(GraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;
        _layout = new ForceLayout(model);
        _hovered = -1;
        _dragging = -1;
        _needsFit = true;

        // A cold layout is a spiral, which is not a picture of anything. Running the first
        // few hundred steps up front costs a fraction of a second and means the reader
        // arrives at a graph rather than at a firework.
        _layout.Step(Math.Max(0, 400 - (model.Nodes.Count / 20)));

        Start();
        InvalidateVisual();
    }

    /// <summary>Advances the simulation one step and redraws. The timer calls this.</summary>
    public void Advance()
    {
        if (_layout is null)
        {
            return;
        }

        if (!_layout.IsSettled)
        {
            _layout.Step();
        }

        if (_needsFit)
        {
            FitToView();
        }

        InvalidateVisual();
    }

    /// <summary>Scales and centres the view so the whole graph is visible.</summary>
    public void FitToView()
    {
        if (_layout is null || _model.Nodes.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        (double minX, double minY, double maxX, double maxY) = _layout.Bounds();

        double width = Math.Max(maxX - minX, 1);
        double height = Math.Max(maxY - minY, 1);

        _scale = Math.Clamp(
            Math.Min((Bounds.Width - 80) / width, (Bounds.Height - 80) / height),
            MinimumScale,
            MaximumScale);

        _offsetX = (Bounds.Width / 2) - ((minX + maxX) / 2 * _scale);
        _offsetY = (Bounds.Height / 2) - ((minY + maxY) / 2 * _scale);
        _needsFit = false;
    }

    /// <summary>Centres the view on a node without changing the zoom.</summary>
    public void CentreOn(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_layout is null || node.Index >= _model.Nodes.Count)
        {
            return;
        }

        _offsetX = (Bounds.Width / 2) - (_layout.X[node.Index] * _scale);
        _offsetY = (Bounds.Height / 2) - (_layout.Y[node.Index] * _scale);
        _needsFit = false;

        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(_theme.Background, new Rect(Bounds.Size));

        if (_layout is null || _model.Nodes.Count == 0)
        {
            return;
        }

        DrawEdges(context);
        DrawNodes(context);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Press(e.GetPosition(this));
        e.Pointer.Capture(this);
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        MoveTo(e.GetPosition(this));
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        e.Pointer.Capture(null);
        Release();
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hovered >= 0)
        {
            _hovered = -1;
            NodeHovered?.Invoke(this, null);
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        Zoom(e.GetPosition(this), e.Delta.Y);
        e.Handled = true;
    }

    /// <summary>
    /// Begins an interaction at a point: on a node it starts a drag, on empty space it
    /// starts a pan.
    /// <para>
    /// Interaction is a public surface rather than only a set of pointer overrides so
    /// that it can be driven from a test, a keyboard binding or the command palette, all
    /// of which reach the same code the mouse does.
    /// </para>
    /// </summary>
    public void Press(Point position)
    {
        _lastPointer = position;
        _moved = false;
        _dragging = HitTest(position);
        _panning = _dragging < 0;
    }

    /// <summary>Continues an interaction, or updates the hover when none is in progress.</summary>
    public void MoveTo(Point position)
    {
        Vector delta = position - _lastPointer;

        if (Math.Abs(delta.X) > 2 || Math.Abs(delta.Y) > 2)
        {
            _moved = true;
        }

        if (_dragging >= 0 && _layout is not null)
        {
            _layout.Place(
                _dragging,
                (position.X - _offsetX) / _scale,
                (position.Y - _offsetY) / _scale);

            _layout.Reheat(0.2);
            Start();
        }
        else if (_panning)
        {
            _offsetX += delta.X;
            _offsetY += delta.Y;
            _needsFit = false;
        }
        else
        {
            UpdateHover(position);
        }

        _lastPointer = position;

        InvalidateVisual();
    }

    /// <summary>
    /// Ends an interaction, opening a node when the press and release were a click rather
    /// than a drag.
    /// </summary>
    public void Release()
    {
        int released = _dragging;

        _dragging = -1;
        _panning = false;

        if (!_moved && released >= 0 && released < _model.Nodes.Count)
        {
            NodeActivated?.Invoke(this, new GraphNodeEventArgs(_model.Nodes[released]));
        }
    }

    /// <summary>Zooms about a point, keeping whatever is under it in place.</summary>
    /// <param name="position">The point to zoom about, in control coordinates.</param>
    /// <param name="steps">Wheel notches; positive zooms in.</param>
    public void Zoom(Point position, double steps)
    {
        double scale = Math.Clamp(_scale * Math.Pow(1.15, steps), MinimumScale, MaximumScale);

        _offsetX = position.X - ((position.X - _offsetX) * (scale / _scale));
        _offsetY = position.Y - ((position.Y - _offsetY) * (scale / _scale));
        _scale = scale;
        _needsFit = false;

        InvalidateVisual();
    }

    /// <summary>Where a node currently sits, in control coordinates.</summary>
    public Point PositionOf(int index) =>
        _layout is null || index < 0 || index >= _model.Nodes.Count ? default : ScreenOf(index);

    /// <summary>The node at a point in control coordinates, or -1.</summary>
    public int HitTest(Point position)
    {
        if (_layout is null)
        {
            return -1;
        }

        int best = -1;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < _model.Nodes.Count; i++)
        {
            double x = (_layout.X[i] * _scale) + _offsetX;
            double y = (_layout.Y[i] * _scale) + _offsetY;
            double dx = x - position.X;
            double dy = y - position.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            double radius = RadiusOf(_model.Nodes[i]) + 2;

            if (distance <= radius && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void Start()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_layout is null)
        {
            return;
        }

        if (_layout.IsSettled && !_needsFit)
        {
            _timer.Stop();
            return;
        }

        Advance();
    }

    private void UpdateHover(Point position)
    {
        int hit = HitTest(position);

        if (hit == _hovered)
        {
            return;
        }

        _hovered = hit;
        NodeHovered?.Invoke(this, Hovered);
    }

    private void DrawEdges(DrawingContext context)
    {
        // Every ordinary edge goes into one geometry: five thousand separate DrawLine
        // calls is five thousand draw commands, and the broken ones are drawn apart only
        // because they are a different colour.
        var solid = new StreamGeometry();
        var broken = new StreamGeometry();

        using (StreamGeometryContext solidContext = solid.Open())
        using (StreamGeometryContext brokenContext = broken.Open())
        {
            foreach (GraphEdge edge in _model.Edges)
            {
                Point from = ScreenOf(edge.Source);
                Point to = ScreenOf(edge.Target);

                if (!IsOnScreen(from) && !IsOnScreen(to))
                {
                    continue;
                }

                StreamGeometryContext target = edge.IsBroken ? brokenContext : solidContext;

                target.BeginFigure(from, isFilled: false);
                target.LineTo(to);
                target.EndFigure(isClosed: false);
            }
        }

        context.DrawGeometry(null, new Pen(_theme.Border, 1), solid);
        context.DrawGeometry(null, new Pen(_theme.UnresolvedLink, 1, DashStyle.Dash), broken);

        if (_hovered < 0)
        {
            return;
        }

        // The hovered page's own links are redrawn on top, which is the only way to read
        // one page's neighbourhood out of a dense picture.
        var pen = new Pen(_theme.Link, 2);

        foreach (GraphEdge edge in _model.Edges)
        {
            if (edge.Source == _hovered || edge.Target == _hovered)
            {
                context.DrawLine(pen, ScreenOf(edge.Source), ScreenOf(edge.Target));
            }
        }
    }

    private void DrawNodes(DrawingContext context)
    {
        bool labels = _scale > 0.45;

        for (int i = 0; i < _model.Nodes.Count; i++)
        {
            Point centre = ScreenOf(i);

            if (!IsOnScreen(centre))
            {
                continue;
            }

            GraphNode node = _model.Nodes[i];
            double radius = RadiusOf(node) * Math.Clamp(_scale, 0.6, 1.6);
            IBrush fill = FillOf(node);

            if (node.Kind == GraphNodeKind.MissingTarget)
            {
                // A page that does not exist is drawn as an outline, so the reader can see
                // at a glance which nodes are work rather than content.
                context.DrawEllipse(null, new Pen(_theme.UnresolvedLink, 1.5, DashStyle.Dash), centre, radius, radius);
            }
            else if (node.IsOrphan)
            {
                context.DrawEllipse(_theme.Background, new Pen(fill, 1.5), centre, radius, radius);
            }
            else
            {
                context.DrawEllipse(fill, null, centre, radius, radius);
            }

            if (i == _hovered)
            {
                context.DrawEllipse(null, new Pen(_theme.Foreground, 2), centre, radius + 3, radius + 3);
            }

            if (labels && (i == _hovered || radius > 6 || node.Kind == GraphNodeKind.Cluster))
            {
                DrawLabel(context, node, centre, radius);
            }
        }
    }

    private void DrawLabel(DrawingContext context, GraphNode node, Point centre, double radius)
    {
        var text = new FormattedText(
            node.Label,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(_theme.FontFamily),
            node.Kind == GraphNodeKind.Cluster ? 13 : 11,
            _theme.Foreground);

        context.DrawText(text, new Point(centre.X - (text.Width / 2), centre.Y + radius + 2));
    }

    private Point ScreenOf(int index) =>
        new((_layout!.X[index] * _scale) + _offsetX, (_layout.Y[index] * _scale) + _offsetY);

    private bool IsOnScreen(Point point) =>
        point.X > -60 && point.Y > -60 && point.X < Bounds.Width + 60 && point.Y < Bounds.Height + 60;

    private static double RadiusOf(GraphNode node) => node.Kind switch
    {
        GraphNodeKind.Cluster => Math.Clamp(6 + (Math.Sqrt(node.Weight) * 1.5), 6, 40),
        GraphNodeKind.MissingTarget => Math.Clamp(3 + Math.Sqrt(node.InboundCount), 3, 12),
        _ => Math.Clamp(3.5 + (Math.Sqrt(node.InboundCount) * 2), 3.5, 24),
    };

    private IBrush FillOf(GraphNode node)
    {
        if (node.Kind == GraphNodeKind.Cluster)
        {
            return _theme.Muted;
        }

        if (node.Type is not { Length: > 0 } type)
        {
            return _theme.Link;
        }

        return Palette[StableIndex(type)];
    }

    /// <summary>
    /// Colour by frontmatter type, chosen by a stable hash of the type name rather than
    /// by order of appearance: a vault's "concept" pages keep the same colour between
    /// sessions and between filters, which is what makes the colour mean anything.
    /// </summary>
    private static int StableIndex(string type)
    {
        uint hash = 2166136261;

        foreach (char c in type)
        {
            hash = (hash ^ char.ToLowerInvariant(c)) * 16777619;
        }

        return (int)(hash % (uint)Palette.Length);
    }

    private static readonly IBrush[] Palette =
    [
        new SolidColorBrush(Color.FromRgb(0x4C, 0x8B, 0xF5)),
        new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)),
        new SolidColorBrush(Color.FromRgb(0xE0, 0x7B, 0x39)),
        new SolidColorBrush(Color.FromRgb(0xA6, 0x6B, 0xE8)),
        new SolidColorBrush(Color.FromRgb(0xD9, 0x4F, 0x70)),
        new SolidColorBrush(Color.FromRgb(0x21, 0xA8, 0xA8)),
        new SolidColorBrush(Color.FromRgb(0xC2, 0xA5, 0x2B)),
        new SolidColorBrush(Color.FromRgb(0x6C, 0x7A, 0x89)),
    ];
}
