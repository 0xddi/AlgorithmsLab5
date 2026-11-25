using System;
using System.Collections.Generic;
using System.Linq;
using GraphLab.Models;
using GraphLab.Playback;

namespace GraphLab.Algorithms;

public enum SpanningTreeKind
{
    Kruskal,
    Prim
}

public sealed class SpanningTreeResult
{
    public List<AlgorithmStep> Steps { get; } = new();
    public HashSet<Guid> TreeEdges { get; } = new();
    public double TotalWeight { get; set; }
    public int Components { get; set; }
    public bool IsSpanningTree => Components <= 1 && TreeEdges.Count > 0;
}

public static class SpanningTreeAlgorithms
{
    public static SpanningTreeResult Build(GraphModel graph, SpanningTreeKind kind, Guid? startId, bool maximum)
    {
        var result = kind == SpanningTreeKind.Kruskal
            ? BuildKruskal(graph, maximum, result: new SpanningTreeResult())
            : BuildPrim(graph, startId, maximum, new SpanningTreeResult());
        return result;
    }

    private static string Label(GraphModel graph, Guid id) => graph.FindNode(id)?.Label ?? "?";

    private static string EdgeName(GraphModel graph, GraphEdge edge) =>
        $"{Label(graph, edge.From)}—{Label(graph, edge.To)}";

    private static List<TableRow> TreeTable(GraphModel graph, IEnumerable<GraphEdge> edges, double total)
    {
        return edges.Select(e => new TableRow
        {
            A = EdgeName(graph, e),
            B = e.Weight.ToString("0.##"),
            C = $"Σ {total:0.##}",
            D = "✓"
        }).ToList();
    }

    private static SpanningTreeResult BuildKruskal(GraphModel graph, bool maximum, SpanningTreeResult result)
    {
        if (graph.Nodes.Count == 0) return result;
        int n = graph.Nodes.Count;
        var edges = graph.Edges
            .OrderBy(e => maximum ? -e.Weight : e.Weight)
            .ThenBy(e => Label(graph, e.From))
            .ThenBy(e => Label(graph, e.To))
            .ToList();

        var parent = new Dictionary<Guid, Guid>();
        var rank = new Dictionary<Guid, int>();
        foreach (var node in graph.Nodes)
        {
            parent[node.Id] = node.Id;
            rank[node.Id] = 0;
        }

        Guid Find(Guid x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union(Guid a, Guid b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra == rb) return;
            if (rank[ra] < rank[rb]) (ra, rb) = (rb, ra);
            parent[rb] = ra;
            if (rank[ra] == rank[rb]) rank[ra]++;
        }

        Dictionary<Guid, string> ComponentBadges()
        {
            var map = new Dictionary<Guid, int>();
            var badges = new Dictionary<Guid, string>();
            foreach (var node in graph.Nodes)
            {
                var root = Find(node.Id);
                if (!map.TryGetValue(root, out var index))
                {
                    index = map.Count + 1;
                    map[root] = index;
                }
                badges[node.Id] = $"C{index}";
            }
            return badges;
        }

        var treeEdges = new List<GraphEdge>();
        double total = 0;
        var badges = ComponentBadges();

        result.Steps.Add(new AlgorithmStep
        {
            Title = "Инициализация Краскала",
            Description = $"Сортируем все рёбра по весу {(maximum ? "по убыванию (максимальное дерево)" : "по возрастанию (минимальное дерево)")}. Каждая вершина сначала образует отдельную компоненту. Далее будем брать рёбра, соединяющие разные компоненты; ребро внутри компоненты отбрасываем, чтобы не создать цикл.",
            Hint = $"Рёбер к рассмотрению: {edges.Count}",
            Metrics = $"Суммарный вес: 0 · рёбер: 0 / {Math.Max(0, n - 1)}",
            Visual = new StepVisual
            {
                NodeBadges = new Dictionary<Guid, string>(badges)
            }
        });

        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            var a = graph.FindNode(edge.From);
            var b = graph.FindNode(edge.To);
            if (a is null || b is null) continue;

            var beforeBadges = ComponentBadges();
            string componentA = beforeBadges[a.Id];
            string componentB = beforeBadges[b.Id];
            var ra = Find(a.Id);
            var rb = Find(b.Id);
            bool connects = ra != rb;
            if (connects && treeEdges.Count < n - 1)
            {
                Union(a.Id, b.Id);
                treeEdges.Add(edge);
                total += edge.Weight;
                badges = ComponentBadges();
                result.TreeEdges.Add(edge.Id);

                var visual = new StepVisual
                {
                    AcceptedEdges = new HashSet<Guid>(result.TreeEdges),
                    ActiveEdges = new HashSet<Guid> { edge.Id },
                    NodeBadges = new Dictionary<Guid, string>(badges)
                };
                result.Steps.Add(new AlgorithmStep
                {
                    Title = $"Добавлено ребро {EdgeName(graph, edge)}",
                    Description = $"Рассматриваем ребро {EdgeName(graph, edge)} с весом {edge.Weight:0.##}. Вершины находятся в разных компонентах {componentA} и {componentB}, поэтому ребро не создаёт цикл. Включаем его в дерево и объединяем компоненты. Суммарный вес: {total:0.##}.",
                    Hint = $"В дереве {treeEdges.Count} из {n - 1} рёбер",
                    Metrics = $"Суммарный вес: {total:0.##} · рёбер: {treeEdges.Count} / {n - 1}",
                    Visual = visual,
                    Table = TreeTable(graph, treeEdges, total)
                });
            }
            else
            {
                result.Steps.Add(new AlgorithmStep
                {
                    Title = $"Пропущено ребро {EdgeName(graph, edge)}",
                    Description = $"Ребро {EdgeName(graph, edge)} с весом {edge.Weight:0.##} соединяет вершины одной компоненты {componentA}, поэтому образует цикл. По правилу Краскала такое ребро пропускается.",
                    Hint = $"В дереве {treeEdges.Count} из {n - 1} рёбер",
                    Metrics = $"Суммарный вес: {total:0.##} · рёбер: {treeEdges.Count} / {n - 1}",
                    Visual = new StepVisual
                    {
                        AcceptedEdges = new HashSet<Guid>(result.TreeEdges),
                        RejectedEdges = new HashSet<Guid> { edge.Id },
                        NodeBadges = new Dictionary<Guid, string>(badges)
                    },
                    Table = TreeTable(graph, treeEdges, total)
                });
            }

            if (treeEdges.Count == n - 1)
            {
                break;
            }
        }

