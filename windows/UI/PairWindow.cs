using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TwinMemory.Net;

namespace TwinMemory;

/// <summary>Shows a one-time pairing code and manages paired phones.</summary>
public class PairWindow : Window
{
    readonly TextBlock _code = new() { FontSize = 38, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 4) };
    readonly TextBlock _left = new() { HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.Gray };
    readonly ListBox _devices = new() { MinHeight = 90, DisplayMemberPath = "Name" };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly TextBlock _result = new();
    DateTime _expires = DateTime.MinValue;

    public PairWindow(Window owner)
    {
        Owner = owner; Title = S.T("ربط الهاتف"); Width = 500; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        FlowDirection = owner.FlowDirection; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;

        var addrs = TwinServer.LocalAddresses();
        var addr = string.Join("   ", addrs);
        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), Text = S.T("الرمز صالح دقيقتين ويُستخدم مرة واحدة.\nعلى الهاتف: المزيد ← ربط الكمبيوتر، ثم أدخل العنوان والرمز.\nيجب أن يكون الجهازان على نفس الشبكة. اسمح لـ TwinMemory في جدار حماية Windows عند ظهور النافذة (الشبكة الخاصة).") };
        var addrBox = new TextBox { Text = S.T("العنوان") + ": " + (addr.Length == 0 ? "—" : addr) + "     " + S.T("المنفذ") + ": " + TwinServer.Port, IsReadOnly = true, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6) };
        var err = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Text = App.ServerError == null ? "" : S.T("تعذّر تشغيل خادم الربط") + ": " + App.ServerError };

        var newCode = new Button { Content = S.T("رمز جديد"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        newCode.Click += (_, _) => Generate();
        var remove = new Button { Content = S.T("إلغاء ربط المحدد"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        remove.Click += (_, _) => { if (_devices.SelectedItem is Core.DeviceInfo d) { App.Db.DeleteDevice(d.Id); RefreshDevices(); } };
        var test = new Button { Content = S.T("اختبار الخادم"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        test.Click += async (_, _) => _result.Text = S.T("نتيجة الاختبار") + ": " + await SelfTest();
        var fw = new Button { Content = S.T("السماح في جدار الحماية"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        fw.Click += (_, _) => OpenFirewall();
        var close = new Button { Content = S.T("إغلاق"), IsCancel = true, Padding = new Thickness(10, 4, 10, 4) };
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        row.Children.Add(newCode); row.Children.Add(remove); row.Children.Add(test); row.Children.Add(fw); row.Children.Add(close);
        _result.Text = App.Server.Running ? S.T("الخادم يعمل") : S.T("الخادم لا يعمل") + (App.ServerError == null ? "" : ": " + App.ServerError);
        _result.TextWrapping = TextWrapping.Wrap; _result.Margin = new Thickness(0, 8, 0, 0);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(_code); panel.Children.Add(_left); panel.Children.Add(info); panel.Children.Add(addrBox); panel.Children.Add(err);
        panel.Children.Add(new TextBlock { Text = S.T("الأجهزة المرتبطة"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 4) });
        panel.Children.Add(_devices); panel.Children.Add(row); panel.Children.Add(_result);
        panel.Children.Add(new TextBlock { Text = S.T("سجل الخادم") + ": " + App.Server.LogPath, Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        Content = panel;

        _timer.Tick += (_, _) =>
        {
            var s = (int)(_expires - DateTime.UtcNow).TotalSeconds;
            if (s <= 0) { _code.Text = "— — — —"; _left.Text = S.T("منتهي"); } else _left.Text = $"{s / 60}:{s % 60:00}";
        };
        App.Server.DataChanged += OnData;
        Closed += (_, _) => { _timer.Stop(); App.Server.DataChanged -= OnData; };
        Loaded += (_, _) => { Generate(); RefreshDevices(); _timer.Start(); };
    }

    void OnData() => Dispatcher.Invoke(RefreshDevices);

    void Generate()
    {
        var (code, exp) = App.Server.NewPairCode();
        _expires = exp;
        _code.Text = code[..4] + " " + code[4..];
    }

    void RefreshDevices()
    {
        _devices.ItemsSource = App.Db.Devices();
        if (_devices.Items.Count == 0) _devices.ItemsSource = new[] { new Core.DeviceInfo("", S.T("لا توجد أجهزة مرتبطة"), "", 0, 0) };
    }

    static async Task<string> SelfTest()
    {
        try
        {
            using var h = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
            using var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(4) };
            var r = await c.GetAsync($"https://127.0.0.1:{TwinServer.Port}/info");
            return r.IsSuccessStatusCode ? S.T("الخادم يعمل") : "HTTP " + (int)r.StatusCode;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Adds inbound rules (local subnet only) for this program. Windows asks for administrator approval.</summary>
    void OpenFirewall()
    {
        try
        {
            var exe = Environment.ProcessPath!;
            var cmd = "/c netsh advfirewall firewall delete rule name=\"TwinMemory TCP\" & netsh advfirewall firewall delete rule name=\"TwinMemory UDP\" & " +
                      $"netsh advfirewall firewall add rule name=\"TwinMemory TCP\" dir=in action=allow program=\"{exe}\" protocol=TCP localport={TwinServer.Port} remoteip=localsubnet profile=any & " +
                      $"netsh advfirewall firewall add rule name=\"TwinMemory UDP\" dir=in action=allow program=\"{exe}\" protocol=UDP localport={TwinServer.DiscoveryPort} remoteip=localsubnet profile=any";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            _result.Text = S.T("طُلبت إضافة قاعدة جدار الحماية — وافق على طلب المدير ثم أعد المحاولة من الهاتف");
        }
        catch (Exception ex) { _result.Text = ex.Message; }
    }
}
