using System;
using System.Collections.Generic;
using System.Linq;
using GraphLab.Models;
using GraphLab.Playback;

namespace GraphLab.Algorithms;

public sealed class ShortestPathResult
{
    public List<AlgorithmStep> Steps { get; } = new();
    public List<Guid> PathNodes { get; } = new();
    public HashSet<Guid> PathEdges { get; } = new();
    public double Distance { get; set; } = double.PositiveInfinity;
    public bool Found { get; set; }
}

public static class ShortestPathAlgorithms
{
    public static ShortestPathResult Build(GraphModel graph, Guid startId, Guid targetId)
    {
        var result = new ShortestPathResult();
        var start = graph.FindNode(startId);
        var target = graph.FindNode(targetId);
        if (start is null || target is null) return result;
        if (graph.Edges.Any(e => e.Weight < 0))
            throw new InvalidOperationException("Алгоритм Дейкстры требует неотрицательных весов рёбер.");

        int n = graph.Nodes.Count;
        var dist = graph.Nodes.ToDictionary(node => node.Id, _ => double.PositiveInfinity);
        var prev = new Dictionary<Guid, (Guid Node, GraphEdge Edge)>();
        var finalized = new HashSet<Guid>();
        dist[start.Id] = 0;
        var tags = new Dictionary<Guid, string>
        {
            [start.Id] = "S",
            [target.Id] = "T"
        };

        string Label(Guid id) => graph.FindNode(id)?.Label ?? "?";
        string DistText(double d) => double.IsPositiveInfinity(d) ? "∞" : d.ToString("0.##");

        Dictionary<Guid, string> Badges()
        {
            var badges = new Dictionary<Guid, string>();
            foreach (var node in graph.Nodes)
            {
                double d = dist[node.Id];
                badges[node.Id] = finalized.Contains(node.Id)
                    ? $"✓{DistText(d)}"
                    : DistText(d);
            }
            return badges;
        }

        HashSet<Guid> PrevEdges()
        {
            var set = new HashSet<Guid>();
            foreach (var value in prev.Values) set.Add(value.Edge.Id);
            return set;
        }

        List<TableRow> Table()
        {
            return graph.Nodes
                .OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select(node =>
                {
                    double d = dist[node.Id];
                    string status = finalized.Contains(node.Id)
                        ? "зафиксирована"
                        : double.IsPositiveInfinity(d) ? "недостижима" : "в очереди";
                    return new TableRow
                    {
                        A = node.Label,
                        B = DistText(d),
                        C = prev.TryGetValue(node.Id, out var p) ? Label(p.Node) : "—",
                        D = status
                    };
                }).ToList();
        }

        result.Steps.Add(new AlgorithmStep
        {
            Title = "Инициализация Дейкстры",
            Description = $"Ищем кратчайший путь из {start.Label} в {target.Label}. Расстояние до старта равно 0, до остальных вершин — бесконечности. На каждом шаге выбираем необработанную вершину с минимальной текущей оценкой и ослабляем её рёбра.",
            Hint = $"d({start.Label}) = 0",
            Metrics = $"Кратчайшее расстояние: пока не найдено",
            Visual = new StepVisual
            {
                ActiveNodes = new HashSet<Guid> { start.Id },
                FrontierNodes = new HashSet<Guid> { start.Id },
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = Badges()
            },
            Table = Table()
        });

        bool found = false;
        while (finalized.Count < n)
        {
            Guid? selected = null;
            double best = double.PositiveInfinity;
            foreach (var node in graph.Nodes)
            {
                if (finalized.Contains(node.Id)) continue;
                double d = dist[node.Id];
                if (double.IsPositiveInfinity(d)) continue;
                if (d < best - 1e-9 || (Math.Abs(d - best) < 1e-9 &&
                    (selected is null || string.Compare(node.Label, Label(selected.Value), StringComparison.CurrentCultureIgnoreCase) < 0)))
                {
                    best = d;
                    selected = node.Id;
                }
            }

            if (selected is null) break;
            var u = selected.Value;
            var uNode = graph.FindNode(u)!;

            finalized.Add(u);
            var selectVisual = new StepVisual
            {
                ActiveNodes = new HashSet<Guid> { u },
                VisitedNodes = new HashSet<Guid>(finalized),
                FrontierNodes = new HashSet<Guid>(graph.Nodes
                    .Where(node => !finalized.Contains(node.Id) && !double.IsPositiveInfinity(dist[node.Id]))
                    .Select(node => node.Id)),
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = Badges()
            };

            bool isTarget = u == target.Id;
            result.Steps.Add(new AlgorithmStep
            {
                Title = isTarget ? $"Цель {uNode.Label} достигнута" : $"Выбрана вершина {uNode.Label}",
                Description = isTarget
                    ? $"Вершина {uNode.Label} имеет минимальную оценку d = {dist[u]:0.##} среди всех незафиксированных. Так как все веса неотрицательны, короче путь до неё уже не станет — цель достигнута."
                    : $"Среди незафиксированных вершин минимальную оценку имеет {uNode.Label}: d = {dist[u]:0.##}. Фиксируем это расстояние и ослабляем все исходящие из неё рёбра.",
                Hint = isTarget ? "Путь найден, восстанавливаем маршрут" : $"Текущая вершина: {uNode.Label}",
                Metrics = isTarget ? $"d({uNode.Label}) = {dist[u]:0.##}" : $"Текущая вершина: {uNode.Label}",
                Visual = selectVisual,
                Table = Table()
            });

            if (isTarget)
            {
                found = true;
                break;
            }

            var neighbors = graph.Neighbors(u)
                .Select(x => (x.Neighbor, x.Edge))
                .OrderBy(x => x.Neighbor.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var (v, edge) in neighbors)
            {
                if (finalized.Contains(v.Id)) continue;
                double old = dist[v.Id];
                double candidate = dist[u] + edge.Weight;
                bool improved = candidate < old - 1e-9;
                if (improved)
                {
                    dist[v.Id] = candidate;
                    prev[v.Id] = (u, edge);
                }

                var visual = new StepVisual
                {
                    ActiveNodes = new HashSet<Guid> { u },
                    VisitedNodes = new HashSet<Guid>(finalized),
                    FrontierNodes = new HashSet<Guid>(graph.Nodes
                        .Where(node => !finalized.Contains(node.Id) && !double.IsPositiveInfinity(dist[node.Id]))
                        .Select(node => node.Id)),
                    ActiveEdges = new HashSet<Guid> { edge.Id },
                    AcceptedEdges = PrevEdges(),
                    RejectedEdges = improved ? new HashSet<Guid>() : new HashSet<Guid> { edge.Id },
                    NodeTags = new Dictionary<Guid, string>(tags),
                    NodeBadges = Badges()
                };
                if (improved) visual.AcceptedEdges.Add(edge.Id);

                result.Steps.Add(new AlgorithmStep
                {
                    Title = improved ? $"Улучшение d({v.Label})" : $"Без изменений для {v.Label}",
                    Description = improved
                        ? $"Ребро {uNode.Label}—{v.Label} (вес {edge.Weight:0.##}): d({uNode.Label}) + {edge.Weight:0.##} = {candidate:0.##} < {DistText(old)}. Обновляем расстояние до {v.Label} и запоминаем предыдущую вершину {uNode.Label}."
                        : $"Ребро {uNode.Label}—{v.Label} (вес {edge.Weight:0.##}): новый вариант {candidate:0.##} не меньше уже известного {DistText(old)}. Расстояние не меняется.",
                    Hint = improved ? $"d({v.Label}) = {dist[v.Id]:0.##}" : $"d({v.Label}) остаётся {DistText(dist[v.Id])}",
                    Metrics = $"Текущая вершина: {uNode.Label} · обработано {finalized.Count} / {n}",
                    Visual = visual,
                    Table = Table()
                });
            }
        }

        if (found)
        {
            var path = new List<Guid>();
            var pathEdges = new HashSet<Guid>();
            var cur = target.Id;
            path.Add(cur);
            while (cur != start.Id)
            {
                var (prevNode, edge) = prev[cur];
                pathEdges.Add(edge.Id);
                path.Add(prevNode);
                cur = prevNode;
            }
            path.Reverse();

            result.Found = true;
            result.Distance = dist[target.Id];
            result.PathNodes.AddRange(path);
            result.PathEdges.UnionWith(pathEdges);

            var pathVisual = new StepVisual
            {
                PathNodes = new HashSet<Guid>(path),
                PathEdges = new HashSet<Guid>(pathEdges),
                VisitedNodes = new HashSet<Guid>(finalized),
                NodeTags = new Dictionary<Guid, string>(tags),
                NodeBadges = Badges()
            };
            string pathText = string.Join(" → ", path.Select(Label));
            result.Steps.Add(new AlgorithmStep
            {
                Title = "Кратчайший путь найден",
                Description = $"Восстанавливаем путь по массиву предыдущих вершин: {pathText}. Суммарная длина = {result.Distance:0.##}. Маршрут выделен бирюзовым, по его рёбрам бегут светящиеся частицы.",
                Hint = pathText,
                Metrics = $"Длина кратчайшего пути: {result.Distance:0.##}",
                Visual = pathVisual,
                Table = Table()
            });
        }
        else
        {
            var reachable = graph.Nodes
                .Where(node => !double.IsPositiveInfinity(dist[node.Id]))
                .Select(node => node.Id)
                .ToList();
            result.Steps.Add(new AlgorithmStep
            {
                Title = "Путь не существует",
                Description = $"Очередь пуста, но вершина {target.Label} так и не была достигнута. Из {start.Label} достижимы вершины: {string.Join(", ", reachable.Select(Label))}. Между стартом и целью нет пути.",
                Hint = "Путь не существует",
                Metrics = "Кратчайший путь не найден",
                Visual = new StepVisual
                {
                    VisitedNodes = new HashSet<Guid>(reachable),
                    NodeTags = new Dictionary<Guid, string>(tags),
                    NodeBadges = Badges()
                },
                Table = Table()
            });
        }

        return result;
    }
}
