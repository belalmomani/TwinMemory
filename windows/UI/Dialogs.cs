using System.Windows;
using System.Windows.Controls;
using TwinMemory.Core;

namespace TwinMemory;

public static class Dlg
{
    static Window Make(Window owner, string title) => new()
    {
        Title = title, Owner = owner, SizeToContent = SizeToContent.Height, Width = 440, ResizeMode = ResizeMode.NoResize,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = owner.FlowDirection, ShowInTaskbar = false,
    };

    static StackPanel Buttons(Window w, Action ok, string okText)
    {
        var okB = new Button { Content = okText, IsDefault = true, MinWidth = 84, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
        okB.Click += (_, _) => ok();
        var cancel = new Button { Content = S.T("إلغاء"), IsCancel = true, MinWidth = 84, Padding = new Thickness(8, 4, 8, 4) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(okB); row.Children.Add(cancel);
        return row;
    }

    public static string? Input(Window owner, string label, string initial = "", bool multiline = false)
    {
        var w = Make(owner, "TwinMemory");
        var tb = new TextBox
        {
            Text = initial, Margin = new Thickness(0, 6, 0, 0), AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap,
            MinHeight = multiline ? 110 : 26, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        };
        string? result = null;
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(tb);
        var row = Buttons(w, () => { result = tb.Text; w.DialogResult = true; }, S.T("حفظ"));
        if (multiline) ((Button)row.Children[0]).IsDefault = false;
        panel.Children.Add(row);
        w.Content = panel;
        w.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return w.ShowDialog() == true && !string.IsNullOrWhiteSpace(result) ? result : null;
    }

    public static Choice? Pick(Window owner, string title, IEnumerable<Choice> options)
    {
        var w = Make(owner, title);
        var lb = new ListBox { ItemsSource = options.ToList(), DisplayMemberPath = "Name", MinHeight = 120, MaxHeight = 280, SelectedIndex = 0 };
        Choice? result = null;
        void Accept() { if (lb.SelectedItem is Choice c) { result = c; w.DialogResult = true; } }
        lb.MouseDoubleClick += (_, _) => Accept();
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(lb);
        panel.Children.Add(Buttons(w, Accept, "OK"));
        w.Content = panel;
        return w.ShowDialog() == true ? result : null;
    }

    public static bool Confirm(Window owner, string message) =>
        MessageBox.Show(owner, message, "TwinMemory", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public static void Info(Window owner, string message) => MessageBox.Show(owner, message, "TwinMemory", MessageBoxButton.OK, MessageBoxImage.Information);
}
