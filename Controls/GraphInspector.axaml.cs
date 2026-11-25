using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GraphLab.Controls;
using GraphLab.Models;

namespace GraphLab.Controls;

public partial class GraphInspector : UserControl
{
    private GraphView? _canvas;
    private GraphValueKind _valueKind = GraphValueKind.Weight;

    public GraphInspector()
    {
        InitializeComponent();

        ApplyNodeButton.Click += (_, _) => ApplyNode();
        ApplyEdgeButton.Click += (_, _) => ApplyEdge();
        DeleteButton.Click += (_, _) => _canvas?.DeleteSelection();
        NodeLabelBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyNode();
                e.Handled = true;
            }
        };
        EdgeValueBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyEdge();
                e.Handled = true;
            }
        };
    }

    public void Attach(GraphView canvas, GraphValueKind valueKind)
    {
        _canvas = canvas;
        _valueKind = valueKind;
        EdgeValueLabel.Text = valueKind == GraphValueKind.Capacity ? "Пропускная способность" : "Вес";
        canvas.SelectionChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var node = _canvas?.SelectedNode;
        var edge = _canvas?.SelectedEdge;
        bool hasNode = node is not null;
        bool hasEdge = edge is not null;

        EmptyText.IsVisible = !hasNode && !hasEdge;
        NodePanel.IsVisible = hasNode;
        EdgePanel.IsVisible = hasEdge;
        DeleteButton.IsVisible = hasNode || hasEdge;
        SelectionKind.Text = hasNode ? "вершина" : hasEdge ? "ребро" : "ничего не выбрано";

        if (node is not null)
        {
            NodeTitle.Text = $"Вершина {node.Label}";
            if (NodeLabelBox.Text != node.Label) NodeLabelBox.Text = node.Label;
            int degree = _canvas?.Model?.Incident(node.Id).Count() ?? 0;
            NodeInfo.Text = $"Степень вершины: {degree}. Позиция: ({node.X:0}, {node.Y:0}). Изменения сразу отражаются на холсте.";
        }

        if (edge is not null && _canvas?.Model is { } model)
        {
            var from = model.FindNode(edge.From)?.Label ?? "?";
            var to = model.FindNode(edge.To)?.Label ?? "?";
            bool directed = model.Directed;
            EdgeTitle.Text = directed ? $"{from} → {to}" : $"{from} — {to}";
            var value = model.GetValue(edge, _valueKind);
            if (Math.Abs((double)(EdgeValueBox.Value ?? 0) - value) > 0.0001)
                EdgeValueBox.Value = (decimal)value;
            EdgeInfo.Text = _valueKind == GraphValueKind.Capacity
                ? $"Пропускная способность: {edge.Capacity:0.##}. Текущий поток: {edge.Flow:0.##}."
                : $"Вес ребра: {edge.Weight:0.##}.";
        }

        if (hasNode || hasEdge)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scroll = this.FindAncestorOfType<ScrollViewer>();
                if (scroll is not null)
                    scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            }, DispatcherPriority.Background);
        }
    }

    private void ApplyNode()
    {
        if (_canvas?.SelectedNode is not { } node || _canvas.Model is null) return;
        var text = NodeLabelBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            NodeInfo.Text = "Метка не может быть пустой.";
            return;
        }
        if (text.Length > 24) text = text[..24];
        if (node.Label == text) return;
        node.Label = text;
        _canvas.Model.NotifyChanged();
        _canvas.NotifyGraphEdited($"Метка вершины изменена на «{text}»");
        Refresh();
    }

    private void ApplyEdge()
    {
        if (_canvas?.SelectedEdge is not { } edge || _canvas.Model is null) return;
        double value = (double)(EdgeValueBox.Value ?? 1);
        value = Math.Clamp(Math.Round(value, 2), 0.1, 99999);
        _canvas.Model.SetValue(edge, _valueKind, value);
        edge.Flow = 0;
        _canvas.NotifyGraphEdited(_valueKind == GraphValueKind.Capacity
            ? "Пропускная способность ребра изменена"
            : "Вес ребра изменён");
        Refresh();
    }
}
