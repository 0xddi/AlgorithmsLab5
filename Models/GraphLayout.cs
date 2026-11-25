using System;
using System.Collections.Generic;
using System.Linq;
using GraphLab.Models;

namespace GraphLab.Models;

public enum LayoutKind
{
    Circle,
    Grid,
    Force
}

public static class GraphLayout
{
    public static void Apply(GraphModel model, LayoutKind kind)
    {
        if (model.Nodes.Count == 0) return;
        switch (kind)
        {
            case LayoutKind.Circle:
                Circle(model);
                break;
            case LayoutKind.Grid:
                Grid(model);
                break;
            case LayoutKind.Force:
                Force(model);
                break;
        }
        model.NotifyChanged();
    }

    public static void Circle(GraphModel model)
    {
        int n = model.Nodes.Count;
        if (n == 0) return;
        if (n == 1)
        {
            model.Nodes[0].X = GraphModel.WorldWidth / 2;
            model.Nodes[0].Y = GraphModel.WorldHeight / 2;
            return;
        }

        double cx = GraphModel.WorldWidth / 2;
        double cy = GraphModel.WorldHeight / 2;
        double radius = Math.Min(260, 170 + n * 9);
        for (int i = 0; i < n; i++)
        {
            double angle = -Math.PI / 2 + 2 * Math.PI * i / n;
            model.Nodes[i].X = GraphModel.ClampX(cx + radius * 1.35 * Math.Cos(angle));
            model.Nodes[i].Y = GraphModel.ClampY(cy + radius * 0.88 * Math.Sin(angle));
        }
    }

    public static void Grid(GraphModel model)
    {
        int n = model.Nodes.Count;
        if (n == 0) return;
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n * 1.55)));
        int rows = (int)Math.Ceiling(n / (double)columns);
        double cellW = (GraphModel.WorldWidth - 2 * GraphModel.Margin) / Math.Max(1, columns);
        double cellH = (GraphModel.WorldHeight - 2 * GraphModel.Margin) / Math.Max(1, rows);
        for (int i = 0; i < n; i++)
        {
            int row = i / columns;
            int col = i % columns;
            double x = GraphModel.Margin + cellW * (col + 0.5);
            double y = GraphModel.Margin + cellH * (row + 0.5);
            if (row == rows - 1 && columns > 2)
            {
                int itemsInRow = n - row * columns;
                double shift = (columns - itemsInRow) * cellW / 2;
                x += shift;
            }
            model.Nodes[i].X = GraphModel.ClampX(x);
            model.Nodes[i].Y = GraphModel.ClampY(y);
        }
    }

    public static void Force(GraphModel model, int iterations = 260)
    {
        int n = model.Nodes.Count;
        if (n == 0) return;
        if (n == 1)
        {
            model.Nodes[0].X = GraphModel.WorldWidth / 2;
            model.Nodes[0].Y = GraphModel.WorldHeight / 2;
            return;
        }

        // Keep the current shape as a starting point; if nodes overlap, seed a circle.
        bool degenerate = model.Nodes.Select(x => (x.X, x.Y)).Distinct().Count() < Math.Max(2, n / 2);
        if (degenerate) Circle(model);

        double area = (GraphModel.WorldWidth - 2 * GraphModel.Margin) * (GraphModel.WorldHeight - 2 * GraphModel.Margin);
        double k = Math.Sqrt(area / Math.Max(1, n)) * 0.78;
        double cx = GraphModel.WorldWidth / 2;
        double cy = GraphModel.WorldHeight / 2;
        var random = new Random(17);
        var disp = new Dictionary<Guid, (double X, double Y)>();

        for (int step = 0; step < iterations; step++)
        {
            foreach (var node in model.Nodes) disp[node.Id] = (0, 0);
            double temperature = 34 * (1 - step / (double)iterations) + 1.5;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var a = model.Nodes[i];
                    var b = model.Nodes[j];
                    double dx = a.X - b.X;
                    double dy = a.Y - b.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 1)
                    {
                        dx = random.NextDouble() - 0.5;
                        dy = random.NextDouble() - 0.5;
                        dist = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                    }
                    double force = k * k / dist;
                    double fx = dx / dist * force;
                    double fy = dy / dist * force;
                    var da = disp[a.Id];
                    var db = disp[b.Id];
                    disp[a.Id] = (da.X + fx, da.Y + fy);
                    disp[b.Id] = (db.X - fx, db.Y - fy);
                }
            }

            foreach (var edge in model.Edges)
            {
                var a = model.FindNode(edge.From);
                var b = model.FindNode(edge.To);
                if (a is null || b is null) continue;
                double dx = a.X - b.X;
                double dy = a.Y - b.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                double force = dist * dist / k;
                double fx = dx / dist * force;
                double fy = dy / dist * force;
                var da = disp[a.Id];
                var db = disp[b.Id];
                disp[a.Id] = (da.X - fx, da.Y - fy);
                disp[b.Id] = (db.X + fx, db.Y + fy);
            }

            foreach (var node in model.Nodes)
            {
                var d = disp[node.Id];
                double length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                if (length > 0.01)
                {
                    double limited = Math.Min(length, temperature);
                    node.X += d.X / length * limited;
                    node.Y += d.Y / length * limited;
                }
                // Gravity toward the canvas center keeps disconnected pieces together.
                node.X += (cx - node.X) * 0.004;
                node.Y += (cy - node.Y) * 0.004;
                node.X = GraphModel.ClampX(node.X);
                node.Y = GraphModel.ClampY(node.Y);
            }
        }
    }
}
