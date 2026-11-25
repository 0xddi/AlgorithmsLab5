using System;
using System.Collections.Generic;
using Avalonia.Threading;

namespace GraphLab.Playback;

public sealed class PlaybackEngine
{
    private readonly DispatcherTimer _timer;
    private readonly Action<int> _apply;
    private IReadOnlyList<AlgorithmStep> _steps = Array.Empty<AlgorithmStep>();
    private double _speed = 1.0;

    public PlaybackEngine(Action<int> apply)
    {
        _apply = apply;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.05)
        };
        _timer.Tick += OnTick;
    }

    public event Action? Changed;

    public IReadOnlyList<AlgorithmStep> Steps => _steps;
    public int Index { get; private set; } = -1;
    public bool IsPlaying { get; private set; }
    public bool HasSteps => _steps.Count > 0;

    public AlgorithmStep? Current =>
        Index >= 0 && Index < _steps.Count ? _steps[Index] : null;

    public double Speed
    {
        get => _speed;
        set
        {
            _speed = Math.Clamp(value, 0.25, 4);
            UpdateInterval();
        }
    }

    public void Load(IReadOnlyList<AlgorithmStep> steps)
    {
        _timer.Stop();
        IsPlaying = false;
        _steps = steps;
        Index = _steps.Count > 0 ? 0 : -1;
        ApplyCurrent();
        Changed?.Invoke();
    }

    public void Clear()
    {
        _timer.Stop();
        IsPlaying = false;
        _steps = Array.Empty<AlgorithmStep>();
        Index = -1;
        Changed?.Invoke();
    }

    public void TogglePlay()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Play()
    {
        if (_steps.Count == 0) return;
        if (Index >= _steps.Count - 1)
        {
            Index = 0;
            ApplyCurrent();
        }
        IsPlaying = true;
        UpdateInterval();
        _timer.Start();
        Changed?.Invoke();
    }

    public void Pause()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        _timer.Stop();
        Changed?.Invoke();
    }

    public void Next()
    {
        if (_steps.Count == 0) return;
        Pause();
        if (Index < _steps.Count - 1)
        {
            Index++;
            ApplyCurrent();
        }
        Changed?.Invoke();
    }

    public void Previous()
    {
        if (_steps.Count == 0) return;
        Pause();
        if (Index > 0)
        {
            Index--;
            ApplyCurrent();
        }
        Changed?.Invoke();
    }

    public void Reset()
    {
        if (_steps.Count == 0) return;
        Pause();
        Index = 0;
        ApplyCurrent();
        Changed?.Invoke();
    }

    public void GoTo(int index)
    {
        if (_steps.Count == 0) return;
        Pause();
        Index = Math.Clamp(index, 0, _steps.Count - 1);
        ApplyCurrent();
        Changed?.Invoke();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (Index < _steps.Count - 1)
        {
            Index++;
            ApplyCurrent();
            Changed?.Invoke();
        }
        else
        {
            IsPlaying = false;
            _timer.Stop();
            Changed?.Invoke();
        }
    }

    private void ApplyCurrent()
    {
        if (Index >= 0 && Index < _steps.Count) _apply(Index);
    }

    private void UpdateInterval()
    {
        _timer.Interval = TimeSpan.FromSeconds(1.05 / _speed);
    }
}
