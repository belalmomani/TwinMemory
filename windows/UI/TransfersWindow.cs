using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TwinMemory;

/// <summary>Transfer history/queue read from the database; "done" appears only after the SHA-256 check passed.</summary>
public class TransfersWindow : Window
{
    readonly ListView _list = new();
    readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Text = S.T("لا توجد عمليات نقل بعد"), Foreground = System.Windows.Media.Brushes.Gray };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public TransfersWindow(Window owner)
    {
        Owner = owner; Title = S.T("قائمة النقل"); Width = 820; Height = 460; FlowDirection = owner.FlowDirection; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var g = new GridView();
        void Col(string h, string path, double w) => g.Columns.Add(new GridViewColumn { Header = S.T(h), Width = w, DisplayMemberBinding = new System.Windows.Data.Binding(path) });
        Col("الاسم", "Title", 240); Col("الحجم", "SizeText", 80); Col("الجهاز", "DeviceName", 110); Col("الحالة", "StateLabel", 230); Col("التاريخ", "DateText", 140);
        _list.View = g;
        var grid = new Grid();
        grid.Children.Add(_list); grid.Children.Add(_empty);
        Content = grid;
        _timer.Tick += (_, _) => Reload();
        Loaded += (_, _) => { Reload(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
    }

    void Reload()
    {
        var l = App.Db.ListTransfers(300);
        _list.ItemsSource = l;
        _empty.Visibility = l.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
