using System;
using System.Collections.Generic;
using System.Linq;
using GraphLab.Models;
using GraphLab.Playback;

namespace GraphLab.Algorithms;

public sealed class MaxFlowResult
{
    public List<AlgorithmStep> Steps { get; } = new();
    public double MaxFlow { get; set; }
    public HashSet<Guid> CutEdges { get; } = new();
    public double CutCapacity { get; set; }
}

public static class MaxFlowAlgorithms
{
    private const double Epsilon = 1e-9;

    private sealed record ResidualArc(GraphEdge Edge, Guid To, double Residual, bool Forward);

    public static MaxFlowResult Build(GraphModel graph, Guid sourceId, Guid sinkId)
    {
        var result = new MaxFlowResult();
        var source = graph.FindNode(sourceId);
        var sink = graph.FindNode(sinkId);
        if (source is null || sink is null || source.Id == sink.Id) return result;

        graph.ResetFlows();
        var tags = new Dictionary<Guid, string>
        {
            [source.Id] = "S",
            [sink.Id] = "T"
        };
        var labels = new Dictionary<Guid, string>();
        foreach (var e in graph.Edges)
            labels[e.Id] = $"{e.Flow:0.##}/{e.Capacity:0.##}";

        StepVisual Base()
        {
            var v = new StepVisual { NodeTags = new Dictionary<Guid, string>(tags) };
            foreach (var e in graph.Edges) v.EdgeLabels[e.Id] = $"{e.Flow:0.##}/{e.Capacity:0.##}";
            return v;
        }

        string Label(Guid id) => graph.FindNode(id)?.Label ?? "?";
        string QueueText(IEnumerable<Guid> ids) => string.Join(", ", ids.Select(Label));

        result.Steps.Add(new AlgorithmStep
        {
            Title = "Инициализация",
            Description = $"Поток в транспортной сети равен нулю. Ищем максимальный поток из {source.Label} в {sink.Label} алгоритмом Эдмондса–Карпа: на каждом шаге в остаточной сети ищется кратчайший дополняющий путь (BFS), затем поток вдоль него увеличивается на минимальный остаток.",
            Hint = "Готовы искать дополняющий путь",
            Metrics = "Макс. поток: 0",
            Visual = Base()
        });

        while (true)
        {
            var level = new Dictionary<Guid, int> { [source.Id] = 0 };
            var parent = new Dictionary<Guid, (Guid Prev, ResidualArc Arc)>();
            var queue = new Queue<Guid>();
            queue.Enqueue(source.Id);
            var treeEdges = new HashSet<Guid>();
            var treeBackEdges = new HashSet<Guid>();

            var startVisual = Base();
            startVisual.ActiveNodes.Add(source.Id);
            startVisual.FrontierNodes.Add(source.Id);
            startVisual.NodeBadges[source.Id] = "d=0";
            result.Steps.Add(new AlgorithmStep
            {
                Title = "BFS в остаточной сети",
                Description = $"Строим остаточную сеть: у каждого ребра остаток прямой дуги равен cap−flow, а обратная дуга позволяет уменьшить уже отправленный поток. Запускаем поиск из {source.Label}.",
                Hint = $"Очередь: {source.Label}",
                Metrics = $"Макс. поток: {result.MaxFlow:0.##}",
                Visual = startVisual
            });

            bool found = false;
            while (queue.Count > 0 && !found)
            {
                var u = queue.Dequeue();
                var uVisual = Base();
                uVisual.ActiveNodes.Add(u);
                uVisual.VisitedNodes.UnionWith(level.Keys);
                uVisual.FrontierNodes.UnionWith(queue);
                uVisual.AcceptedEdges.UnionWith(treeEdges);
                uVisual.BackEdges.UnionWith(treeBackEdges);
                foreach (var (id, d) in level)
                {
                    uVisual.NodeBadges[id] = $"d={d}";
                }

                var arcs = ResidualArcs(graph, u);
                result.Steps.Add(new AlgorithmStep
                {
                    Title = $"Извлекаем {Label(u)}",
                    Description = $"Вершина {Label(u)} извлечена из очереди. Рассматриваем все дуги остаточной сети с положительным остатком, выходящие из неё.",
                    Hint = queue.Count == 0 ? "Очередь пуста" : "Очередь: " + QueueText(queue),
                    Metrics = $"Макс. поток: {result.MaxFlow:0.##}",
                    Visual = uVisual
                });

                foreach (var arc in arcs)
                {
                    if (level.ContainsKey(arc.To)) continue;
                    level[arc.To] = level[u] + 1;
                    parent[arc.To] = (u, arc);
                    queue.Enqueue(arc.To);
                    if (arc.Forward) treeEdges.Add(arc.Edge.Id);
                    else treeBackEdges.Add(arc.Edge.Id);

                    var v = Base();
                    v.ActiveNodes.Add(u);
                    v.VisitedNodes.UnionWith(level.Keys);
                    v.FrontierNodes.UnionWith(queue);
                    v.ActiveEdges.Add(arc.Edge.Id);
                    if (arc.Forward) v.AcceptedEdges.Add(arc.Edge.Id);
                    else v.BackEdges.Add(arc.Edge.Id);
                    v.AcceptedEdges.UnionWith(treeEdges);
                    v.BackEdges.UnionWith(treeBackEdges);
                    foreach (var (id, d) in level) v.NodeBadges[id] = $"d={d}";

                    string kind = arc.Forward ? "прямая" : "обратная";
                    string ends = arc.Forward
                        ? $"{Label(u)} → {Label(arc.To)}"
                        : $"{Label(u)} → {Label(arc.To)} (по ребру {Label(arc.Edge.To)} → {Label(arc.Edge.From)})";
                    result.Steps.Add(new AlgorithmStep
                    {
                        Title = $"Дуга {ends}",
                        Description = $"Проверяем {kind} дугу {ends}. Остаток = {arc.Residual:0.##} > 0, вершина {Label(arc.To)} ещё не достигнута — добавляем её в очередь на уровень d={level[arc.To]}.",
                        Hint = "Очередь: " + QueueText(queue),
                        Metrics = $"Макс. поток: {result.MaxFlow:0.##}",
                        Visual = v
                    });

                    if (arc.To == sink.Id)
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                var reachable = new HashSet<Guid>(level.Keys);
                var cut = graph.Edges
                    .Where(e => reachable.Contains(e.From) && !reachable.Contains(e.To))
                    .ToList();
                result.CutEdges.Clear();
                foreach (var e in cut) result.CutEdges.Add(e.Id);
                result.CutCapacity = cut.Sum(e => e.Capacity);

                var v = Base();
                v.VisitedNodes.UnionWith(reachable);
                v.RejectedEdges.UnionWith(cut.Select(e => e.Id));
                foreach (var (id, d) in level) v.NodeBadges[id] = $"d={d}";

                result.Steps.Add(new AlgorithmStep
                {
                    Title = "Дополняющий путь не найден",
                    Description = $"Вершина {sink.Label} недостижима в остаточной сети. Множество вершин, достижимых из {source.Label}, образует минимальный разрез (S-сторона): {string.Join(", ", reachable.Select(Label))}. Пропускная способность разреза = {result.CutCapacity:0.##}. По теореме о максимальном потоке и минимальном разрезе это и есть максимальный поток.",
                    Hint = "Алгоритм завершён",
                    Metrics = $"Макс. поток: {result.MaxFlow:0.##} · разрез: {result.CutCapacity:0.##}",
                    Visual = v
                });
                break;
            }

            // Reconstruct the augmenting path.
            var arcsPath = new List<ResidualArc>();
            var nodesPath = new List<Guid> { sink.Id };
            var cur = sink.Id;
            while (cur != source.Id)
            {
                var (prev, arc) = parent[cur];
                arcsPath.Add(arc);
                nodesPath.Add(prev);
                cur = prev;
            }
            arcsPath.Reverse();
            nodesPath.Reverse();

            var pathVisual = Base();
            pathVisual.PathNodes.UnionWith(nodesPath);
            pathVisual.PathEdges.UnionWith(arcsPath.Select(a => a.Edge.Id));
            pathVisual.BackEdges.UnionWith(arcsPath.Where(a => !a.Forward).Select(a => a.Edge.Id));
            pathVisual.NodeBadges[source.Id] = "S";
            pathVisual.NodeBadges[sink.Id] = "T";

            var pathText = string.Join(" → ", nodesPath.Select(Label));
            result.Steps.Add(new AlgorithmStep
            {
                Title = "Дополняющий путь найден",
                Description = $"BFS нашёл путь {pathText}. Он состоит из {arcsPath.Count} дуг остаточной сети; это значит, что поток вдоль прямых дуг можно увеличить, а вдоль обратных — уменьшить.",
                Hint = pathText,
                Metrics = $"Макс. поток: {result.MaxFlow:0.##}",
                Visual = pathVisual
            });

            double delta = arcsPath.Min(a => a.Residual);
            var detail = string.Join("; ", arcsPath.Select(a =>
                $"{Label(a.Edge.From)}→{Label(a.Edge.To)}: {(a.Forward ? "прямая" : "обратная")}, остаток {a.Residual:0.##}"));

            var bottleneckVisual = Base();
            bottleneckVisual.PathNodes.UnionWith(nodesPath);
            bottleneckVisual.PathEdges.UnionWith(arcsPath.Select(a => a.Edge.Id));
            bottleneckVisual.BackEdges.UnionWith(arcsPath.Where(a => !a.Forward).Select(a => a.Edge.Id));
            bottleneckVisual.ActiveNodes.UnionWith(nodesPath);
            result.Steps.Add(new AlgorithmStep
            {
                Title = $"Узкое место: δ = {delta:0.##}",
                Description = $"Остатки дуг на пути: {detail}. Минимальный остаток определяет, насколько можно увеличить поток: δ = {delta:0.##}.",
                Hint = $"Увеличиваем поток на {delta:0.##}",
                Metrics = $"Макс. поток: {result.MaxFlow:0.##}",
                Visual = bottleneckVisual
            });

            var changes = new List<string>();
            foreach (var arc in arcsPath)
            {
                double oldFlow = arc.Edge.Flow;
                if (arc.Forward) arc.Edge.Flow += delta;
                else arc.Edge.Flow -= delta;
                arc.Edge.Flow = Math.Max(0, Math.Min(arc.Edge.Capacity, arc.Edge.Flow));
                changes.Add($"{Label(arc.Edge.From)}→{Label(arc.Edge.To)}: {oldFlow:0.##} → {arc.Edge.Flow:0.##}");
            }
            result.MaxFlow += delta;

            var augmentVisual = Base();
            augmentVisual.PathNodes.UnionWith(nodesPath);
            augmentVisual.PathEdges.UnionWith(arcsPath.Select(a => a.Edge.Id));
            augmentVisual.AcceptedEdges.UnionWith(arcsPath.Where(a => a.Forward).Select(a => a.Edge.Id));
            augmentVisual.BackEdges.UnionWith(arcsPath.Where(a => !a.Forward).Select(a => a.Edge.Id));

            result.Steps.Add(new AlgorithmStep
            {
                Title = $"Поток увеличен на {delta:0.##}",
                Description = $"Обновляем поток вдоль пути: {string.Join("; ", changes)}. Суммарный поток из {source.Label} в {sink.Label} теперь равен {result.MaxFlow:0.##}. Возвращаемся к поиску следующего дополняющего пути.",
                Hint = "Ищем следующий дополняющий путь",
                Metrics = $"Макс. поток: {result.MaxFlow:0.##}",
                Visual = augmentVisual
            });
        }

        return result;
    }

    private static List<ResidualArc> ResidualArcs(GraphModel graph, Guid u)
    {
        var arcs = new List<ResidualArc>();
        foreach (var edge in graph.Edges)
        {
            if (edge.From == u)
            {
                double residual = edge.Capacity - edge.Flow;
                if (residual > Epsilon) arcs.Add(new ResidualArc(edge, edge.To, residual, true));
            }
            if (edge.To == u)
            {
                double residual = edge.Flow;
                if (residual > Epsilon) arcs.Add(new ResidualArc(edge, edge.From, residual, false));
            }
        }

        arcs.Sort((a, b) =>
        {
            var la = graph.FindNode(a.To)?.Label ?? "";
            var lb = graph.FindNode(b.To)?.Label ?? "";
            int c = string.Compare(la, lb, StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : a.Residual.CompareTo(b.Residual);
        });
        return arcs;
    }
}
