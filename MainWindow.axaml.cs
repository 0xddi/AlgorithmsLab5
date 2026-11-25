using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GraphLab.Controls;

namespace GraphLab;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.Handled) return;
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or NumericUpDown or ComboBox or Button)
            return;
        if (MainTabs.SelectedItem is not TabItem { Content: Control content }) return;
        var playback = content.GetVisualDescendants().OfType<PlaybackBar>().FirstOrDefault();
        if (playback?.Engine is { Steps.Count: > 0 } engine)
        {
            engine.TogglePlay();
            e.Handled = true;
        }
    }
}
