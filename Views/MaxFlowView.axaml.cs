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

public partial class MaxFlowView : UserControl
{
    private readonly PlaybackEngine _engine;
    private MaxFlowResult? _result;
    private bool _ready;
    private bool _refreshingSelectors;

    public MaxFlowView()
    {
        InitializeComponent();

        _engine = new PlaybackEngine(ApplyStep);
        Playback.Attach(_engine);

        Toolbar.Attach(GraphCanvas, GraphValueKind.Capacity, showDirected: false);
        Inspector.Attach(GraphCanvas, GraphValueKind.Capacity);
        GraphCanvas.EdgeLabelKind = EdgeLabelKind.CapacityFlow;

        GraphCanvas.GraphEdited += OnGraphEdited;
        GraphCanvas.StatusMessage += message => CanvasHint.Text = message;

        Toolbar.SampleRequested += (_, _) => LoadSample();
        Toolbar.ClearRequested += (_, _) => ClearGraph();
        BuildFlowButton.Click += (_, _) => BuildSteps();
        SourceBox.SelectionChanged += (_, _) => AutoRebuild();
        SinkBox.SelectionChanged += (_, _) => AutoRebuild();
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
        SetGraph(Samples.TransportNetwork(), autoBuild: false,
            "Загружена классическая транспортная сеть. Источник S и сток T уже выбраны; нажмите «Пуск».");
        BuildSteps();
    }

    private void ClearGraph()
    {
        SetGraph(new GraphModel { Directed = true }, autoBuild: false,
            "Сеть очищена. Добавляйте вершины и направленные дуги, затем задайте источник и сток.");
    }

    private void SetGraph(GraphModel model, bool autoBuild, string message)
    {
        model.Directed = true;
        foreach (var edge in model.Edges) edge.Flow = 0;
        GraphCanvas.Model = model;
        Toolbar.RefreshModelState();
        GraphCanvas.ClearVisual();
        _result = null;
        _engine.Clear();
        RefreshSelectors();
        ResultText.Text = "Постройте поток, чтобы увидеть значение.";
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
            var previousSource = (SourceBox.SelectedItem as NodeOption)?.Id;
            var previousSink = (SinkBox.SelectedItem as NodeOption)?.Id;
            var options = model?.Nodes.Select(n => new NodeOption { Id = n.Id, Label = n.Label }).ToList()
                          ?? new List<NodeOption>();

            SourceBox.ItemsSource = options;
            SinkBox.ItemsSource = options.ToList();

            var source = options.FirstOrDefault(x => x.Id == previousSource)
                         ?? options.FirstOrDefault(x => string.Equals(x.Label, "S", StringComparison.OrdinalIgnoreCase))
                         ?? options.FirstOrDefault();
            var sink = options.FirstOrDefault(x => x.Id == previousSink)
                       ?? options.LastOrDefault(x => string.Equals(x.Label, "T", StringComparison.OrdinalIgnoreCase))
                       ?? options.LastOrDefault();
            if (source is not null && sink is not null && source.Id == sink.Id && options.Count > 1)
                sink = options.First(x => x.Id != source.Id);

            SourceBox.SelectedItem = source;
            SinkBox.SelectedItem = sink;
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
            ResultText.Text = "Нужно минимум две вершины: источник и сток.";
            Journal.SetMessage("Добавьте как минимум две вершины и соедините их дугами.");
            return;
        }
        if (SourceBox.SelectedItem is not NodeOption source || SinkBox.SelectedItem is not NodeOption sink)
        {
            Journal.SetMessage("Выберите источник и сток.");
            return;
        }
        if (source.Id == sink.Id)
        {
            _engine.Clear();
            GraphCanvas.ClearVisual();
            ShowNotice("Источник и сток должны быть разными вершинами.", true);
            Journal.SetMessage("✕ Источник и сток совпадают. Выберите разные вершины.");
            return;
        }

        model.ResetFlows();
        _result = MaxFlowAlgorithms.Build(model, source.Id, sink.Id);
        if (_result.Steps.Count == 0)
        {
            _engine.Clear();
            Journal.SetMessage("Не удалось построить шаги алгоритма.");
            return;
        }

        _engine.Load(_result.Steps);
        CanvasHint.Text = "Нажмите «Пуск», чтобы увидеть поиск дополняющих путей и рост потока.";
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
        var model = GraphCanvas.Model;
        if (model is not null)
        {
            foreach (var edge in model.Edges) edge.Flow = 0;
            model.NotifyChanged();
        }
        _result = null;
        _engine.Clear();
        GraphCanvas.ClearVisual();
        RefreshSelectors();
        ResultText.Text = "Изменения в сети сбросили предыдущий поток.";
        CanvasHint.Text = message + " · Постройте поток заново.";
        Journal.SetMessage($"✎ {message}. Сбросьте/постройте поток заново, чтобы увидеть актуальные шаги.");
        NoticeText.IsVisible = false;
    }

    private async void SaveGraph_Click(object? sender, RoutedEventArgs e)
    {
        var model = GraphCanvas.Model;
        if (model is null || model.Nodes.Count == 0)
        {
            ShowNotice("Нечего сохранять: сеть пуста.", true);
            return;
        }
        try
        {
            bool saved = await GraphIO.SaveGraphAsync(this, model, "Сохранить транспортную сеть", "transport-network.json");
            if (saved) ShowNotice("Транспортная сеть сохранена.");
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
            var model = await GraphIO.LoadGraphAsync(this, "Загрузить транспортную сеть");
            if (model is null) return;
            SetGraph(model, autoBuild: true, $"Сеть загружена: {model.Nodes.Count} вершин, {model.Edges.Count} дуг.");
            ShowNotice("Сеть успешно загружена.");
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
