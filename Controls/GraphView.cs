using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using GraphLab.Models;
using GraphLab.Playback;

namespace GraphLab.Controls;

public enum GraphEditMode
{
    Select,
    AddNode,
    AddEdge,
    Delete
}

public enum EdgeLabelKind
{
    None,
    Weight,
    Capacity,
    CapacityFlow
}

public sealed class GraphView : Control
{
    private const double NodeRadius = 26;
    private static readonly Typeface UiTypeface = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface UiRegular = new("Inter", FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface MonoTypeface = new("Cascadia Mono", FontStyle.Normal, FontWeight.SemiBold);

    private static readonly Color Cyan = Color.Parse("#22D3EE");
    private static readonly Color CyanDark = Color.Parse("#0E7490");
    private static readonly Color Green = Color.Parse("#34D399");
    private static readonly Color GreenDark = Color.Parse("#115E59");
    private static readonly Color Amber = Color.Parse("#FBBF24");
    private static readonly Color AmberDark = Color.Parse("#78350F");
    private static readonly Color Red = Color.Parse("#F87171");
    private static readonly Color RedDark = Color.Parse("#3F1724");
    private static readonly Color Violet = Color.Parse("#A78BFA");
    private static readonly Color VioletDark = Color.Parse("#3B1D70");
    private static readonly Color Slate = Color.Parse("#4A5F86");
    private static readonly Color NodeFill = Color.Parse("#1A2942");
    private static readonly Color NodeBorder = Color.Parse("#3D5378");
    private static readonly Color TextMain = Color.Parse("#EEF5FF");
    private static readonly Color TextMuted = Color.Parse("#93A6C6");

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
    private static readonly Cursor SizeAllCursor = new(StandardCursorType.SizeAll);

    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Guid, Point> _displayPositions = new();
    private GraphModel? _model;
    private GraphEditMode _editMode = GraphEditMode.Select;
    private EdgeLabelKind _edgeLabelKind = EdgeLabelKind.Weight;
    private StepVisual? _visual;
    private GraphNode? _selectedNode;
    private GraphEdge? _selectedEdge;
    private Guid? _pendingFrom;
    private GraphNode? _dragNode;
    private Point _dragStartScreen;
    private bool _dragMoved;
    private Point _pointerScreen;
    private Guid? _hoverNode;
    private Guid? _hoverEdge;
    private double _scale = 1;
    private Point _offset;
    private double _phase;
    private double _newEdgeValue = 5;

    public GraphView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = ArrowCursor;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    public event Action<string>? GraphEdited;
    public event Action<string>? StatusMessage;
    public event EventHandler? SelectionChanged;

    public GraphModel? Model
    {
        get => _model;
        set
        {
            if (ReferenceEquals(_model, value)) return;
            if (_model != null) _model.Changed -= OnModelChanged;
            _model = value;
            if (_model != null) _model.Changed += OnModelChanged;
            _displayPositions.Clear();
            if (_model != null)
            {
                foreach (var node in _model.Nodes)
                    _displayPositions[node.Id] = new Point(node.X, node.Y);
            }
            _selectedNode = null;
            _selectedEdge = null;
            _pendingFrom = null;
            _dragNode = null;
            _visual = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            StatusMessage?.Invoke("Холст готов. Выберите режим редактирования или запустите алгоритм.");
            InvalidateVisual();
        }
    }

    public GraphEditMode EditMode
    {
        get => _editMode;
        set
        {
            if (_editMode == value) return;
            _editMode = value;
            _pendingFrom = null;
            _hoverNode = null;
            _hoverEdge = null;
            StatusMessage?.Invoke(value switch
            {
                GraphEditMode.Select => "Режим выбора: перетаскивайте вершины, кликайте по элементам для инспектора.",
                GraphEditMode.AddNode => "Кликните по пустому месту холста, чтобы добавить вершину.",
                GraphEditMode.AddEdge => "Кликните по первой вершине, затем по второй, чтобы создать ребро.",
                GraphEditMode.Delete => "Кликните по вершине или ребру, чтобы удалить их.",
                _ => ""
            });
            InvalidateVisual();
        }
    }

    public EdgeLabelKind EdgeLabelKind
    {
        get => _edgeLabelKind;
        set
        {
            _edgeLabelKind = value;
            InvalidateVisual();
        }
    }

    public double NewEdgeValue
    {
        get => _newEdgeValue;
        set => _newEdgeValue = Math.Max(0.01, value);
    }

    public GraphValueKind NewEdgeValueKind { get; set; } = GraphValueKind.Weight;

    public GraphNode? SelectedNode => _selectedNode;
    public GraphEdge? SelectedEdge => _selectedEdge;

    public void SetVisual(StepVisual? visual)
    {
        _visual = visual;
        InvalidateVisual();
    }

    public void ClearVisual()
    {
        _visual = null;
        InvalidateVisual();
    }

    public void SelectNode(GraphNode? node)
    {
        _selectedNode = node;
        _selectedEdge = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SelectEdge(GraphEdge? edge)
    {
        _selectedEdge = edge;
        _selectedNode = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        if (_selectedNode is null && _selectedEdge is null) return;
        _selectedNode = null;
        _selectedEdge = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void DeleteSelection()
    {
        if (_model is null) return;
        if (_selectedNode is not null)
        {
            string label = _selectedNode.Label;
            _model.RemoveNode(_selectedNode.Id);
            _selectedNode = null;
            _selectedEdge = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            NotifyGraphEdited($"Вершина {label} удалена");
        }
        else if (_selectedEdge is not null)
        {
            _model.RemoveEdge(_selectedEdge.Id);
            _selectedEdge = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            NotifyGraphEdited("Ребро удалено");
        }
    }

    public void NotifyGraphEdited(string message)
    {
        _model?.NotifyChanged();
        GraphEdited?.Invoke(message);
        StatusMessage?.Invoke(message);
        InvalidateVisual();
    }

    public void RefreshDisplayPositions(bool immediate)
    {
        if (_model is null) return;
        if (immediate)
        {
            _displayPositions.Clear();
            foreach (var node in _model.Nodes)
                _displayPositions[node.Id] = new Point(node.X, node.Y);
        }
        InvalidateVisual();
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (_model is null) return;
        if (_selectedNode is not null && !_model.Nodes.Any(n => n.Id == _selectedNode.Id))
            _selectedNode = null;
        if (_selectedEdge is not null && !_model.Edges.Any(x => x.Id == _selectedEdge.Id))
            _selectedEdge = null;
        if (_pendingFrom.HasValue && _model.FindNode(_pendingFrom.Value) is null)
            _pendingFrom = null;

        var alive = new HashSet<Guid>(_model.Nodes.Select(n => n.Id));
        foreach (var id in _displayPositions.Keys.Where(id => !alive.Contains(id)).ToList())
            _displayPositions.Remove(id);
        foreach (var node in _model.Nodes)
        {
            if (!_displayPositions.ContainsKey(node.Id))
                _displayPositions[node.Id] = new Point(node.X, node.Y);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _phase += 0.033;
        if (_model is null) return;

        bool moving = false;
        foreach (var node in _model.Nodes)
        {
            if (!_displayPositions.TryGetValue(node.Id, out var display))
            {
                _displayPositions[node.Id] = new Point(node.X, node.Y);
                continue;
            }
            double dx = node.X - display.X;
            double dy = node.Y - display.Y;
            if (Math.Abs(dx) > 0.2 || Math.Abs(dy) > 0.2)
            {
                _displayPositions[node.Id] = new Point(display.X + dx * 0.18, display.Y + dy * 0.18);
                moving = true;
            }
            else if (display.X != node.X || display.Y != node.Y)
            {
                _displayPositions[node.Id] = new Point(node.X, node.Y);
            }
        }

        bool animated = _pendingFrom.HasValue || _dragNode is not null;
        if (_visual is not null)
        {
            animated |= _visual.ActiveNodes.Count > 0 || _visual.PathEdges.Count > 0 ||
                        _visual.PathNodes.Count > 0 || _visual.ActiveEdges.Count > 0;
        }
        if (moving || animated) InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || _model is null) return;

        var screen = point.Position;
        _pointerScreen = screen;
        var world = ScreenToWorld(screen);
        var node = HitNode(screen);
        var edge = node is null ? HitEdge(screen) : null;

        switch (_editMode)
        {
            case GraphEditMode.Select:
                if (node is not null)
                {
                    SelectNode(node);
                    _dragNode = node;
                    _dragStartScreen = screen;
                    _dragMoved = false;
                    if (_displayPositions.ContainsKey(node.Id))
                        _displayPositions[node.Id] = new Point(node.X, node.Y);
                    e.Pointer.Capture(this);
                    Cursor = SizeAllCursor;
                }
                else if (edge is not null)
                {
                    SelectEdge(edge);
                }
                else
                {
                    ClearSelection();
                }
                break;

            case GraphEditMode.AddNode:
                if (node is not null)
                {
                    SelectNode(node);
                }
                else if (IsInsideWorld(world))
                {
                    var created = _model.AddNode(null, world.X, world.Y);
                    _displayPositions[created.Id] = new Point(created.X, created.Y);
                    SelectNode(created);
                    NotifyGraphEdited($"Добавлена вершина {created.Label}");
                }
                break;

            case GraphEditMode.AddEdge:
                if (node is null)
                {
                    if (_pendingFrom.HasValue)
                    {
                        _pendingFrom = null;
                        StatusMessage?.Invoke("Создание ребра отменено.");
                        InvalidateVisual();
                    }
                    break;
                }

                if (!_pendingFrom.HasValue)
                {
                    _pendingFrom = node.Id;
                    SelectNode(node);
                    StatusMessage?.Invoke($"Первая вершина: {node.Label}. Теперь кликните по второй вершине.");
                    InvalidateVisual();
                }
                else if (_pendingFrom.Value == node.Id)
                {
                    StatusMessage?.Invoke("Петли не поддерживаются — выберите другую вершину.");
                }
                else
                {
                    var fromId = _pendingFrom.Value;
                    var created = _model.AddEdge(fromId, node.Id, _newEdgeValue, NewEdgeValueKind);
                    _pendingFrom = null;
                    if (created is null)
                    {
                        StatusMessage?.Invoke("Такое ребро уже существует (или недопустимо).");
                    }
                    else
                    {
                        SelectEdge(created);
                        NotifyGraphEdited("Ребро добавлено");
                    }
                    InvalidateVisual();
                }
                break;

            case GraphEditMode.Delete:
                if (node is not null)
                {
                    string label = node.Label;
                    _model.RemoveNode(node.Id);
                    _pendingFrom = null;
                    ClearSelection();
                    NotifyGraphEdited($"Вершина {label} удалена");
                }
                else if (edge is not null)
                {
                    _model.RemoveEdge(edge.Id);
                    ClearSelection();
                    NotifyGraphEdited("Ребро удалено");
                }
                break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_model is null) return;
        var screen = e.GetPosition(this);
        _pointerScreen = screen;

        if (_dragNode is not null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (Math.Abs(screen.X - _dragStartScreen.X) > 2 || Math.Abs(screen.Y - _dragStartScreen.Y) > 2)
                _dragMoved = true;
            var world = ScreenToWorld(screen);
            _dragNode.X = GraphModel.ClampX(world.X);
            _dragNode.Y = GraphModel.ClampY(world.Y);
            _displayPositions[_dragNode.Id] = new Point(_dragNode.X, _dragNode.Y);
            Cursor = SizeAllCursor;
            InvalidateVisual();
            return;
        }

        var node = HitNode(screen);
        var edge = node is null ? HitEdge(screen) : null;
        var newHoverNode = node?.Id;
        var newHoverEdge = edge?.Id;
        if (newHoverNode != _hoverNode || newHoverEdge != _hoverEdge)
        {
            _hoverNode = newHoverNode;
            _hoverEdge = newHoverEdge;
            InvalidateVisual();
        }

        Cursor = _pendingFrom.HasValue
            ? HandCursor
            : node is not null || edge is not null
                ? HandCursor
                : _editMode == GraphEditMode.AddNode ? CrossCursor : ArrowCursor;

        if (_pendingFrom.HasValue) InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragNode is not null)
        {
            var node = _dragNode;
            _dragNode = null;
            e.Pointer.Capture(null);
            if (_dragMoved)
            {
                _model?.NotifyChanged();
                NotifyGraphEdited($"Положение вершины {node.Label} изменено");
            }
            Cursor = HandCursor;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverNode is null && _hoverEdge is null) return;
        _hoverNode = null;
        _hoverEdge = null;
        Cursor = ArrowCursor;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (_pendingFrom.HasValue)
            {
                _pendingFrom = null;
                StatusMessage?.Invoke("Создание ребра отменено.");
                InvalidateVisual();
                e.Handled = true;
            }
            else if (_selectedNode is not null || _selectedEdge is not null)
            {
                ClearSelection();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_selectedNode is not null || _selectedEdge is not null)
            {
                DeleteSelection();
                e.Handled = true;
            }
        }
    }

    private bool IsInsideWorld(Point world) =>
        world.X >= GraphModel.Margin && world.X <= GraphModel.WorldWidth - GraphModel.Margin &&
        world.Y >= GraphModel.Margin && world.Y <= GraphModel.WorldHeight - GraphModel.Margin;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureViewport();
        if (Bounds.Width <= 1 || Bounds.Height <= 1) return;

        DrawBackdrop(context);
        if (_model is null) return;

        var nodesById = _model.Nodes.ToDictionary(n => n.Id);
        DrawEdges(context, nodesById);
        DrawPendingEdge(context, nodesById);
        DrawNodes(context);
    }