        result.TotalWeight = total;
        result.Components = CountComponents(graph);
        var finalVisual = new StepVisual
        {
            AcceptedEdges = new HashSet<Guid>(result.TreeEdges),
            NodeBadges = ComponentBadges()
        };
        string finalText;
        if (result.IsSpanningTree)
        {
            finalText = $"Остовное дерево построено: {treeEdges.Count} рёбер, суммарный вес {total:0.##}. Все {n} вершин связаны, циклов нет.";
        }
        else if (result.Components > 1)
        {
            finalText = $"Граф несвязный: получен остовный лес из {treeEdges.Count} рёбер и {result.Components} компонент связности, суммарный вес {total:0.##}.";
        }
        else
        {
            finalText = "В графе нет рёбер, остовное дерево построить невозможно.";
        }

        result.Steps.Add(new AlgorithmStep
        {
            Title = "Краскал завершён",
            Description = finalText + " Выбранные рёбра выделены зелёным.",
            Hint = "Готово",
            Metrics = $"Суммарный вес: {total:0.##} · рёбер: {treeEdges.Count} · компонент: {result.Components}",
            Visual = finalVisual,
            Table = TreeTable(graph, treeEdges, total)
        });
        return result;
    }

    private static SpanningTreeResult BuildPrim(GraphModel graph, Guid? startId, bool maximum, SpanningTreeResult result)
    {
        if (graph.Nodes.Count == 0) return result;
        int n = graph.Nodes.Count;
        var start = (startId.HasValue ? graph.FindNode(startId.Value) : null) ?? graph.Nodes[0];
        var visited = new HashSet<Guid> { start.Id };
        var treeEdges = new List<GraphEdge>();
        var badges = new Dictionary<Guid, string> { [start.Id] = "#1" };
        var tags = new Dictionary<Guid, string> { [start.Id] = "S" };
        double total = 0;
        int order = 1;

        result.Steps.Add(new AlgorithmStep
        {
            Title = "Инициализация Прима",
            Description = $"Начинаем из вершины {start.Label}. Текущее дерево содержит только её. На каждом шаге рассматриваем все рёбра, идущие из дерева наружу (разрез), и выбираем {(maximum ? "максимальное" : "минимальное")} по весу. Так дерево постепенно «прорастает» по графу.",
            Hint = $"Дерево: {start.Label}",
            Metrics = $"Суммарный вес: 0 · вершин: 1 / {n}",
            Visual = new StepVisual
            {
                ActiveNodes = new HashSet<Guid> { start.Id },
                VisitedNodes = new HashSet<Guid>(visited),
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = new Dictionary<Guid, string>(badges)
            }
        });

        while (visited.Count < n)
        {
            var candidates = new List<GraphEdge>();
            foreach (var edge in graph.Edges)
            {
                bool fromIn = visited.Contains(edge.From);
                bool toIn = visited.Contains(edge.To);
                if (fromIn == toIn) continue;
                candidates.Add(edge);
            }

            if (candidates.Count == 0) break;

            var chosen = maximum
                ? candidates.OrderByDescending(e => e.Weight).ThenBy(e => Label(graph, e.From)).First()
                : candidates.OrderBy(e => e.Weight).ThenBy(e => Label(graph, e.From)).First();

            var outside = visited.Contains(chosen.From) ? chosen.To : chosen.From;
            var outsideNode = graph.FindNode(outside)!;
            visited.Add(outside);
            order++;
            badges[outside] = $"#{order}";
            treeEdges.Add(chosen);
            total += chosen.Weight;
            result.TreeEdges.Add(chosen.Id);

            string candidateText = string.Join(", ", candidates
                .OrderBy(e => e.Weight)
                .Select(e => $"{EdgeName(graph, e)} ({e.Weight:0.##})"));

            var visual = new StepVisual
            {
                ActiveNodes = new HashSet<Guid> { outside },
                VisitedNodes = new HashSet<Guid>(visited),
                ActiveEdges = new HashSet<Guid> { chosen.Id },
                AcceptedEdges = new HashSet<Guid>(result.TreeEdges),
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = new Dictionary<Guid, string>(badges)
            };

            result.Steps.Add(new AlgorithmStep
            {
                Title = $"Добавлена вершина {outsideNode.Label}",
                Description = $"Рёбра разреза: {candidateText}. Выбираем {(maximum ? "максимальное" : "минимальное")} ребро {EdgeName(graph, chosen)} весом {chosen.Weight:0.##}; вершина {outsideNode.Label} подключается к дереву. Суммарный вес: {total:0.##}.",
                Hint = $"Дерево: {string.Join(", ", visited.Select(id => Label(graph, id)))}",
                Metrics = $"Суммарный вес: {total:0.##} · вершин: {visited.Count} / {n}",
                Visual = visual,
                Table = TreeTable(graph, treeEdges, total)
            });
        }

        result.TotalWeight = total;
        result.Components = CountComponents(graph);
        bool spanning = visited.Count == n;
        var finalVisual = new StepVisual
        {
            VisitedNodes = new HashSet<Guid>(visited),
            AcceptedEdges = new HashSet<Guid>(result.TreeEdges),
            NodeTags = new Dictionary<Guid, string>(tags),
            NodeBadges = new Dictionary<Guid, string>(badges)
        };
        result.Steps.Add(new AlgorithmStep
        {
            Title = "Прим завершён",
            Description = spanning
                ? $"Построено остовное дерево из {treeEdges.Count} рёбер. Суммарный вес: {total:0.##}."
                : $"Граф несвязный: из {start.Label} достижимо только {visited.Count} из {n} вершин. Получен остовный лес, суммарный вес {total:0.##}.",
            Hint = "Готово",
            Metrics = $"Суммарный вес: {total:0.##} · рёбер: {treeEdges.Count} · компонент: {result.Components}",
            Visual = finalVisual,
            Table = TreeTable(graph, treeEdges, total)
        });
        return result;
    }

    private static int CountComponents(GraphModel graph)
    {
        var seen = new HashSet<Guid>();
        int components = 0;
        foreach (var node in graph.Nodes)
        {
            if (seen.Contains(node.Id)) continue;
            components++;
            var stack = new Stack<Guid>();
            stack.Push(node.Id);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!seen.Add(id)) continue;
                foreach (var (other, _, _) in graph.Neighbors(id)) stack.Push(other.Id);
            }
        }
        return components;
    }
}
