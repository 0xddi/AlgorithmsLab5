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

public partial class ShortestPathView : UserControl
{
    private readonly PlaybackEngine _engine;
    private ShortestPathResult? _result;
    private bool _ready;
    private bool _refreshingSelectors;

    public ShortestPathView()
    {
        InitializeComponent();

        _engine = new PlaybackEngine(ApplyStep);
        Playback.Attach(_engine);

        Toolbar.Attach(GraphCanvas, GraphValueKind.Weight, showDirected: true);
        Inspector.Attach(GraphCanvas, GraphValueKind.Weight);
        GraphCanvas.EdgeLabelKind = EdgeLabelKind.Weight;

        GraphCanvas.GraphEdited += OnGraphEdited;
        GraphCanvas.StatusMessage += message => CanvasHint.Text = message;

        Toolbar.SampleRequested += (_, _) => LoadSample();
        Toolbar.ClearRequested += (_, _) => ClearGraph();
        BuildPathButton.Click += (_, _) => BuildSteps();
        StartBox.SelectionChanged += (_, _) => AutoRebuild();
        TargetBox.SelectionChanged += (_, _) => AutoRebuild();
        SaveGraphButton.Click += SaveGraph_Click;
        LoadGraphButton.Click += LoadGraph_Click;

        LoadSample();
        _ready = true;
    }

    private void AutoRebuild()
    {
        if (_ready && !_refreshingSelectors) BuildSteps();
    }

    private void LoadSample()
    {
        SetGraph(Samples.ShortestPathGraph(), autoBuild: false,
            "Загружена дорожная сеть. Начало A и конец H выбраны; кратчайший маршрут имеет длину 18.");
        BuildSteps();
    }

    private void ClearGraph()
    {
        bool directed = GraphCanvas.Model?.Directed == true;
        SetGraph(new GraphModel { Directed = directed }, autoBuild: false,
            "Граф очищен. Добавьте вершины и рёбра, затем выберите начало и конец маршрута.");
    }

    private void SetGraph(GraphModel model, bool autoBuild, string message)
    {
        GraphCanvas.Model = model;
        Toolbar.RefreshModelState();
        GraphCanvas.ClearVisual();
        _result = null;
        _engine.Clear();
        DistanceItems.ItemsSource = null;
        TableInfo.Text = "нет данных";
        RefreshSelectors();
        ResultText.Text = "Выберите вершины и постройте маршрут.";
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
            var previousStart = (StartBox.SelectedItem as NodeOption)?.Id;
            var previousTarget = (TargetBox.SelectedItem as NodeOption)?.Id;
            var options = model?.Nodes.Select(n => new NodeOption { Id = n.Id, Label = n.Label }).ToList()
                          ?? new List<NodeOption>();
            StartBox.ItemsSource = options;
            TargetBox.ItemsSource = options.ToList();

            var start = options.FirstOrDefault(x => x.Id == previousStart)
                        ?? options.FirstOrDefault(x => string.Equals(x.Label, "A", StringComparison.OrdinalIgnoreCase))
                        ?? options.FirstOrDefault();
            var target = options.FirstOrDefault(x => x.Id == previousTarget)
                         ?? options.LastOrDefault(x => string.Equals(x.Label, "H", StringComparison.OrdinalIgnoreCase))
                         ?? options.LastOrDefault();
            if (start is not null && target is not null && start.Id == target.Id && options.Count > 1)
                target = options.Last(x => x.Id != start.Id);
            StartBox.SelectedItem = start;
            TargetBox.SelectedItem = target;
        }
        finally
        {
            _refreshingSelectors = false;
        }
    }

    private void BuildSteps()
    {
        var model = GraphCanvas.Model;
        if (model is null || model.Nodes.Count < 2)
        {
            _engine.Clear();
            GraphCanvas.ClearVisual();
            DistanceItems.ItemsSource = null;
            ResultText.Text = "Нужно минимум две вершины.";
            Journal.SetMessage("Добавьте как минимум две вершины и соедините их рёбрами.");
            return;
        }
        if (StartBox.SelectedItem is not NodeOption start || TargetBox.SelectedItem is not NodeOption target)
        {
            Journal.SetMessage("Выберите начальную и конечную вершины.");
            return;
        }
        if (start.Id == target.Id)
        {
            _engine.Clear();
            GraphCanvas.ClearVisual();
            ShowNotice("Начальная и конечная вершины должны различаться.", true);
            Journal.SetMessage("✕ Начальная и конечная вершины совпадают.");
            return;
        }
        if (model.Edges.Any(e => e.Weight < 0))
        {
            _engine.Clear();
            GraphCanvas.ClearVisual();
            ShowNotice("Дейкстра не работает с отрицательными весами. Исправьте веса рёбер.", true);
            Journal.SetMessage("✕ Найдены рёбра с отрицательным весом. Алгоритм Дейкстры требует неотрицательных весов.");
            return;
        }

        _result = ShortestPathAlgorithms.Build(model, start.Id, target.Id);
        if (_result.Steps.Count == 0)
        {
            _engine.Clear();
            Journal.SetMessage("Не удалось построить шаги алгоритма.");
            return;
        }

        _engine.Load(_result.Steps);
        CanvasHint.Text = "Нажмите «Пуск», чтобы увидеть выбор вершин, релаксацию рёбер и восстановление пути.";
        NoticeText.IsVisible = false;
    }

    private void ApplyStep(int index)
    {
        var step = _engine.Steps[index];
        GraphCanvas.SetVisual(step.Visual);
        DistanceItems.ItemsSource = step.Table;
        int fixedCount = step.Table.Count(row => row.D == "зафиксирована");
        TableInfo.Text = step.Table.Count == 0 ? "нет данных" : $"{fixedCount} / {step.Table.Count} зафиксировано";

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
        DistanceItems.ItemsSource = null;
        TableInfo.Text = "нет данных";
        RefreshSelectors();
        ResultText.Text = "Изменения в графе сбросили найденный путь.";
        CanvasHint.Text = message + " · Постройте маршрут заново.";
        Journal.SetMessage($"✎ {message}. Постройте кратчайший путь заново.");
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
            bool saved = await GraphIO.SaveGraphAsync(this, model, "Сохранить граф", "shortest-path-graph.json");
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

    private void ShowNotice(string message, bool error = false)
    {
        NoticeText.Text = message;
        NoticeText.Foreground = new SolidColorBrush(Color.Parse(error ? "#F87171" : "#34D399"));
        NoticeText.IsVisible = true;
    }
}
