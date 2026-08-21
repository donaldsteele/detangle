namespace Detangle.Core.Graph;

/// <summary>Tuning for the force simulation.</summary>
public sealed record ForceLayoutOptions
{
    /// <summary>The defaults, tuned on a five thousand page vault.</summary>
    public static ForceLayoutOptions Default { get; } = new();

    /// <summary>Strength of the node-node repulsion. Negative pushes apart.</summary>
    public double Repulsion { get; init; } = -900;

    /// <summary>How hard an edge pulls its ends together, per step.</summary>
    public double LinkStrength { get; init; } = 0.08;

    /// <summary>Rest length of an edge.</summary>
    public double LinkDistance { get; init; } = 45;

    /// <summary>Pull toward the origin, which stops disconnected components drifting away.</summary>
    public double Gravity { get; init; } = 0.015;

    /// <summary>Fraction of velocity kept between steps.</summary>
    public double VelocityDecay { get; init; } = 0.6;

    /// <summary>
    /// Barnes-Hut opening angle. A quadtree cell further away than its width divided by
    /// this is treated as one body; larger trades accuracy for speed.
    /// </summary>
    public double Theta { get; init; } = 0.9;

    /// <summary>How fast the simulation cools.</summary>
    public double AlphaDecay { get; init; } = 0.0228;

    /// <summary>The temperature below which the layout is considered settled.</summary>
    public double AlphaMin { get; init; } = 0.001;
}

/// <summary>
/// A force-directed layout over a <see cref="GraphModel"/>.
/// <para>
/// Repulsion goes through a Barnes-Hut quadtree rather than comparing every pair, which
/// is the difference between a five thousand node vault running at sixty frames a second
/// and at one: the all-pairs form is twenty five million distance computations per step.
/// </para>
/// <para>
/// Positions are seeded from a phyllotaxis spiral rather than at random, so the same
/// vault always lays out the same way. A graph that rearranges itself every time it is
/// opened is one the reader cannot build a memory of, and it also makes the layout
/// untestable.
/// </para>
/// </summary>
public sealed class ForceLayout
{
    private readonly ForceLayoutOptions _options;
    private readonly int[] _edgeSource;
    private readonly int[] _edgeTarget;
    private readonly double[] _edgeStrength;
    private readonly double[] _x;
    private readonly double[] _y;
    private readonly double[] _vx;
    private readonly double[] _vy;
    private readonly double[] _mass;

    private readonly QuadTree _tree = new();

    /// <summary>Creates a layout, seeded but not yet stepped.</summary>
    /// <param name="model">The graph to lay out.</param>
    /// <param name="options">Force tuning; defaults to <see cref="ForceLayoutOptions.Default"/>.</param>
    public ForceLayout(GraphModel model, ForceLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        Model = model;
        _options = options ?? ForceLayoutOptions.Default;

        int count = model.Nodes.Count;
        _x = new double[count];
        _y = new double[count];
        _vx = new double[count];
        _vy = new double[count];
        _mass = new double[count];

        int[] degree = new int[count];

        foreach (GraphEdge edge in model.Edges)
        {
            degree[edge.Source]++;
            degree[edge.Target]++;
        }

        _edgeSource = new int[model.Edges.Count];
        _edgeTarget = new int[model.Edges.Count];
        _edgeStrength = new double[model.Edges.Count];

        for (int i = 0; i < model.Edges.Count; i++)
        {
            GraphEdge edge = model.Edges[i];
            _edgeSource[i] = edge.Source;
            _edgeTarget[i] = edge.Target;

            // A hub's edges each pull less hard than a leaf's, so one heavily linked page
            // does not drag the whole vault into its own orbit.
            _edgeStrength[i] = _options.LinkStrength / Math.Max(1, Math.Min(degree[edge.Source], degree[edge.Target]));
        }

        for (int i = 0; i < count; i++)
        {
            // Node mass grows with how much it stands for, so a cluster of two hundred
            // pages pushes harder than a single note.
            _mass[i] = Math.Sqrt(model.Nodes[i].Weight);

            double radius = 12 * Math.Sqrt(i + 0.5);
            double angle = (i + 0.5) * GoldenAngle;

            _x[i] = radius * Math.Cos(angle);
            _y[i] = radius * Math.Sin(angle);
        }
    }

    private const int ParallelThreshold = 512;

    private const double GoldenAngle = Math.PI * (3 - 2.2360679774997896);

    /// <summary>The graph being laid out.</summary>
    public GraphModel Model { get; }

    /// <summary>The simulation temperature: 1 at the start, decaying toward zero.</summary>
    public double Alpha { get; private set; } = 1;

    /// <summary>True once the layout has cooled enough to stop stepping.</summary>
    public bool IsSettled => Alpha < _options.AlphaMin;

    /// <summary>X positions, indexed by node.</summary>
    public IReadOnlyList<double> X => _x;

