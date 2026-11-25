using System;
using System.Collections.Generic;
using System.Linq;
using GraphLab.Models;
using GraphLab.Playback;

namespace GraphLab.Algorithms;

public enum TraversalKind
{
    Bfs,
    Dfs
}

public enum NeighborOrder
{
    ByLabel,
    ByWeight
}

public sealed class TraversalResult
{
    public List<AlgorithmStep> Steps { get; } = new();
    public List<Guid> Order { get; } = new();
    public string OrderText { get; set; } = "";
}

public static class TraversalAlgorithms
{
    public static TraversalResult Build(GraphModel graph, Guid startId, TraversalKind kind, NeighborOrder order)
    {
        var result = new TraversalResult();
        var start = graph.FindNode(startId);
        if (start is null || graph.Nodes.Count == 0) return result;

        if (kind == TraversalKind.Bfs) BuildBfs(graph, start, order, result);
        else BuildDfs(graph, start, order, result);

        return result;
    }

    private static List<(GraphNode Node, GraphEdge Edge)> Neighbors(GraphModel graph, Guid id, NeighborOrder order)
    {
        var list = graph.Neighbors(id)
            .Select(x => (x.Neighbor, x.Edge))
            .ToList();

        list.Sort(order == NeighborOrder.ByLabel
            ? (a, b) =>
            {
                int c = string.Compare(a.Neighbor.Label, b.Neighbor.Label, StringComparison.CurrentCultureIgnoreCase);
                return c != 0 ? c : a.Edge.Weight.CompareTo(b.Edge.Weight);
            }
            : (a, b) =>
            {
                int c = a.Edge.Weight.CompareTo(b.Edge.Weight);
                return c != 0 ? c : string.Compare(a.Neighbor.Label, b.Neighbor.Label, StringComparison.CurrentCultureIgnoreCase);
            });

        return list;
    }

    private static string NodeLabel(GraphModel graph, Guid id) => graph.FindNode(id)?.Label ?? "?";

    private static string OrderText(GraphModel graph, IEnumerable<Guid> ids) =>
        string.Join(" → ", ids.Select(id => NodeLabel(graph, id)));

    private static string Tag(GraphModel graph, Guid startId)
    {
        return graph.FindNode(startId)?.Label ?? "S";
    }

    private static void BuildBfs(GraphModel graph, GraphNode start, NeighborOrder order, TraversalResult result)
    {
        int n = graph.Nodes.Count;
        var visited = new HashSet<Guid>();
        var discovered = new HashSet<Guid> { start.Id };
        var queue = new Queue<Guid>();
        var treeEdges = new HashSet<Guid>();
        var badges = new Dictionary<Guid, string>();
        var tags = new Dictionary<Guid, string> { [start.Id] = "S" };
        queue.Enqueue(start.Id);

        result.Steps.Add(new AlgorithmStep
        {
            Title = "Инициализация BFS",
            Description = $"Обход в ширину начинается из вершины {start.Label}. Она помечена как обнаруженная и добавлена в очередь. BFS сначала посещает всех соседей старта, затем соседей их соседей — то есть движется «слоями».",
            Hint = $"Очередь: {start.Label}",
            Metrics = $"Посещено: 0 / {n}",
            Visual = new StepVisual
            {
                FrontierNodes = new HashSet<Guid> { start.Id },
                ActiveNodes = new HashSet<Guid> { start.Id },
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = new Dictionary<Guid, string> { [start.Id] = "d=0" }
            }
        });

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            var uNode = graph.FindNode(u)!;
            visited.Add(u);
            result.Order.Add(u);
            badges[u] = $"#{result.Order.Count}";

            var current = new StepVisual
            {
                ActiveNodes = new HashSet<Guid> { u },
                VisitedNodes = new HashSet<Guid>(visited),
                FrontierNodes = new HashSet<Guid>(queue),
                AcceptedEdges = new HashSet<Guid>(treeEdges),
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = new Dictionary<Guid, string>(badges)
            };
            result.Steps.Add(new AlgorithmStep
            {
                Title = $"Извлекаем {uNode.Label} из очереди",
                Description = $"Вершина {uNode.Label} становится текущей и считается посещённой (порядковый номер {result.Order.Count}). Просматриваем её соседей и добавляем ещё не обнаруженные вершины в конец очереди.",
                Hint = queue.Count == 0 ? "Очередь пуста" : "Очередь: " + string.Join(", ", queue.Select(x => NodeLabel(graph, x))),
                Metrics = $"Посещено: {visited.Count} / {n}",
                Visual = current.Copy()
            });

            foreach (var (v, edge) in Neighbors(graph, u, order))
            {
                if (discovered.Contains(v.Id)) continue;
                discovered.Add(v.Id);
                treeEdges.Add(edge.Id);
                queue.Enqueue(v.Id);

                badges[v.Id] = "в очереди";
                var visual = new StepVisual
                {
                    ActiveNodes = new HashSet<Guid> { u },
                    VisitedNodes = new HashSet<Guid>(visited),
                    FrontierNodes = new HashSet<Guid>(queue),
                    ActiveEdges = new HashSet<Guid> { edge.Id },
                    AcceptedEdges = new HashSet<Guid>(treeEdges),
                    NodeTags = new Dictionary<Guid, string>(tags),
                    NodeBadges = new Dictionary<Guid, string>(badges)
                };
                result.Steps.Add(new AlgorithmStep
                {
                    Title = $"Найдена вершина {v.Label}",
                    Description = $"Из вершины {uNode.Label} по ребру {uNode.Label}—{v.Label} (вес {edge.Weight:0.##}) обнаружена новая вершина {v.Label}. Помещаем её в очередь. Ребро входит в дерево обхода.",
                    Hint = "Очередь: " + string.Join(", ", queue.Select(x => NodeLabel(graph, x))),
                    Metrics = $"Обнаружено: {discovered.Count} / {n}",
                    Visual = visual
                });
            }

            badges[u] = $"#{result.Order.Count}";
        }