    private void EnsureViewport()
    {
        double availableWidth = Math.Max(1, Bounds.Width - 24);
        double availableHeight = Math.Max(1, Bounds.Height - 24);
        _scale = Math.Min(availableWidth / GraphModel.WorldWidth, availableHeight / GraphModel.WorldHeight);
        if (!double.IsFinite(_scale) || _scale <= 0) _scale = 1;
        _scale = Math.Clamp(_scale, 0.25, 1.7);
        double w = GraphModel.WorldWidth * _scale;
        double h = GraphModel.WorldHeight * _scale;
        _offset = new Point((Bounds.Width - w) / 2, (Bounds.Height - h) / 2);
    }

    private Point ToScreen(Point world) => new(_offset.X + world.X * _scale, _offset.Y + world.Y * _scale);
    private Point ScreenToWorld(Point screen) => new((screen.X - _offset.X) / _scale, (screen.Y - _offset.Y) / _scale);
    private double ScreenRadius => Math.Clamp(NodeRadius * _scale, 13, 32);

    private void DrawBackdrop(DrawingContext context)
    {
        var worldRect = new Rect(ToScreen(new Point(0, 0)), ToScreen(new Point(GraphModel.WorldWidth, GraphModel.WorldHeight)));
        var glowA = new SolidColorBrush(Color.FromArgb(22, 34, 211, 238));
        var glowB = new SolidColorBrush(Color.FromArgb(18, 129, 140, 248));
        context.DrawEllipse(glowA, null, new Point(Bounds.Width * 0.12, Bounds.Height * 0.10), 280, 190);
        context.DrawEllipse(glowB, null, new Point(Bounds.Width * 0.90, Bounds.Height * 0.92), 320, 220);

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(20, 160, 190, 230)), 1);
        const double step = 50;
        for (double x = 0; x <= GraphModel.WorldWidth + 0.1; x += step)
        {
            var a = ToScreen(new Point(x, 0));
            var b = ToScreen(new Point(x, GraphModel.WorldHeight));
            context.DrawLine(gridPen, a, b);
        }
        for (double y = 0; y <= GraphModel.WorldHeight + 0.1; y += step)
        {
            var a = ToScreen(new Point(0, y));
            var b = ToScreen(new Point(GraphModel.WorldWidth, y));
            context.DrawLine(gridPen, a, b);
        }

        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 55, 80, 116)), 1);
        context.DrawRectangle(null, borderPen, worldRect, 14, 14);
    }

    private Point GetDisplayPosition(GraphNode node)
    {
        return _displayPositions.TryGetValue(node.Id, out var p) ? p : new Point(node.X, node.Y);
    }

    private EdgeGeometry? GetEdgeGeometry(GraphEdge edge, IReadOnlyDictionary<Guid, GraphNode> nodes)
    {
        if (!nodes.TryGetValue(edge.From, out var from) || !nodes.TryGetValue(edge.To, out var to))
            return null;
        if (from.Id == to.Id) return null;

        var a = ToScreen(GetDisplayPosition(from));
        var b = ToScreen(GetDisplayPosition(to));
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return null;

        double nx = -dy / length;
        double ny = dx / length;
        double offset = 0;
        if (_model is { Directed: true })
        {
            var reverse = _model.Edges.FirstOrDefault(x => x.From == edge.To && x.To == edge.From);
            if (reverse is not null)
            {
                offset = edge.Id.CompareTo(reverse.Id) < 0 ? 8 : -8;
            }
        }

        var aa = new Point(a.X + nx * offset, a.Y + ny * offset);
        var bb = new Point(b.X + nx * offset, b.Y + ny * offset);
        dx = bb.X - aa.X;
        dy = bb.Y - aa.Y;
        length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return null;
        double ux = dx / length;
        double uy = dy / length;

        double radius = ScreenRadius;
        double arrowTrim = _model is { Directed: true } ? 9 : 0;
        var start = new Point(aa.X + ux * radius, aa.Y + uy * radius);
        var end = new Point(bb.X - ux * (radius + arrowTrim), bb.Y - uy * (radius + arrowTrim));
        return new EdgeGeometry(start, end, nx, ny, offset, ux, uy);
    }

    private void DrawEdges(DrawingContext context, IReadOnlyDictionary<Guid, GraphNode> nodes)
    {
        if (_model is null) return;

        foreach (var edge in _model.Edges)
        {
            var geometry = GetEdgeGeometry(edge, nodes);
            if (geometry is null) continue;
            var g = geometry.Value;

            bool isPath = _visual?.PathEdges.Contains(edge.Id) == true;
            bool isPathNode = _visual?.PathNodes.Contains(edge.From) == true && _visual?.PathNodes.Contains(edge.To) == true;
            bool isActive = _visual?.ActiveEdges.Contains(edge.Id) == true;
            bool isAccepted = _visual?.AcceptedEdges.Contains(edge.Id) == true;
            bool isRejected = _visual?.RejectedEdges.Contains(edge.Id) == true;
            bool isBack = _visual?.BackEdges.Contains(edge.Id) == true;
            bool isSelected = _selectedEdge?.Id == edge.Id;
            bool isHover = _hoverEdge == edge.Id;

            Color color;
            double width;
            IDashStyle? dash = null;
            bool glow = false;

            if (isPath)
            {
                color = Cyan;
                width = 4.2;
                glow = true;
                if (isBack) dash = new DashStyle(new double[] { 4, 3 }, 0);
            }
            else if (isActive)
            {
                color = Amber;
                width = 3.6;
                glow = true;
                dash = new DashStyle(new double[] { 5, 3 }, _phase * 20 % 8);
            }
            else if (isBack)
            {
                color = Violet;
                width = 2.6;
                dash = new DashStyle(new double[] { 3, 3 }, 0);
            }
            else if (isRejected)
            {
                color = Red;
                width = 2.4;
                dash = new DashStyle(new double[] { 4, 3 }, 0);
            }
            else if (isSelected)
            {
                color = Color.Parse("#F8FAFC");
                width = 3.6;
                glow = true;
            }
            else if (isAccepted)
            {
                color = Green;
                width = 3.1;
                glow = true;
            }
            else if (isPathNode)
            {
                color = Color.Parse("#2B6C7C");
                width = 2.2;
            }
            else
            {
                color = isHover ? Color.Parse("#6C86AD") : Slate;
                width = isHover ? 2.6 : 2.0;
            }

            if (glow)
            {
                var glowBrush = new SolidColorBrush(Color.FromArgb(42, color.R, color.G, color.B));
                var glowPen = new Pen(glowBrush, width + 7, dash, PenLineCap.Round);
                context.DrawLine(glowPen, g.Start, g.End);
            }

            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush, width, dash, PenLineCap.Round);
            context.DrawLine(pen, g.Start, g.End);

            if (_model.Directed && !isRejected)
            {
                double arrowLength = Math.Max(8, ScreenRadius * 0.42);
                double arrowWidth = arrowLength * 0.55;
                var tip = g.End;
                var baseCenter = new Point(tip.X - g.Ux * arrowLength, tip.Y - g.Uy * arrowLength);
                var p1 = new Point(baseCenter.X + g.Nx * arrowWidth, baseCenter.Y + g.Ny * arrowWidth);
                var p2 = new Point(baseCenter.X - g.Nx * arrowWidth, baseCenter.Y - g.Ny * arrowWidth);
                var geo = new StreamGeometry();
                using (var geoContext = geo.Open())
                {
                    geoContext.BeginFigure(tip, true);
                    geoContext.LineTo(p1);
                    geoContext.LineTo(p2);
                    geoContext.EndFigure(true);
                }
                context.DrawGeometry(brush, null, geo);
            }

            if (isPath)
            {
                DrawFlowParticles(context, g);
            }

            DrawEdgeLabel(context, edge, g, isPath, isActive, isAccepted, isRejected, isBack, isSelected);
        }
    }

    private void DrawFlowParticles(DrawingContext context, EdgeGeometry g)
    {
        double length = Math.Sqrt(Math.Pow(g.End.X - g.Start.X, 2) + Math.Pow(g.End.Y - g.Start.Y, 2));
        if (length < 26) return;
        var core = new SolidColorBrush(Color.FromArgb(230, 236, 254, 255));
        for (int i = 0; i < 3; i++)
        {
            double t = ((_phase * 0.32 + i / 3.0) % 1.0);
            double px = g.Start.X + (g.End.X - g.Start.X) * t;
            double py = g.Start.Y + (g.End.Y - g.Start.Y) * t;
            context.DrawEllipse(core, null, new Point(px, py), 2.8, 2.8);
        }
    }

    private void DrawEdgeLabel(DrawingContext context, GraphEdge edge, EdgeGeometry g,
        bool isPath, bool isActive, bool isAccepted, bool isRejected, bool isBack, bool isSelected)
    {
        string? text = null;
        if (_visual is not null && _visual.EdgeLabels.TryGetValue(edge.Id, out var annotated))
            text = annotated;
        else if (_edgeLabelKind == EdgeLabelKind.Weight)
            text = edge.Weight.ToString("0.##");
        else if (_edgeLabelKind == EdgeLabelKind.Capacity)
            text = edge.Capacity.ToString("0.##");
        else if (_edgeLabelKind == EdgeLabelKind.CapacityFlow)
            text = $"{edge.Flow:0.##}/{edge.Capacity:0.##}";

        if (string.IsNullOrWhiteSpace(text) || _scale < 0.42) return;

        Color textColor = TextMain;
        Color borderColor = Color.Parse("#3A4E70");
        if (isPath) { textColor = Color.Parse("#C9F7FF"); borderColor = Cyan; }
        else if (isActive) { textColor = Color.Parse("#FFF3C4"); borderColor = Amber; }
        else if (isBack) { textColor = Color.Parse("#DDD2FF"); borderColor = Violet; }
        else if (isRejected) { textColor = Color.Parse("#FFD1D1"); borderColor = Red; }
        else if (isAccepted) { textColor = Color.Parse("#CFFCE8"); borderColor = Green; }
        else if (isSelected) { textColor = Color.Parse("#FFFFFF"); borderColor = Color.Parse("#F8FAFC"); }

        var ft = MakeText(text, _scale < 0.62 ? 8.5 : 9.5, new SolidColorBrush(textColor), UiTypeface, MonoTypeface);
        double chipW = ft.Width + 9;
        double chipH = ft.Height + 4;
        var center = new Point((g.Start.X + g.End.X) / 2 - g.Nx * 13, (g.Start.Y + g.End.Y) / 2 - g.Ny * 13);
        var rect = new Rect(center.X - chipW / 2, center.Y - chipH / 2, chipW, chipH);
        var chipBrush = new SolidColorBrush(Color.FromArgb(232, 9, 16, 29));
        var chipPen = new Pen(new SolidColorBrush(Color.FromArgb(160, borderColor.R, borderColor.G, borderColor.B)), 1);
        context.DrawRectangle(chipBrush, chipPen, rect, chipH / 2, chipH / 2);
        context.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    private void DrawPendingEdge(DrawingContext context, IReadOnlyDictionary<Guid, GraphNode> nodes)
    {
        if (_pendingFrom is null || _model is null) return;
        if (!nodes.TryGetValue(_pendingFrom.Value, out var from)) return;
        var a = ToScreen(GetDisplayPosition(from));
        var b = _pointerScreen;
        var brush = new SolidColorBrush(Color.FromArgb(230, 251, 191, 36));
        var pen = new Pen(brush, 2.2, new DashStyle(new double[] { 5, 4 }, _phase * 24 % 9), PenLineCap.Round);
        context.DrawLine(pen, a, b);
        context.DrawEllipse(brush, null, b, 4, 4);
        var ft = MakeText("выберите вторую вершину", 10, new SolidColorBrush(Color.FromArgb(220, 255, 243, 196)), UiRegular);
        var pos = new Point(b.X + 12, b.Y - 10);
        context.DrawText(ft, pos);
    }

    private void DrawNodes(DrawingContext context)
    {
        if (_model is null) return;
        double radius = ScreenRadius;
        foreach (var node in _model.Nodes)
        {
            var center = ToScreen(GetDisplayPosition(node));
            bool isActive = _visual?.ActiveNodes.Contains(node.Id) == true;
            bool isPath = _visual?.PathNodes.Contains(node.Id) == true;
            bool isVisited = _visual?.VisitedNodes.Contains(node.Id) == true;
            bool isFrontier = _visual?.FrontierNodes.Contains(node.Id) == true;
            bool isSelected = _selectedNode?.Id == node.Id;
            bool isHover = _hoverNode == node.Id;
            bool isPending = _pendingFrom == node.Id;

            if (isActive || isPending)
            {
                double pulse = radius + 5 + 4 * (0.5 + 0.5 * Math.Sin(_phase * 4.2 + node.Id.GetHashCode() % 10));
                var pulseBrush = new SolidColorBrush(Color.FromArgb(150, 251, 191, 36));
                context.DrawEllipse(null, new Pen(pulseBrush, 1.8), center, pulse, pulse);
            }
            else if (isPath)
            {
                var pathRing = new SolidColorBrush(Color.FromArgb(90, 34, 211, 238));
                context.DrawEllipse(null, new Pen(pathRing, 2), center, radius + 4, radius + 4);
            }

            var shadowBrush = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
            context.DrawEllipse(shadowBrush, null, new Point(center.X + 2, center.Y + 4), radius, radius);

            Color fill;
            Color border;
            if (isPath) { fill = Color.Parse("#0B5564"); border = Cyan; }
            else if (isActive) { fill = Color.Parse("#5B3B0B"); border = Amber; }
            else if (isVisited) { fill = Color.Parse("#103E3B"); border = Green; }
            else if (isFrontier) { fill = Color.Parse("#2A1A50"); border = Violet; }
            else if (isHover) { fill = Color.Parse("#22344F"); border = Color.Parse("#5A739B"); }
            else { fill = NodeFill; border = NodeBorder; }

            var fillBrush = new SolidColorBrush(fill);
            var borderBrush = new SolidColorBrush(border);
            var pen = new Pen(borderBrush, isSelected ? 2.8 : isPath || isActive ? 2.2 : 1.6);
            context.DrawEllipse(fillBrush, pen, center, radius, radius);

            // inner highlight
            var inner = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            context.DrawEllipse(inner, null, new Point(center.X - radius * 0.22, center.Y - radius * 0.28), radius * 0.48, radius * 0.36);

            if (isSelected)
            {
                var selPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 248, 250, 252)), 1.2, new DashStyle(new double[] { 3, 3 }, 0));
                context.DrawEllipse(null, selPen, center, radius + 5, radius + 5);
            }

            double fontSize = Math.Clamp(15 * _scale, 9.5, 16);
            if (node.Label.Length > 2)
                fontSize = Math.Max(8, Math.Min(fontSize, radius * 1.7 / (node.Label.Length * 0.62)));
            var label = MakeText(node.Label, fontSize, new SolidColorBrush(TextMain), UiTypeface);
            context.DrawText(label, new Point(center.X - label.Width / 2, center.Y - label.Height / 2));

            if (_visual is not null && _visual.NodeTags.TryGetValue(node.Id, out var tag))
                DrawNodeTag(context, center, radius, tag);

            if (_visual is not null && _visual.NodeBadges.TryGetValue(node.Id, out var badge) && !string.IsNullOrWhiteSpace(badge))
                DrawNodeBadge(context, center, radius, badge, border, isActive || isPath);
        }
    }

    private void DrawNodeTag(DrawingContext context, Point center, double radius, string tag)
    {
        Color color = tag == "S" ? Green : tag == "T" ? Red : Violet;
        var ft = MakeText(tag, 9.5, new SolidColorBrush(Color.FromArgb(255, 6, 15, 26)), UiTypeface);
        double w = Math.Max(17, ft.Width + 9);
        double h = 17;
        var rect = new Rect(center.X - w / 2, center.Y - radius - h - 7, w, h);
        var brush = new SolidColorBrush(Color.FromArgb(235, color.R, color.G, color.B));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(255, 220, 255, 245)), 1);
        context.DrawRectangle(brush, pen, rect, h / 2, h / 2);
        context.DrawText(ft, new Point(rect.Center.X - ft.Width / 2, rect.Center.Y - ft.Height / 2));
    }

    private void DrawNodeBadge(DrawingContext context, Point center, double radius, string badge, Color borderColor, bool strong)
    {
        var ft = MakeText(badge, 9, new SolidColorBrush(strong ? Color.Parse("#E6FEFF") : TextMuted), MonoTypeface);
        double w = ft.Width + 10;
        double h = 17;
        var rect = new Rect(center.X - w / 2, center.Y + radius + 8, w, h);
        var brush = new SolidColorBrush(Color.FromArgb(235, 8, 14, 26));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(strong ? (byte)220 : (byte)120, borderColor.R, borderColor.G, borderColor.B)), 1);
        context.DrawRectangle(brush, pen, rect, h / 2, h / 2);
        context.DrawText(ft, new Point(rect.Center.X - ft.Width / 2, rect.Center.Y - ft.Height / 2));
    }

    private GraphNode? HitNode(Point screen)
    {
        if (_model is null) return null;
        double radius = ScreenRadius + 4;
        for (int i = _model.Nodes.Count - 1; i >= 0; i--)
        {
            var node = _model.Nodes[i];
            var center = ToScreen(GetDisplayPosition(node));
            double dx = screen.X - center.X;
            double dy = screen.Y - center.Y;
            if (dx * dx + dy * dy <= radius * radius) return node;
        }
        return null;
    }

    private GraphEdge? HitEdge(Point screen)
    {
        if (_model is null) return null;
        var nodes = _model.Nodes.ToDictionary(n => n.Id);
        GraphEdge? best = null;
        double bestDistance = 12;
        foreach (var edge in _model.Edges)
        {
            var g = GetEdgeGeometry(edge, nodes);
            if (g is null) continue;
            double distance = DistanceToSegment(screen, g.Value.Start, g.Value.End);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = edge;
            }
        }
        return best;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared < 1e-6) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        double px = a.X + t * dx;
        double py = a.Y + t * dy;
        return Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
    }

    private static FormattedText MakeText(string text, double size, IBrush brush, params Typeface[] typefaces)
    {
        var typeface = typefaces.Length > 0 ? typefaces[0] : UiRegular;
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
    }

    private readonly record struct EdgeGeometry(
        Point Start,
        Point End,
        double Nx,
        double Ny,
        double Offset,
        double Ux,
        double Uy);
}
