using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GraphLab.Controls;

public partial class JournalPanel : UserControl
{
    public JournalPanel()
    {
        InitializeComponent();
    }

    public void SetEntries(IReadOnlyList<string> entries)
    {
        JournalItems.ItemsSource = entries;
        EntryCount.Text = entries.Count == 1 ? "1 запись" : $"{entries.Count} записей";
        Dispatcher.UIThread.Post(() =>
        {
            if (entries.Count <= 1)
            {
                JournalScroll.Offset = new Vector(0, 0);
            }
            else
            {
                ScrollToEnd();
            }
        }, DispatcherPriority.Background);
    }

    public void SetMessage(string text)
    {
        SetEntries(new[] { text });
    }

    private void ScrollToEnd()
    {
        double max = Math.Max(0, JournalScroll.Extent.Height - JournalScroll.Viewport.Height);
        JournalScroll.Offset = new Vector(0, max);
    }
}
