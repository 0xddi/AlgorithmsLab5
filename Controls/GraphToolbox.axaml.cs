using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using GraphLab.Models;

namespace GraphLab.Controls;

public partial class GraphToolbox : UserControl
{
    private GraphView? _canvas;
    private bool _syncing;
    private bool _syncingModel;
    private GraphValueKind _valueKind = GraphValueKind.Weight;
    private bool _showDirected;

    public GraphToolbox()
    {
        InitializeComponent();

        SelectModeButton.Click += (_, _) => SetMode(GraphEditMode.Select);
        AddNodeButton.Click += (_, _) => SetMode(GraphEditMode.AddNode);
        AddEdgeButton.Click += (_, _) => SetMode(GraphEditMode.AddEdge);
        DeleteModeButton.Click += (_, _) => SetMode(GraphEditMode.Delete);

        ValueBox.PropertyChanged += (_, args) =>
        {
            if (args.Property != NumericUpDown.ValueProperty || _canvas is null) return;
            _canvas.NewEdgeValue = (double)(ValueBox.Value ?? 5);
        };

        LayoutButton.Click += (_, _) =>
        {
            if (_canvas?.Model is null) return;
            var kind = LayoutBox.SelectedIndex switch
            {
                1 => LayoutKind.Grid,
                2 => LayoutKind.Force,
                _ => LayoutKind.Circle
            };
            GraphLayout.Apply(_canvas.Model, kind);
            _canvas.RefreshDisplayPositions(false);
            _canvas.NotifyGraphEdited("Раскладка вершин изменена");
        };

        SampleButton.Click += (_, _) => SampleRequested?.Invoke(this, EventArgs.Empty);
        ClearButton.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);

        DirectedCheck.Click += (_, _) =>
        {
            if (_syncingModel || !_showDirected || _canvas?.Model is null) return;
            bool directed = DirectedCheck.IsChecked == true;
            if (_canvas.Model.Directed == directed) return;
            _canvas.Model.Directed = directed;
            _canvas.NotifyGraphEdited(directed ? "Граф стал ориентированным" : "Граф стал неориентированным");
        };
    }

    public event EventHandler? SampleRequested;
    public event EventHandler? ClearRequested;

    public void Attach(GraphView canvas, GraphValueKind valueKind, bool showDirected, bool showValue = true)
    {
        _canvas = canvas;
        _valueKind = valueKind;
        _showDirected = showDirected;
        ValueLabel.Text = valueKind == GraphValueKind.Capacity ? "Пропускная способность" : "Вес ребра";
        ValueBox.IsVisible = showValue;
        ValueLabel.IsVisible = showValue;
        DirectedCheck.IsVisible = showDirected;
        canvas.NewEdgeValueKind = valueKind;
        canvas.NewEdgeValue = (double)(ValueBox.Value ?? 5);
        SetMode(GraphEditMode.Select);
        RefreshModelState();
    }

    public void RefreshModelState()
    {
        _syncingModel = true;
        try
        {
            if (DirectedCheck.IsVisible)
                DirectedCheck.IsChecked = _canvas?.Model?.Directed == true;
        }
        finally
        {
            _syncingModel = false;
        }
    }

    private void SetMode(GraphEditMode mode)
    {
        if (_syncing) return;
        _syncing = true;
        SelectModeButton.IsChecked = mode == GraphEditMode.Select;
        AddNodeButton.IsChecked = mode == GraphEditMode.AddNode;
        AddEdgeButton.IsChecked = mode == GraphEditMode.AddEdge;
        DeleteModeButton.IsChecked = mode == GraphEditMode.Delete;
        _syncing = false;
        if (_canvas is not null) _canvas.EditMode = mode;
    }
}
