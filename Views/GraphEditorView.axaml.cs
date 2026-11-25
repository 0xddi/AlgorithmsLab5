using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using GraphLab.Algorithms;
using GraphLab.Controls;
using GraphLab.Models;
using GraphLab.Playback;

namespace GraphLab.Views;

public partial class GraphEditorView : UserControl
{
    private readonly PlaybackEngine _engine;
    private TraversalResult? _result;
    private bool _ready;
    private bool _refreshingSelectors;

    public GraphEditorView()
    {
        InitializeComponent();

        _engine = new PlaybackEngine(ApplyStep);
        Playback.Attach(_engine);

        Toolbar.Attach(GraphCanvas, GraphValueKind.Weight, showDirected: true);
        Inspector.Attach(GraphCanvas, GraphValueKind.Weight);

        GraphCanvas.GraphEdited += OnGraphEdited;
        GraphCanvas.StatusMessage += message => CanvasHint.Text = message;

        Toolbar.SampleRequested += (_, _) => LoadSample();
        Toolbar.ClearRequested += (_, _) => ClearGraph();

        BuildButton.Click += (_, _) => BuildSteps();
        AlgorithmBox.SelectionChanged += (_, _) => AutoRebuild();
        NeighborOrderBox.SelectionChanged += (_, _) => AutoRebuild();
        StartBox.SelectionChanged += (_, _) => AutoRebuild();

        SaveGraphButton.Click += SaveGraph_Click;
        LoadGraphButton.Click += LoadGraph_Click;
        ApplyMatrixButton.Click += (_, _) => ApplyMatrix();
        ImportCsvButton.Click += ImportCsv_Click;
        ExportCsvButton.Click += ExportCsv_Click;

        LoadSample();
        _ready = true;
        BuildSteps();
    }

    private void AutoRebuild()
    {
        if (_ready && !_refreshingSelectors) BuildSteps();
    }

    private void LoadSample()
    {
        SetGraph(Samples.TraversalGraph(), autoBuild: false,
            "Загружен демонстрационный граф из 8 вершин. Выберите стартовую вершину и алгоритм, затем запустите демонстрацию.");
        BuildSteps();
    }

    private void ClearGraph()
    {
        bool directed = GraphCanvas.Model?.Directed == true;
        SetGraph(new GraphModel { Directed = directed }, autoBuild: false,
            "Граф очищен. Добавьте вершины и рёбра вручную или нажмите «Пример».");
    }

    private void SetGraph(GraphModel model, bool autoBuild, string message)
    {
        GraphCanvas.Model = model;
        Toolbar.RefreshModelState();
        GraphCanvas.ClearVisual();
        _result = null;
        _engine.Clear();
        RefreshSelectors();
        UpdateMatrix();
        ResultText.Text = "Постройте обход, чтобы увидеть порядок посещения.";
        CanvasHint.Text = message;
        Journal.SetMessage("ℹ " + message);
        NoticeText.IsVisible = false;
        if (autoBuild) BuildSteps();
    }

    private void RefreshSelectors()
    {
        _refreshingSelectors = true;
        try
        {
            var model = GraphCanvas.Model;
            var previous = (StartBox.SelectedItem as NodeOption)?.Id;
            var options = model?.Nodes
                .Select(n => new NodeOption { Id = n.Id, Label = n.Label })
                .ToList() ?? new List<NodeOption>();
            StartBox.ItemsSource = options;
            StartBox.SelectedItem = options.FirstOrDefault(x => x.Id == previous) ?? options.FirstOrDefault();
        }
        finally
        {
            _refreshingSelectors = false;
        }
    }

    private void UpdateMatrix()
    {
        if (GraphCanvas.Model is { } model)
            MatrixBox.Text = GraphIO.BuildAdjacencyCsv(model);
    }

    private void BuildSteps()
    {
        var model = GraphCanvas.Model;
        if (model is null || model.Nodes.Count == 0)
        {
            _engine.Clear();
            GraphCanvas.ClearVisual();
            ResultText.Text = "Добавьте хотя бы одну вершину, чтобы построить обход.";
            Journal.SetMessage("Добавьте вершины и рёбра на холсте или загрузите пример.");
            CanvasHint.Text = "Граф пуст.";
            return;
        }

        if (StartBox.SelectedItem is not NodeOption option || model.FindNode(option.Id) is null)
        {
            var first = model.Nodes[0];
            option = new NodeOption { Id = first.Id, Label = first.Label };
            _refreshingSelectors = true;
            StartBox.ItemsSource = model.Nodes.Select(n => new NodeOption { Id = n.Id, Label = n.Label }).ToList();
            StartBox.SelectedItem = option;
            _refreshingSelectors = false;
        }

        var kind = AlgorithmBox.SelectedIndex == 1 ? TraversalKind.Dfs : TraversalKind.Bfs;
        var order = NeighborOrderBox.SelectedIndex == 1 ? NeighborOrder.ByWeight : NeighborOrder.ByLabel;
        _result = TraversalAlgorithms.Build(model, option.Id, kind, order);
        if (_result.Steps.Count == 0)
        {
            Journal.SetMessage("Не удалось построить обход: проверьте выбранную вершину.");
            return;
        }

        _engine.Load(_result.Steps);
        CanvasHint.Text = "Нажмите «Пуск» для автоматической демонстрации или «▶» для пошагового просмотра.";
        NoticeText.IsVisible = false;
    }

