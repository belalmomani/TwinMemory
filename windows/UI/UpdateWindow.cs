using System.IO;
using System.Windows;
using System.Windows.Controls;
using TwinMemory.Update;

namespace TwinMemory;

public class UpdateWindow : Window
{
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    readonly TextBox _notes = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100, MaxHeight = 220, Visibility = Visibility.Collapsed };
    readonly ProgressBar _bar = new() { Height = 10, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    readonly Button _check, _apply;
    UpdateInfo? _info;
    bool _busy;

    public UpdateWindow(Window owner)
    {
        Owner = owner; Title = S.T("التحديثات"); Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        FlowDirection = owner.FlowDirection; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;

        _check = new Button { Content = S.T("التحقق من التحديثات…"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        _check.Click += async (_, _) => await CheckAsync();
        _apply = new Button { Content = S.T("التحديث الآن"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        _apply.Click += async (_, _) => await ApplyAsync();
        var close = new Button { Content = S.T("إغلاق"), IsCancel = true, Padding = new Thickness(10, 4, 10, 4) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(_check); row.Children.Add(_apply); row.Children.Add(close);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = S.T("الإصدار الحالي") + ": " + UpdateService.Current.ToString(3), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_status); panel.Children.Add(_notes); panel.Children.Add(_bar); panel.Children.Add(row);
        panel.Children.Add(new TextBlock { Text = S.T("بياناتك ومجلد TwinMemory لا تتأثر بالتحديث."), Foreground = System.Windows.Media.Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
        Content = panel;
        Loaded += async (_, _) => await CheckAsync();
    }

    async Task CheckAsync()
    {
        if (_busy) return;
        _busy = true; _apply.IsEnabled = false; _notes.Visibility = Visibility.Collapsed;
        _status.Text = S.T("جارٍ التحقق…");
        var (info, err) = await UpdateService.CheckAsync();
        if (err != null) _status.Text = err;
        else if (info == null) _status.Text = S.T("أنت على أحدث إصدار");
        else
        {
            _info = info;
            _status.Text = S.T("يتوفر إصدار جديد") + ": " + info.Version;
            if (!string.IsNullOrWhiteSpace(info.Notes)) { _notes.Text = S.T("ما الجديد") + ":\n" + info.Notes; _notes.Visibility = Visibility.Visible; }
            _apply.IsEnabled = true;
        }
        _busy = false;
    }

    async Task ApplyAsync()
    {
        if (_info == null || _busy) return;
        if (App.Db.ListTransfers(100).Any(t => t.State == "receiving") && !Dlg.Confirm(this, S.T("توجد عمليات نقل جارية. سيُعاد تشغيل البرنامج للتحديث وتُستأنف لاحقًا. متابعة؟"))) return;
        _busy = true; _apply.IsEnabled = false; _check.IsEnabled = false;
        _bar.Value = 0; _bar.Visibility = Visibility.Visible; _status.Text = S.T("جارٍ التنزيل");
        var (path, err) = await UpdateService.DownloadAsync(_info, new Progress<int>(p => _bar.Value = p));
        if (err != null) { _status.Text = err; }
        else
        {
            _status.Text = S.T("تم التحقق من الملف. جارٍ التحديث…");
            var fail = UpdateService.ApplyAndRestart(path!);
            if (fail != null) _status.Text = S.T("تعذّر استبدال البرنامج (قد يكون في مجلد محمي). الملف المُنزَّل في") + ": " + path + "\n" + fail;
        }
        _bar.Visibility = Visibility.Collapsed; _check.IsEnabled = true; _busy = false;
    }
}