        foreach (var node in graph.Nodes)
        {
            if (visited.Contains(node.Id)) continue;
            if (result.Order.Count > 0)
            {
                result.Steps.Add(new AlgorithmStep
                {
                    Title = "Компонента завершена",
                    Description = $"Из вершины {start.Label} достижимы не все вершины. Например, {node.Label} осталась непосещённой — значит, граф несвязный относительно стартовой вершины.",
                    Hint = "Обход завершён",
                    Metrics = $"Посещено: {visited.Count} / {n}",
                    Visual = new StepVisual
                    {
                        VisitedNodes = new HashSet<Guid>(visited),
                        AcceptedEdges = new HashSet<Guid>(treeEdges),
                        NodeTags = new Dictionary<Guid, string>(tags),
                        NodeBadges = new Dictionary<Guid, string>(badges)
                    }
                });
                break;
            }
        }

        result.OrderText = OrderText(graph, result.Order);
        result.Steps.Add(new AlgorithmStep
        {
            Title = "Обход в ширину завершён",
            Description = $"Очередь опустела. Итоговый порядок посещения: {result.OrderText}. Дерево обхода выделено зелёным; веса рёбер показаны на холсте.",
            Hint = "Готово",
            Metrics = $"Порядок: {result.OrderText}",
            Visual = new StepVisual
            {
                VisitedNodes = new HashSet<Guid>(visited),
                AcceptedEdges = new HashSet<Guid>(treeEdges),
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = new Dictionary<Guid, string>(badges)
            }
        });
    }

    private static void BuildDfs(GraphModel graph, GraphNode start, NeighborOrder order, TraversalResult result)
    {
        int n = graph.Nodes.Count;
        var visited = new HashSet<Guid>();
        var discovered = new HashSet<Guid> { start.Id };
        var stack = new Stack<(Guid Node, Guid? ParentEdge)>();
        stack.Push((start.Id, null));
        var treeEdges = new HashSet<Guid>();
        var badges = new Dictionary<Guid, string>();
        var tags = new Dictionary<Guid, string> { [start.Id] = "S" };

        result.Steps.Add(new AlgorithmStep
        {
            Title = "Инициализация DFS",
            Description = $"Обход в глубину начинается из вершины {start.Label}. Она помещена в стек. DFS идёт как можно дальше по первому направлению, и только когда идти некуда — возвращается назад.",
            Hint = $"Стек: {start.Label}",
            Metrics = $"Посещено: 0 / {n}",
            Visual = new StepVisual
            {
                FrontierNodes = new HashSet<Guid> { start.Id },
                ActiveNodes = new HashSet<Guid> { start.Id },
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = new Dictionary<Guid, string> { [start.Id] = "в стеке" }
            }
        });

        while (stack.Count > 0)
        {
            var (u, parentEdge) = stack.Pop();
            if (visited.Contains(u)) continue;

            visited.Add(u);
            result.Order.Add(u);
            badges[u] = $"#{result.Order.Count}";
            if (parentEdge.HasValue) treeEdges.Add(parentEdge.Value);

            var pushed = new List<GraphNode>();
            var neighbors = Neighbors(graph, u, order);
            // Push in reverse so the first neighbour by the selected ordering is on top.
            for (int i = neighbors.Count - 1; i >= 0; i--)
            {
                var (v, edge) = neighbors[i];
                if (visited.Contains(v.Id) || discovered.Contains(v.Id)) continue;
                discovered.Add(v.Id);
                stack.Push((v.Id, edge.Id));
                pushed.Add(v);
                badges[v.Id] = "в стеке";
            }

            var uNode = graph.FindNode(u)!;
            var stackText = stack.Count == 0
                ? "Стек пуст"
                : "Стек (сверху): " + string.Join(", ", stack.Select(x => NodeLabel(graph, x.Node)));

            result.Steps.Add(new AlgorithmStep
            {
                Title = $"Посещаем {uNode.Label}",
                Description = pushed.Count > 0
                    ? $"Вершина {uNode.Label} посещена (номер {result.Order.Count}). В стек добавлены соседи: {string.Join(", ", pushed.Select(x => x.Label))}. Следующей будет обработана вершина на вершине стека."
                    : $"Вершина {uNode.Label} посещена (номер {result.Order.Count}). Новых непосещённых соседей нет — алгоритм выполняет возврат по стеку.",
                Hint = stackText,
                Metrics = $"Посещено: {visited.Count} / {n}",
                Visual = new StepVisual
                {
                    ActiveNodes = new HashSet<Guid> { u },
                    VisitedNodes = new HashSet<Guid>(visited),
                    FrontierNodes = new HashSet<Guid>(stack.Select(x => x.Node)),
                    ActiveEdges = new HashSet<Guid>(parentEdge.HasValue ? new[] { parentEdge.Value } : Array.Empty<Guid>()),
                    AcceptedEdges = new HashSet<Guid>(treeEdges),
                    NodeTags = new Dictionary<Guid, string>(tags),
                    NodeBadges = new Dictionary<Guid, string>(badges)
                }
            });
        }

        var unreachable = graph.Nodes.Where(x => !visited.Contains(x.Id)).ToList();
        if (unreachable.Count > 0)
        {
            result.Steps.Add(new AlgorithmStep
            {
                Title = "Стек пуст, есть недостижимые вершины",
                Description = $"Обход из {start.Label} завершён, но вершины {string.Join(", ", unreachable.Select(x => x.Label))} недостижимы. Это говорит о несвязности графа.",
                Hint = "Обход завершён",
                Metrics = $"Посещено: {visited.Count} / {n}",
                Visual = new StepVisual
                {
                    VisitedNodes = new HashSet<Guid>(visited),
                    AcceptedEdges = new HashSet<Guid>(treeEdges),
                    NodeTags = new Dictionary<Guid, string>(tags),
                    NodeBadges = new Dictionary<Guid, string>(badges)
                }
            });
        }

        result.OrderText = OrderText(graph, result.Order);
        result.Steps.Add(new AlgorithmStep
        {
            Title = "Обход в глубину завершён",
            Description = $"Стек пуст. Итоговый порядок посещения: {result.OrderText}. Рёбра дерева DFS подсвечены зелёным.",
            Hint = "Готово",
            Metrics = $"Порядок: {result.OrderText}",
            Visual = new StepVisual
            {
                VisitedNodes = new HashSet<Guid>(visited),
                AcceptedEdges = new HashSet<Guid>(treeEdges),
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = new Dictionary<Guid, string>(badges)
            }
        });
    }
}
