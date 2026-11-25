using System;
using System.Collections.Generic;
using System.Linq;

namespace GraphLab.Models;

public enum GraphValueKind
{
    Weight,
    Capacity
}

public sealed class GraphNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "A";
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class GraphEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid From { get; set; }
    public Guid To { get; set; }
    public double Weight { get; set; } = 1;
    public double Capacity { get; set; } = 1;
    public double Flow { get; set; }
}

public sealed class GraphModel
{
    public const double WorldWidth = 1000;
    public const double WorldHeight = 660;
    public const double Margin = 48;

    public List<GraphNode> Nodes { get; } = new();
    public List<GraphEdge> Edges { get; } = new();
    public bool Directed { get; set; }

    public event EventHandler? Changed;

    public GraphNode? FindNode(Guid id) => Nodes.FirstOrDefault(n => n.Id == id);

    public GraphEdge? FindEdge(Guid id) => Edges.FirstOrDefault(e => e.Id == id);

    public GraphNode? NodeAtEnd(GraphEdge edge, Guid nodeId)
    {
        if (edge.From == nodeId) return FindNode(edge.To);
        if (edge.To == nodeId) return FindNode(edge.From);
        return null;
    }

    public double GetValue(GraphEdge edge, GraphValueKind kind) =>
        kind == GraphValueKind.Capacity ? edge.Capacity : edge.Weight;

    public void SetValue(GraphEdge edge, GraphValueKind kind, double value)
    {
        value = Math.Max(0.01, Math.Round(value, 2));
        if (kind == GraphValueKind.Capacity)
            edge.Capacity = value;
        else
            edge.Weight = value;
    }

    public GraphNode AddNode(string? label, double x, double y)
    {
        var node = new GraphNode
        {
            Label = string.IsNullOrWhiteSpace(label) ? NextLabel() : label.Trim(),
            X = ClampX(x),
            Y = ClampY(y)
        };
        Nodes.Add(node);
        NotifyChanged();
        return node;
    }

    public GraphEdge? AddEdge(Guid from, Guid to, double value, GraphValueKind? valueKind = null)
    {
        if (from == to) return null;
        if (FindNode(from) is null || FindNode(to) is null) return null;

        bool duplicate = Directed
            ? Edges.Any(e => e.From == from && e.To == to)
            : Edges.Any(e => (e.From == from && e.To == to) || (e.From == to && e.To == from));
        if (duplicate) return null;

        value = Math.Max(0.01, Math.Round(value, 2));
        var edge = new GraphEdge
        {
            From = from,
            To = to,
            Weight = value,
            Capacity = value
        };
        if (valueKind == GraphValueKind.Weight) edge.Capacity = value;
        if (valueKind == GraphValueKind.Capacity) edge.Weight = value;

        Edges.Add(edge);
        NotifyChanged();
        return edge;
    }

    public void RemoveNode(Guid id)
    {
        Edges.RemoveAll(e => e.From == id || e.To == id);
        Nodes.RemoveAll(n => n.Id == id);
        NotifyChanged();
    }

    public void RemoveEdge(Guid id)
    {
        Edges.RemoveAll(e => e.Id == id);
        NotifyChanged();
    }

    public void Clear()
    {
        Nodes.Clear();
        Edges.Clear();
        NotifyChanged();
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void ResetFlows()
    {
        foreach (var edge in Edges) edge.Flow = 0;
    }

    public string NextLabel()
    {
        var used = new HashSet<string>(Nodes.Select(n => n.Label), StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 200; i++)
        {
            string candidate = i < 26
                ? ((char)('A' + i)).ToString()
                : ((char)('A' + (i % 26))).ToString() + (i / 26 + 1);
            if (!used.Contains(candidate)) return candidate;
        }
        return "V" + (Nodes.Count + 1);
    }

    public IEnumerable<(GraphEdge Edge, bool Outgoing, GraphNode Other)> Incident(Guid nodeId)
    {
        foreach (var edge in Edges)
        {
            if (edge.From == nodeId)
            {
                var other = FindNode(edge.To);
                if (other != null) yield return (edge, true, other);
            }
            else if (!Directed && edge.To == nodeId)
            {
                var other = FindNode(edge.From);
                if (other != null) yield return (edge, false, other);
            }
        }
    }

    public IEnumerable<(GraphNode Neighbor, GraphEdge Edge, bool Outgoing)> Neighbors(Guid nodeId)
    {
        foreach (var edge in Edges)
        {
            if (edge.From == nodeId)
            {
                var other = FindNode(edge.To);
                if (other != null) yield return (other, edge, true);
            }
            else if (!Directed && edge.To == nodeId)
            {
                var other = FindNode(edge.From);
                if (other != null) yield return (other, edge, false);
            }
        }
    }

    public GraphModel Clone()
    {
        var clone = new GraphModel { Directed = Directed };
        foreach (var node in Nodes)
            clone.Nodes.Add(new GraphNode { Id = node.Id, Label = node.Label, X = node.X, Y = node.Y });
        foreach (var edge in Edges)
            clone.Edges.Add(new GraphEdge
            {
                Id = edge.Id,
                From = edge.From,
                To = edge.To,
                Weight = edge.Weight,
                Capacity = edge.Capacity,
                Flow = edge.Flow
            });
        return clone;
    }

    public static double ClampX(double x) => Math.Clamp(x, Margin, WorldWidth - Margin);
    public static double ClampY(double y) => Math.Clamp(y, Margin, WorldHeight - Margin);
}

public sealed class NodeOption
{
    public Guid Id { get; init; }
    public string Label { get; init; } = "";
    public override string ToString() => Label;
}