    private void ApplyStep(int index)
    {
        var step = _engine.Steps[index];
        GraphCanvas.SetVisual(step.Visual);

        var entries = new List<string>();
        for (int i = 0; i <= index; i++)
        {
            var current = _engine.Steps[i];
            entries.Add($"{(i == index ? "▶" : "·")} Шаг {i + 1:00} · {current.Title}\n{current.Description}");
        }
        Journal.SetEntries(entries);

        if (!string.IsNullOrWhiteSpace(step.Hint)) CanvasHint.Text = step.Hint;
        if (!string.IsNullOrWhiteSpace(step.Metrics)) ResultText.Text = step.Metrics;
    }

    private void OnGraphEdited(string message)
    {
        _result = null;
        _engine.Clear();
        GraphCanvas.ClearVisual();
        RefreshSelectors();
        UpdateMatrix();
        ResultText.Text = "Изменения в графе сбросили предыдущий обход.";
        CanvasHint.Text = message + " · Постройте обход заново.";
        Journal.SetMessage($"✎ {message}. Постройте обход заново, чтобы увидеть актуальные шаги.");
        NoticeText.IsVisible = false;
    }

    private async void SaveGraph_Click(object? sender, RoutedEventArgs e)
    {
        var model = GraphCanvas.Model;
        if (model is null || model.Nodes.Count == 0)
        {
            ShowNotice("Нечего сохранять: граф пуст.", true);
            return;
        }
        try
        {
            bool saved = await GraphIO.SaveGraphAsync(this, model, "Сохранить граф", "graph.json");
            if (saved) ShowNotice("Граф сохранён в JSON-файл.");
        }
        catch (Exception ex)
        {
            ShowNotice("Ошибка сохранения: " + ex.Message, true);
        }
    }

    private async void LoadGraph_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var model = await GraphIO.LoadGraphAsync(this, "Загрузить граф");
            if (model is null) return;
            SetGraph(model, autoBuild: true, $"Граф загружен: {model.Nodes.Count} вершин, {model.Edges.Count} рёбер.");
            ShowNotice("Граф успешно загружен.");
        }
        catch (Exception ex)
        {
            ShowNotice("Ошибка загрузки: " + ex.Message, true);
        }
    }

    private void ApplyMatrix()
    {
        if (GraphCanvas.Model is null) return;
        try
        {
            var model = GraphIO.ParseAdjacencyCsv(MatrixBox.Text ?? "", GraphCanvas.Model.Directed, GraphCanvas.Model);
            SetGraph(model, autoBuild: true,
                $"Матрица применена: {model.Nodes.Count} вершин, {model.Edges.Count} рёбер.");
            ShowNotice("Матрица успешно применена к графу.");
        }
        catch (Exception ex)
        {
            ShowNotice("Ошибка матрицы: " + ex.Message, true);
            Journal.SetMessage("✕ Не удалось применить матрицу.\n" + ex.Message);
        }
    }

    private async void ImportCsv_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var text = await GraphIO.LoadTextAsync(this, "Импорт матрицы смежности", "csv");
            if (text is null) return;
            MatrixBox.Text = text;
            ApplyMatrix();
        }
        catch (Exception ex)
        {
            ShowNotice("Ошибка импорта CSV: " + ex.Message, true);
        }
    }

    private async void ExportCsv_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            bool saved = await GraphIO.SaveTextAsync(this, "Экспорт матрицы смежности",
                "adjacency.csv", "csv", MatrixBox.Text ?? "");
            if (saved) ShowNotice("Матрица сохранена в CSV-файл.");
        }
        catch (Exception ex)
        {
            ShowNotice("Ошибка экспорта CSV: " + ex.Message, true);
        }
    }

    private void ShowNotice(string message, bool error = false)
    {
        NoticeText.Text = message;
        NoticeText.Foreground = new SolidColorBrush(Color.Parse(error ? "#F87171" : "#34D399"));
        NoticeText.IsVisible = true;
    }
}
