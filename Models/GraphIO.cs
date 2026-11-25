using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GraphLab.Models;

namespace GraphLab.Models;

public sealed class GraphFileDto
{
    public int Version { get; set; } = 1;
    public string Kind { get; set; } = "graph";
    public bool Directed { get; set; }
    public List<NodeDto> Nodes { get; set; } = new();
    public List<EdgeDto> Edges { get; set; } = new();
}

public sealed class NodeDto
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class EdgeDto
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public double Weight { get; set; } = 1;
    public double Capacity { get; set; } = 1;
}

public static class GraphIO
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(GraphModel model, string kind = "graph")
    {
        var dto = new GraphFileDto
        {
            Kind = kind,
            Directed = model.Directed,
            Nodes = model.Nodes.Select(n => new NodeDto
            {
                Id = n.Id.ToString(),
                Label = n.Label,
                X = Math.Round(n.X, 2),
                Y = Math.Round(n.Y, 2)
            }).ToList(),
            Edges = model.Edges.Select(e => new EdgeDto
            {
                Id = e.Id.ToString(),
                From = e.From.ToString(),
                To = e.To.ToString(),
                Weight = Math.Round(e.Weight, 4),
                Capacity = Math.Round(e.Capacity, 4)
            }).ToList()
        };
        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    public static GraphModel Deserialize(string json)
    {
        GraphFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<GraphFileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Файл не является корректным JSON: " + ex.Message);
        }

        if (dto is null) throw new InvalidDataException("Пустой файл графа.");
        if (dto.Nodes is null) throw new InvalidDataException("В файле отсутствует список вершин.");
        if (dto.Nodes.Count > 500) throw new InvalidDataException("Слишком много вершин (максимум 500).");

        var model = new GraphModel { Directed = dto.Directed };
        var ids = new HashSet<Guid>();
        foreach (var nodeDto in dto.Nodes)
        {
            if (!Guid.TryParse(nodeDto.Id, out var id) || !ids.Add(id))
                id = Guid.NewGuid();
            string label = string.IsNullOrWhiteSpace(nodeDto.Label)
                ? model.NextLabel()
                : nodeDto.Label.Trim();
            model.Nodes.Add(new GraphNode
            {
                Id = id,
                Label = label,
                X = double.IsFinite(nodeDto.X) ? GraphModel.ClampX(nodeDto.X) : 0,
                Y = double.IsFinite(nodeDto.Y) ? GraphModel.ClampY(nodeDto.Y) : 0
            });
        }

        foreach (var edgeDto in dto.Edges ?? new List<EdgeDto>())
        {
            if (!Guid.TryParse(edgeDto.From, out var from) || !Guid.TryParse(edgeDto.To, out var to))
                continue;
            if (from == to || model.FindNode(from) is null || model.FindNode(to) is null)
                continue;

            double weight = double.IsFinite(edgeDto.Weight) && edgeDto.Weight > 0 ? edgeDto.Weight : 1;
            double capacity = double.IsFinite(edgeDto.Capacity) && edgeDto.Capacity > 0 ? edgeDto.Capacity : weight;
            Guid.TryParse(edgeDto.Id, out var edgeId);
            if (edgeId == Guid.Empty) edgeId = Guid.NewGuid();
            if (model.Edges.Any(e => e.Id == edgeId)) edgeId = Guid.NewGuid();

            model.Edges.Add(new GraphEdge
            {
                Id = edgeId,
                From = from,
                To = to,
                Weight = weight,
                Capacity = capacity
            });
        }

        if (model.Nodes.Count > 1 && model.Nodes.All(n => n.X == 0 && n.Y == 0))
            GraphLayout.Circle(model);

        return model;
    }

    public static GraphModel BuildSubgraph(GraphModel source, IReadOnlyCollection<Guid> edgeIds, string kind = "mst")
    {
        var model = new GraphModel { Directed = source.Directed };
        foreach (var node in source.Nodes)
            model.Nodes.Add(new GraphNode { Id = node.Id, Label = node.Label, X = node.X, Y = node.Y });
        foreach (var edge in source.Edges.Where(e => edgeIds.Contains(e.Id)))
        {
            model.Edges.Add(new GraphEdge
            {
                Id = edge.Id,
                From = edge.From,
                To = edge.To,
                Weight = edge.Weight,
                Capacity = edge.Capacity
            });
        }
        return model;
    }

    public static string BuildAdjacencyCsv(GraphModel model)
    {
        var sb = new StringBuilder();
        sb.Append(',');
        sb.AppendJoin(',', model.Nodes.Select(n => EscapeCsv(n.Label)));
        sb.AppendLine();
        foreach (var row in model.Nodes)
        {
            sb.Append(EscapeCsv(row.Label));
            foreach (var col in model.Nodes)
            {
                double value = 0;
                foreach (var edge in model.Edges)
                {
                    if (edge.From == row.Id && edge.To == col.Id)
                    {
                        value = edge.Weight;
                        break;
                    }
                    if (!model.Directed && edge.From == col.Id && edge.To == row.Id)
                    {
                        value = edge.Weight;
                        break;
                    }
                }
                sb.Append(',');
                if (value != 0) sb.Append(value.ToString("0.##", CultureInfo.InvariantCulture));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static GraphModel ParseAdjacencyCsv(string text, bool directed, GraphModel? existing)
    {
        var rawRows = text
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        if (rawRows.Count == 0) throw new InvalidDataException("Матрица пуста.");

        var rows = rawRows.Select(SplitCsv).ToList();
        int width = rows.Max(r => r.Count);
        if (width < 1) throw new InvalidDataException("Не удалось прочитать строки матрицы.");

        bool firstRowHasNumbers = rows[0].Any(cell => !string.IsNullOrWhiteSpace(cell) && TryNumber(cell, out _));
        List<string> labels = new();
        int dataStart = 0;

        if (!firstRowHasNumbers)
        {
            labels = rows[0].Select(x => x.Trim()).ToList();
            if (labels.Count > 0 && labels[0] == "") labels.RemoveAt(0);
            dataStart = 1;
        }
        else if (rows[0].Count > 0 && !TryNumber(rows[0][0], out _) && rows[0][0].Trim() != "")
        {
            labels = rows[0].Select(x => x.Trim()).ToList();
            dataStart = 1;
        }

        var dataRows = rows.Skip(dataStart).ToList();
        if (dataRows.Count == 0) throw new InvalidDataException("В матрице нет строк с данными.");

        var rowLabels = dataRows.Select(r => r.Count > 0 ? r[0].Trim() : "").ToList();
        bool hasRowLabels = rowLabels.Any(x => !TryNumber(x, out _));
        if (labels.Count == 0 && hasRowLabels)
            labels = rowLabels.Where(x => x.Length > 0).ToList();
        if (labels.Count == 0)
            labels = Enumerable.Range(0, dataRows.Count)
                .Select(i => i < 26 ? ((char)('A' + i)).ToString() : "V" + (i + 1))
                .ToList();

        if (labels.Count < dataRows.Count)
            throw new InvalidDataException($"В заголовке {labels.Count} вершин, а строк данных — {dataRows.Count}.");
        if (labels.Count > 100) throw new InvalidDataException("Слишком большая матрица (максимум 100 вершин).");

        int n = labels.Count;
        var matrix = new double[n, n];
        for (int i = 0; i < dataRows.Count; i++)
        {
            var row = dataRows[i];
            int offset = hasRowLabels ? 1 : 0;
            for (int j = 0; j < n; j++)
            {
                if (j + offset >= row.Count) continue;
                string cell = row[j + offset];
                if (string.IsNullOrWhiteSpace(cell)) continue;
                if (!TryNumber(cell, out double value))
                    throw new InvalidDataException($"Нечисловое значение «{cell}» в строке {i + dataStart + 1}, столбце {j + 1}.");
                if (value < 0) throw new InvalidDataException("Веса рёбер не могут быть отрицательными.");
                matrix[i, j] = value;
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (matrix[i, i] != 0)
                throw new InvalidDataException("Петли (ненулевая диагональ) не поддерживаются редактором.");
        }

        if (!directed)
        {
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (Math.Abs(matrix[i, j] - matrix[j, i]) > 1e-6)
                        throw new InvalidDataException($"Матрица несимметрична: ячейки [{labels[i]},{labels[j]}] и [{labels[j]},{labels[i]}] различаются. Для неориентированного графа матрица должна быть симметричной.");
                }
            }
        }

        var model = new GraphModel { Directed = directed };
        var existingByLabel = existing?.Nodes
            .GroupBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase)
            ?? new Dictionary<string, GraphNode>(StringComparer.CurrentCultureIgnoreCase);

        for (int i = 0; i < n; i++)
        {
            string label = string.IsNullOrWhiteSpace(labels[i]) ? model.NextLabel() : labels[i].Trim();
            double x = GraphModel.WorldWidth / 2;
            double y = GraphModel.WorldHeight / 2;
            if (existingByLabel.TryGetValue(label, out var existingNode))
            {
                x = existingNode.X;
                y = existingNode.Y;
            }
            model.AddNode(label, x, y);
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = directed ? 0 : i + 1; j < n; j++)
            {
                if (i == j) continue;
                double value = matrix[i, j];
                if (value <= 0) continue;
                model.AddEdge(model.Nodes[i].Id, model.Nodes[j].Id, value);
            }
        }

        if (existing is null || existing.Nodes.Count != n)
            GraphLayout.Circle(model);
        else if (model.Nodes.All(x => x.X == GraphModel.WorldWidth / 2 && x.Y == GraphModel.WorldHeight / 2))
            GraphLayout.Circle(model);

        return model;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',' || c == ';')
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    private static bool TryNumber(string text, out double value)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            value = 0;
            return true;
        }
        return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static async Task<bool> SaveGraphAsync(Visual owner, GraphModel model, string title, string suggestedName, string kind = "graph")
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top is null) return false;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("GraphLab JSON") { Patterns = new[] { "*.json" } }
            }
        });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(Serialize(model, kind));
        return true;
    }

    public static async Task<GraphModel?> LoadGraphAsync(Visual owner, string title)
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("GraphLab JSON") { Patterns = new[] { "*.json" } }
            }
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        string json = await reader.ReadToEndAsync();
        return Deserialize(json);
    }

    public static async Task<bool> SaveTextAsync(Visual owner, string title, string suggestedName, string extension, string text)
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top is null) return false;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = new[] { "*." + extension } }
            }
        });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
        return true;
    }

    public static async Task<string?> LoadTextAsync(Visual owner, string title, string extension)
    {
        var top = TopLevel.GetTopLevel(owner);
        if (top is null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = new[] { "*." + extension } }
            }
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