    /// <summary>Y positions, indexed by node.</summary>
    public IReadOnlyList<double> Y => _y;

    /// <summary>Reheats a settled layout, after a filter change or a drag.</summary>
    public void Reheat(double alpha = 0.4) => Alpha = Math.Clamp(alpha, 0, 1);

    /// <summary>Pins a node to a position, as a drag does.</summary>
    /// <param name="index">The node to move.</param>
    /// <param name="x">New x position.</param>
    /// <param name="y">New y position.</param>
    public void Place(int index, double x, double y)
    {
        _x[index] = x;
        _y[index] = y;
        _vx[index] = 0;
        _vy[index] = 0;
    }

    /// <summary>Advances the simulation.</summary>
    /// <param name="iterations">How many steps to run.</param>
    public void Step(int iterations = 1)
    {
        for (int i = 0; i < iterations && !IsSettled; i++)
        {
            Alpha += (0 - Alpha) * _options.AlphaDecay;
            Tick();
        }
    }

    /// <summary>The bounding box of the current positions.</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
    {
        if (_x.Length == 0)
        {
            return (0, 0, 0, 0);
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        for (int i = 0; i < _x.Length; i++)
        {
            minX = Math.Min(minX, _x[i]);
            minY = Math.Min(minY, _y[i]);
            maxX = Math.Max(maxX, _x[i]);
            maxY = Math.Max(maxY, _y[i]);
        }

        return (minX, minY, maxX, maxY);
    }

    private void Tick()
    {
        _tree.Build(_x, _y, _mass);

        double repulsion = _options.Repulsion * Alpha;
        double theta = _options.Theta;

        // The repulsion pass is the whole cost of a step and it only reads the tree, so
        // above a few hundred nodes it is worth spreading over cores: on a five thousand
        // page vault that is the difference between twenty milliseconds a frame and four.
        if (_x.Length >= ParallelThreshold)
        {
            Parallel.For(0, _x.Length, i =>
            {
                (double fx, double fy) = _tree.Force(i, _x[i], _y[i], repulsion, theta);
                _vx[i] += fx;
                _vy[i] += fy;
            });
        }
        else
        {
            for (int i = 0; i < _x.Length; i++)
            {
                (double fx, double fy) = _tree.Force(i, _x[i], _y[i], repulsion, theta);
                _vx[i] += fx;
                _vy[i] += fy;
            }
        }

        for (int i = 0; i < _edgeSource.Length; i++)
        {
            int a = _edgeSource[i];
            int b = _edgeTarget[i];

            double dx = _x[b] - _x[a];
            double dy = _y[b] - _y[a];
            double distance = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance < 1e-6)
            {
                // Two nodes exactly on top of each other have no direction to separate
                // along; nudge deterministically by index so the next step has one.
                dx = ((i % 7) - 3) * 0.01;
                dy = ((i % 5) - 2) * 0.01;
                distance = Math.Sqrt((dx * dx) + (dy * dy));
            }

            double push = (distance - _options.LinkDistance) / distance * Alpha * _edgeStrength[i];

            _vx[a] += dx * push;
            _vy[a] += dy * push;
            _vx[b] -= dx * push;
            _vy[b] -= dy * push;
        }

        double gravity = _options.Gravity * Alpha;
        double decay = _options.VelocityDecay;

        for (int i = 0; i < _x.Length; i++)
        {
            _vx[i] -= _x[i] * gravity;
            _vy[i] -= _y[i] * gravity;

            _vx[i] *= decay;
            _vy[i] *= decay;

            _x[i] += _vx[i];
            _y[i] += _vy[i];
        }
    }


    /// <summary>
    /// A Barnes-Hut quadtree over the node positions, rebuilt each step into reused
    /// arrays. Allocating a tree of objects per frame is what turns a sixty frame budget
    /// into a garbage collection pause.
    /// </summary>
    private sealed class QuadTree
    {
        private double[] _geometryX = new double[64];
        private double[] _geometryY = new double[64];
        private double[] _size = new double[64];
        private double[] _mass = new double[64];
        private double[] _weightedX = new double[64];
        private double[] _weightedY = new double[64];
        private int[] _body = new int[64];
        private int[] _children = new int[64 * 4];

        private double[] _bodyX = [];
        private double[] _bodyY = [];
        private double[] _bodyMass = [];
        private int _count;

