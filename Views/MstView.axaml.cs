using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using GraphLab.Algorithms;
using GraphLab.Controls;
using GraphLab.Models;
using GraphLab.Playback;

namespace GraphLab.Views;

public partial class MstView : UserControl
{
    private readonly PlaybackEngine _engine;
    private SpanningTreeResult? _result;
    private bool _ready;
    private bool _refreshingSelectors;

    public MstView()
    {
        InitializeComponent();

        _engine = new PlaybackEngine(ApplyStep);
        Playback.Attach(_engine);

        Toolbar.Attach(GraphCanvas, GraphValueKind.Weight, showDirected: false);
        Inspector.Attach(GraphCanvas, GraphValueKind.Weight);
        GraphCanvas.EdgeLabelKind = EdgeLabelKind.Weight;

        GraphCanvas.GraphEdited += OnGraphEdited;
        GraphCanvas.StatusMessage += message => CanvasHint.Text = message;

        Toolbar.SampleRequested += (_, _) => LoadSample();
        Toolbar.ClearRequested += (_, _) => ClearGraph();
        BuildTreeButton.Click += (_, _) => BuildSteps();
        AlgorithmBox.SelectionChanged += (_, _) => AutoRebuild();
        StartBox.SelectionChanged += (_, _) => AutoRebuild();
        MaximumCheckBox.Click += (_, _) => AutoRebuild();

        SaveGraphButton.Click += SaveGraph_Click;
        LoadGraphButton.Click += LoadGraph_Click;
        SaveTreeButton.Click += SaveTree_Click;
        LoadTreeButton.Click += LoadTree_Click;

        LoadSample();
        _ready = true;
    }

    private void AutoRebuild()
    {
        if (_ready && !_refreshingSelectors) BuildSteps();
    }

    private void LoadSample()
    {
        SetGraph(Samples.MstGraph(), autoBuild: false,
            "Загружен демонстрационный граф из 7 вершин и 11 рёбер. Минимальное дерево имеет вес 39.");
        BuildSteps();
    }

    private void ClearGraph()
    {
        SetGraph(new GraphModel { Directed = false }, autoBuild: false,
            "Граф очищен. Добавьте вершины и взвешенные рёбра.");
    }

    private void SetGraph(GraphModel model, bool autoBuild, string message)
    {
        model.Directed = false;
        GraphCanvas.Model = model;
        Toolbar.RefreshModelState();
        GraphCanvas.ClearVisual();
        _result = null;
        _engine.Clear();
        TreeItems.ItemsSource = null;
        TreeCountText.Text = "0 рёбер";
        RefreshSelectors();
        ResultText.Text = "Постройте дерево, чтобы увидеть суммарный вес.";
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
            var options = model?.Nodes.Select(n => new NodeOption { Id = n.Id, Label = n.Label }).ToList()
                          ?? new List<NodeOption>();
            StartBox.ItemsSource = options;
            StartBox.SelectedItem = options.FirstOrDefault(x => x.Id == previous)
                                    ?? options.FirstOrDefault(x => string.Equals(x.Label, "A", StringComparison.OrdinalIgnoreCase))
                                    ?? options.FirstOrDefault();
        }
        finally
        {
            _refreshingSelectors = false;
        }
    }

    private void BuildSteps()
    {
        var model = GraphCanvas.Model;
        if (model is null || model.Nodes.Count == 0)
        {
            _engine.Clear();
            GraphCanvas.ClearVisual();
            TreeItems.ItemsSource = null;
            ResultText.Text = "Добавьте вершины и рёбра, чтобы построить дерево.";
            Journal.SetMessage("Граф пуст: добавьте вершины и рёбра.");
            return;
        }

        Guid? startId = (StartBox.SelectedItem as NodeOption)?.Id;
        if (startId is null || model.FindNode(startId.Value) is null)
        {
            startId = model.Nodes[0].Id;
            _refreshingSelectors = true;
            var options = model.Nodes.Select(n => new NodeOption { Id = n.Id, Label = n.Label }).ToList();
            StartBox.ItemsSource = options;
            StartBox.SelectedItem = options[0];
            _refreshingSelectors = false;
        }

        var kind = AlgorithmBox.SelectedIndex == 1 ? SpanningTreeKind.Prim : SpanningTreeKind.Kruskal;
        bool maximum = MaximumCheckBox.IsChecked == true;
        _result = SpanningTreeAlgorithms.Build(model, kind, startId, maximum);
        if (_result.Steps.Count == 0)
        {
            Journal.SetMessage("Не удалось построить шаги алгоритма.");
            return;
        }

        _engine.Load(_result.Steps);
        CanvasHint.Text = "Нажмите «Пуск», чтобы наблюдать добавление рёбер и проверку циклов.";
        NoticeText.IsVisible = false;
    }

    private void ApplyStep(int index)
    {
        var step = _engine.Steps[index];
        GraphCanvas.SetVisual(step.Visual);
        TreeItems.ItemsSource = step.Table;
        TreeCountText.Text = step.Table.Count == 1 ? "1 ребро" : $"{step.Table.Count} рёбер";

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
        TreeItems.ItemsSource = null;
        TreeCountText.Text = "0 рёбер";
        RefreshSelectors();
        ResultText.Text = "Изменения в графе сбросили предыдущее дерево.";
        CanvasHint.Text = message + " · Постройте дерево заново.";
        Journal.SetMessage($"✎ {message}. Постройте остовное дерево заново, чтобы увидеть актуальные шаги.");
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
            bool saved = await GraphIO.SaveGraphAsync(this, model, "Сохранить граф", "mst-graph.json");
            if (saved) ShowNotice("Граф сохранён.");
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

    private async void SaveTree_Click(object? sender, RoutedEventArgs e)
    {
        var model = GraphCanvas.Model;
        if (model is null || _result is null || _result.TreeEdges.Count == 0)
        {
            ShowNotice("Сначала постройте остовное дерево.", true);
            return;
        }
        try
        {
            var tree = GraphIO.BuildSubgraph(model, _result.TreeEdges, "mst");
            bool saved = await GraphIO.SaveGraphAsync(this, tree, "Сохранить остовное дерево", "spanning-tree.json", "mst");
            if (saved) ShowNotice("Остовное дерево сохранено как отдельный граф.");
        }
        catch (Exception ex)
        {
            ShowNotice("Ошибка сохранения дерева: " + ex.Message, true);
        }
    }

    private async void LoadTree_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var model = await GraphIO.LoadGraphAsync(this, "Загрузить остовное дерево");
            if (model is null) return;
            SetGraph(model, autoBuild: true,
                $"Дерево загружено как граф: {model.Nodes.Count} вершин, {model.Edges.Count} рёбер.");
            ShowNotice("Остовное дерево загружено и показано на холсте.");
        }
        catch (Exception ex)
        {
            ShowNotice("Ошибка загрузки дерева: " + ex.Message, true);
        }
    }

    private void ShowNotice(string message, bool error = false)
    {
        NoticeText.Text = message;
        NoticeText.Foreground = new SolidColorBrush(Color.Parse(error ? "#F87171" : "#34D399"));
        NoticeText.IsVisible = true;
    }
}
