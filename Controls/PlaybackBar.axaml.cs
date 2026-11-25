using System;
using Avalonia.Controls;
using GraphLab.Playback;

namespace GraphLab.Controls;

public partial class PlaybackBar : UserControl
{
    private PlaybackEngine? _engine;
    private bool _sync;

    public PlaybackBar()
    {
        InitializeComponent();

        ResetButton.Click += (_, _) => _engine?.Reset();
        BackButton.Click += (_, _) => _engine?.Previous();
        ForwardButton.Click += (_, _) => _engine?.Next();
        PlayButton.Click += (_, _) => _engine?.TogglePlay();

        ProgressSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property != Slider.ValueProperty || _sync || _engine is null) return;
            _engine.GoTo((int)Math.Round(ProgressSlider.Value));
        };

        SpeedBox.SelectionChanged += (_, _) =>
        {
            if (_engine is null) return;
            _engine.Speed = SpeedBox.SelectedIndex switch
            {
                0 => 0.5,
                2 => 1.5,
                3 => 2.0,
                _ => 1.0
            };
        };
    }

    public PlaybackEngine? Engine => _engine;

    public void Attach(PlaybackEngine engine)
    {
        if (_engine is not null) _engine.Changed -= Refresh;
        _engine = engine;
        _engine.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null)
        {
            IsEnabled = false;
            return;
        }

        var steps = _engine.Steps;
        bool hasSteps = steps.Count > 0;
        int index = _engine.Index;

        _sync = true;
        try
        {
            ResetButton.IsEnabled = hasSteps && index > 0;
            BackButton.IsEnabled = hasSteps && index > 0;
            ForwardButton.IsEnabled = hasSteps && index < steps.Count - 1;
            PlayButton.IsEnabled = hasSteps;
            PlayButton.Content = _engine.IsPlaying ? "⏸ Пауза" : "▶ Пуск";
            ProgressSlider.IsEnabled = hasSteps && steps.Count > 1;
            ProgressSlider.Maximum = Math.Max(0, steps.Count - 1);
            ProgressSlider.Value = Math.Clamp(index, 0, Math.Max(0, steps.Count - 1));
            PositionText.Text = hasSteps ? $"{Math.Max(1, index + 1)} / {steps.Count}" : "— / —";
            StepTitle.Text = _engine.Current?.Title ?? "Алгоритм ещё не запущен";
        }
        finally
        {
            _sync = false;
        }
    }
}