        /// <summary>Rebuilds the tree over the current positions.</summary>
        public void Build(double[] x, double[] y, double[] mass)
        {
            _bodyX = x;
            _bodyY = y;
            _bodyMass = mass;
            _count = 0;

            if (x.Length == 0)
            {
                return;
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            for (int i = 0; i < x.Length; i++)
            {
                minX = Math.Min(minX, x[i]);
                minY = Math.Min(minY, y[i]);
                maxX = Math.Max(maxX, x[i]);
                maxY = Math.Max(maxY, y[i]);
            }

            Reserve(Math.Max(64, x.Length * 4));
            Allocate(
                (minX + maxX) / 2,
                (minY + maxY) / 2,
                Math.Max(Math.Max(maxX - minX, maxY - minY), 1));

            for (int i = 0; i < x.Length; i++)
            {
                Insert(i);
            }
        }

        /// <summary>The repulsion one node feels from every other, approximated.</summary>
        public (double X, double Y) Force(int index, double x, double y, double strength, double theta) =>
            _count == 0 ? (0, 0) : Accumulate(0, index, x, y, strength, theta);

        private (double X, double Y) Accumulate(
            int node, int index, double x, double y, double strength, double theta)
        {
            double mass = _mass[node];

            if (mass <= 0)
            {
                return (0, 0);
            }

            double dx = (_weightedX[node] / mass) - x;
            double dy = (_weightedY[node] / mass) - y;
            double distanceSquared = (dx * dx) + (dy * dy);
            int body = _body[node];

            if (body >= 0)
            {
                // A leaf. The node's force on itself is not a force.
                if (body == index)
                {
                    return (0, 0);
                }

                return Pull(dx, dy, distanceSquared, mass, strength);
            }

            // Far enough away that the cell can stand in for everything inside it.
            if (_size[node] * _size[node] < theta * theta * distanceSquared)
            {
                return Pull(dx, dy, distanceSquared, mass, strength);
            }

            double fx = 0;
            double fy = 0;

            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                int child = _children[(node * 4) + quadrant];

                if (child <= 0)
                {
                    continue;
                }

                (double cx, double cy) = Accumulate(child, index, x, y, strength, theta);
                fx += cx;
                fy += cy;
            }

            return (fx, fy);
        }

        /// <summary>
        /// The inverse-square term. The distance floor is what keeps two pages that
        /// happen to land on the same point from launching each other off screen.
        /// </summary>
        private static (double X, double Y) Pull(
            double dx, double dy, double distanceSquared, double mass, double strength)
        {
            double clamped = Math.Max(distanceSquared, 1);
            double force = strength * mass / clamped;

            return (dx * force, dy * force);
        }

        private void Insert(int body)
        {
            int node = 0;
            double mass = _bodyMass[body];
            double x = _bodyX[body];
            double y = _bodyY[body];

            while (true)
            {
                _mass[node] += mass;
                _weightedX[node] += x * mass;
                _weightedY[node] += y * mass;

                int occupant = _body[node];

                if (occupant == -1)
                {
                    _body[node] = body;

                    return;
                }

                if (occupant >= 0)
                {
                    // Coincident bodies would subdivide forever; below this cell size they
                    // share a leaf and the simulation's nudge separates them instead.
                    if (_size[node] < 1e-3)
                    {
                        return;
                    }

                    _body[node] = -2;

                    int moved = Child(node, _bodyX[occupant], _bodyY[occupant]);
                    _mass[moved] += _bodyMass[occupant];
                    _weightedX[moved] += _bodyX[occupant] * _bodyMass[occupant];
                    _weightedY[moved] += _bodyY[occupant] * _bodyMass[occupant];
                    _body[moved] = occupant;
                }

                node = Child(node, x, y);
            }
        }

        private int Child(int node, double x, double y)
        {
            bool right = x >= _geometryX[node];
            bool down = y >= _geometryY[node];
            int slot = (node * 4) + (down ? 2 : 0) + (right ? 1 : 0);
            int child = _children[slot];

            if (child > 0)
            {
                return child;
            }

            double half = _size[node] / 2;

            child = Allocate(
                _geometryX[node] + (right ? half / 2 : -half / 2),
                _geometryY[node] + (down ? half / 2 : -half / 2),
                half);

            _children[slot] = child;

            return child;
        }

        private int Allocate(double centreX, double centreY, double size)
        {
            Reserve(_count + 1);

            int index = _count++;

            _geometryX[index] = centreX;
            _geometryY[index] = centreY;
            _size[index] = size;
            _mass[index] = 0;
            _weightedX[index] = 0;
            _weightedY[index] = 0;
            _body[index] = -1;

            int slot = index * 4;
            _children[slot] = 0;
            _children[slot + 1] = 0;
            _children[slot + 2] = 0;
            _children[slot + 3] = 0;

            return index;
        }

        private void Reserve(int capacity)
        {
            if (capacity <= _geometryX.Length)
            {
                return;
            }

            int size = Math.Max(capacity, _geometryX.Length * 2);

            Array.Resize(ref _geometryX, size);
            Array.Resize(ref _geometryY, size);
            Array.Resize(ref _size, size);
            Array.Resize(ref _mass, size);
            Array.Resize(ref _weightedX, size);
            Array.Resize(ref _weightedY, size);
            Array.Resize(ref _body, size);
            Array.Resize(ref _children, size * 4);
        }
    }
}
