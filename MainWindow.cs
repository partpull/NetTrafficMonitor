using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace NetTrafficMonitor
{
    /// <summary>
    /// 悬浮窗主体：深色半透明圆角卡片、始终置顶；
    /// 紧凑模式显示 4 项，展开模式显示 9 项；支持拖动、双击或右上角按钮切换模式。
    /// </summary>
    internal sealed class MainWindow : Window
    {
        private const double CardWidth = 280;      // 可见卡片宽度
        private const double HCompact = 72;        // 紧凑模式可见高度
        private const double HExpanded = 165;      // 展开模式可见高度
        private const double ShadowPad = 8;        // 四周留白，用于绘制投影

        private static readonly Brush BrText = MakeBrush(0xFF, 0xED, 0xED, 0xF2);
        private static readonly Brush BrLabel = MakeBrush(0xFF, 0x9A, 0x9A, 0xA6);
        private static readonly Brush BrGroup = MakeBrush(0xFF, 0x82, 0x82, 0x8E);
        private static readonly Brush BrUp = MakeBrush(0xFF, 0x4A, 0xDE, 0x80);
        private static readonly Brush BrDown = MakeBrush(0xFF, 0x60, 0xA5, 0xFA);
        private static readonly Brush BrHover = MakeBrush(0x1A, 0xFF, 0xFF, 0xFF);
        private static readonly FontFamily FFNum = new FontFamily("Consolas");
        private static readonly FontFamily FFCn = new FontFamily("Microsoft YaHei UI");
        private static readonly FontFamily FFGlyph = new FontFamily("Segoe UI Symbol, Segoe UI");

        private readonly Config _cfg;
        private readonly MonitorEngine _engine;
        private readonly int[] _refreshValues = new int[] { 500, 1000, 2000 };

        private bool _expanded;
        private Grid _compactView;
        private Grid _expandedView;
        private TextBlock _compactUp;
        private TextBlock _compactDown;
        private TextBlock _compactCpu;
        private TextBlock _compactMem;
        private TextBlock _xSysUp;
        private TextBlock _xSysDown;
        private TextBlock _xProxyUp;
        private TextBlock _xProxyDown;
        private TextBlock _xCpu;
        private TextBlock _xMem;
        private TextBlock _xUpTotal;
        private TextBlock _xDownTotal;
        private TextBlock _xProxyDownTotal;
        private TextBlock _chevron;
        private ContextMenu _menu;
        private MenuItem _miAutoStart;
        private MenuItem[] _miRefreshItems;

        public MainWindow()
        {
            _cfg = Config.Load();
            _expanded = _cfg.Mode == "expanded";

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Title = "网络流量监控V1";
            UseLayoutRounding = true;
            Width = CardWidth + ShadowPad * 2;
            Height = (_expanded ? HExpanded : HCompact) + ShadowPad * 2;

            BuildMenu();
            BuildUi();
            ApplyInitialPosition();

            _engine = new MonitorEngine(_cfg);
            _engine.SnapshotReady += OnSnapshot;

            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        // ---------- 界面构建 ----------

        private void BuildUi()
        {
            Border card = new Border();
            card.CornerRadius = new CornerRadius(12);
            card.Background = MakeBrush(0xE0, 0x14, 0x14, 0x18);   // rgba(20,20,24,0.88)
            card.BorderBrush = MakeBrush(0x18, 0xFF, 0xFF, 0xFF);
            card.BorderThickness = new Thickness(1);
            card.Margin = new Thickness(ShadowPad);
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 14;
            shadow.ShadowDepth = 0;
            shadow.Opacity = 0.35;
            shadow.Color = Colors.Black;
            card.Effect = shadow;

            Grid root = new Grid();
            root.Margin = new Thickness(12);

            _compactView = BuildCompactView();
            _expandedView = BuildExpandedView();
            _compactView.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
            _expandedView.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            root.Children.Add(_compactView);
            root.Children.Add(_expandedView);

            // 右上角展开/收起按钮
            _chevron = new TextBlock();
            _chevron.Text = _expanded ? "▴" : "▾";
            _chevron.FontFamily = FFGlyph;
            _chevron.FontSize = 12;
            _chevron.Foreground = BrGroup;
            Border chevronBtn = new Border();
            chevronBtn.Background = Brushes.Transparent;
            chevronBtn.Padding = new Thickness(4, 1, 4, 2);
            chevronBtn.CornerRadius = new CornerRadius(4);
            chevronBtn.Child = _chevron;
            chevronBtn.Cursor = Cursors.Hand;
            chevronBtn.HorizontalAlignment = HorizontalAlignment.Right;
            chevronBtn.VerticalAlignment = VerticalAlignment.Top;
            chevronBtn.Margin = new Thickness(0, -6, -8, 0);
            chevronBtn.ToolTip = "展开 / 收起（双击窗口亦可）";
            chevronBtn.MouseEnter += OnChevronEnter;
            chevronBtn.MouseLeave += OnChevronLeave;
            chevronBtn.MouseLeftButtonDown += OnChevronDown;
            Panel.SetZIndex(chevronBtn, 10);
            root.Children.Add(chevronBtn);

            card.Child = root;
            card.ContextMenu = _menu;
            card.MouseLeftButtonDown += OnCardMouseDown;
            card.MouseLeftButtonUp += OnCardMouseUp;
            Content = card;
        }

        private Grid BuildCompactView()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(MakeRow(20));
            g.RowDefinitions.Add(MakeRow(8));
            g.RowDefinitions.Add(MakeRow(20));
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());

            _compactUp = MakeValue();
            _compactDown = MakeValue();
            _compactCpu = MakeValue();
            _compactMem = MakeValue();

            AddValue(g, 0, 0, null, _compactUp, "↑", BrUp);
            AddValue(g, 0, 1, null, _compactDown, "↓", BrDown);
            AddValue(g, 2, 0, "CPU", _compactCpu, null, null);
            AddValue(g, 2, 1, "内存", _compactMem, null, null);
            return g;
        }

        private Grid BuildExpandedView()
        {
            Grid g = new Grid();
            double[] rows = new double[] { 20, 8, 20, 8, 20, 8, 1, 8, 20, 8, 20 };
            for (int i = 0; i < rows.Length; i++) g.RowDefinitions.Add(MakeRow(rows[i]));
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(56);
            g.ColumnDefinitions.Add(c0);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());

            _xSysUp = MakeValue();
            _xSysDown = MakeValue();
            _xProxyUp = MakeValue();
            _xProxyDown = MakeValue();
            _xCpu = MakeValue();
            _xMem = MakeValue();
            _xUpTotal = MakeValue();
            _xDownTotal = MakeValue();
            _xProxyDownTotal = MakeValue();

            AddGroup(g, 0, "系统");
            AddValue(g, 0, 1, null, _xSysUp, "↑", BrUp);
            AddValue(g, 0, 2, null, _xSysDown, "↓", BrDown);

            AddGroup(g, 2, "代理");
            AddValue(g, 2, 1, null, _xProxyUp, "↑", BrUp);
            AddValue(g, 2, 2, null, _xProxyDown, "↓", BrDown);

            AddGroup(g, 4, "硬件");
            AddValue(g, 4, 1, "CPU", _xCpu, null, null);
            AddValue(g, 4, 2, "内存", _xMem, null, null);

            Border line = new Border();
            line.Background = MakeBrush(0x18, 0xFF, 0xFF, 0xFF);
            line.Height = 1;
            line.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(line, 6);
            Grid.SetColumnSpan(line, 3);
            g.Children.Add(line);

            AddGroup(g, 8, "本次运行");
            AddValue(g, 8, 1, "上传", _xUpTotal, null, null);
            AddValue(g, 8, 2, "下载", _xDownTotal, null, null);
            AddValue(g, 10, 1, "代理下载", _xProxyDownTotal, null, null);
            return g;
        }

        private static RowDefinition MakeRow(double h)
        {
            RowDefinition r = new RowDefinition();
            r.Height = new GridLength(h);
            return r;
        }

        private static TextBlock MakeValue()
        {
            TextBlock t = new TextBlock();
            t.FontFamily = FFNum;
            t.FontSize = 13;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = BrText;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        private static void AddGroup(Grid g, int row, string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = FFCn;
            t.FontSize = 12;
            t.Foreground = BrGroup;
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(t, row);
            Grid.SetColumn(t, 0);
            g.Children.Add(t);
        }

        private static void AddValue(Grid g, int row, int col, string label, TextBlock value, string arrow, Brush arrowBrush)
        {
            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.VerticalAlignment = VerticalAlignment.Center;
            if (col == 2) sp.Margin = new Thickness(12, 0, 0, 0);

            if (arrow != null)
            {
                TextBlock a = new TextBlock();
                a.Text = arrow;
                a.FontFamily = FFNum;
                a.FontSize = 13;
                a.Foreground = arrowBrush;
                a.VerticalAlignment = VerticalAlignment.Center;
                a.Margin = new Thickness(0, 0, 4, 0);
                sp.Children.Add(a);
            }
            if (label != null)
            {
                TextBlock l = new TextBlock();
                l.Text = label;
                l.FontFamily = FFCn;
                l.FontSize = 12;
                l.Foreground = BrLabel;
                l.VerticalAlignment = VerticalAlignment.Center;
                l.Margin = new Thickness(0, 0, 6, 0);
                sp.Children.Add(l);
            }
            sp.Children.Add(value);
            Grid.SetRow(sp, row);
            Grid.SetColumn(sp, col);
            g.Children.Add(sp);
        }

        // ---------- 右键菜单 ----------

        private void BuildMenu()
        {
            _menu = new ContextMenu();
            _menu.FontFamily = FFCn;
            _menu.FontSize = 12;

            MenuItem miToggle = new MenuItem();
            miToggle.Header = "切换显示模式";
            miToggle.Click += OnToggleMenuClick;
            _menu.Items.Add(miToggle);

            MenuItem miRefresh = new MenuItem();
            miRefresh.Header = "刷新频率";
            string[] names = new string[] { "0.5 秒", "1 秒", "2 秒" };
            _miRefreshItems = new MenuItem[_refreshValues.Length];
            for (int i = 0; i < _refreshValues.Length; i++)
            {
                MenuItem mi = new MenuItem();
                mi.Header = names[i];
                mi.IsCheckable = true;
                mi.Tag = _refreshValues[i];
                mi.Click += OnRefreshClick;
                _miRefreshItems[i] = mi;
                miRefresh.Items.Add(mi);
            }
            _menu.Items.Add(miRefresh);

            MenuItem miProxy = new MenuItem();
            miProxy.Header = "代理设置…";
            miProxy.Click += OnProxySettingsClick;
            _menu.Items.Add(miProxy);

            _miAutoStart = new MenuItem();
            _miAutoStart.Header = "开机自启";
            _miAutoStart.IsCheckable = true;
            _miAutoStart.Click += OnAutoStartClick;
            _menu.Items.Add(_miAutoStart);

            _menu.Items.Add(new Separator());

            MenuItem miExit = new MenuItem();
            miExit.Header = "退出";
            miExit.Click += OnExitClick;
            _menu.Items.Add(miExit);

            _menu.Opened += OnMenuOpened;
        }

        private void OnMenuOpened(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < _miRefreshItems.Length; i++)
            {
                _miRefreshItems[i].IsChecked = (int)_miRefreshItems[i].Tag == _cfg.RefreshMs;
            }
            _miAutoStart.IsChecked = Autostart.IsEnabled();
        }

        private void OnToggleMenuClick(object sender, RoutedEventArgs e)
        {
            ToggleMode();
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            MenuItem mi = (MenuItem)sender;
            int ms = (int)mi.Tag;
            _engine.SetInterval(ms);
            _cfg.Save();
        }

        private void OnAutoStartClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Autostart.SetEnabled(!Autostart.IsEnabled());
            }
            catch { }
        }

        private void OnProxySettingsClick(object sender, RoutedEventArgs e)
        {
            SettingsWindow w = new SettingsWindow(_cfg, _engine);
            w.Owner = this;
            w.ShowDialog();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ---------- 交互 ----------

        private void OnChevronEnter(object sender, MouseEventArgs e)
        {
            _chevron.Foreground = BrText;
            ((Border)sender).Background = BrHover;
        }

        private void OnChevronLeave(object sender, MouseEventArgs e)
        {
            _chevron.Foreground = BrGroup;
            ((Border)sender).Background = Brushes.Transparent;
        }

        private void OnChevronDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ToggleMode();
        }

        private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMode();
                return;
            }
            try
            {
                DragMove();
            }
            catch { }
        }

        private void OnCardMouseUp(object sender, MouseButtonEventArgs e)
        {
            SavePosition();
        }

        private void ToggleMode()
        {
            _expanded = !_expanded;
            _cfg.Mode = _expanded ? "expanded" : "compact";
            _cfg.Save();
            _chevron.Text = _expanded ? "▴" : "▾";
            AnimateTo(_expanded);
        }

        private void AnimateTo(bool expanded)
        {
            _compactView.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            _expandedView.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

            double target = (expanded ? HExpanded : HCompact) + ShadowPad * 2;
            double from = Height;

            DoubleAnimation heightAnim = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(180));
            heightAnim.EasingFunction = new CubicEase();
            ((CubicEase)heightAnim.EasingFunction).EasingMode = EasingMode.EaseOut;
            heightAnim.Completed += delegate
            {
                BeginAnimation(HeightProperty, null);
                Height = target;
            };
            BeginAnimation(HeightProperty, heightAnim);

            _expandedView.BeginAnimation(OpacityProperty, null);
            _expandedView.Opacity = 1;
            DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170));
            fade.EasingFunction = new CubicEase();
            ((CubicEase)fade.EasingFunction).EasingMode = EasingMode.EaseOut;
            _expandedView.BeginAnimation(OpacityProperty, fade);
        }

        // ---------- 数据刷新 ----------

        private void OnSnapshot(Snapshot s)
        {
            string up = Fmt.MBps(s.SysUpBps);
            string down = Fmt.MBps(s.SysDownBps);
            string cpu = Fmt.Percent(s.CpuPercent);
            string mem = Fmt.Percent(s.MemPercent);

            _compactUp.Text = up;
            _compactDown.Text = down;
            _compactCpu.Text = cpu;
            _compactMem.Text = mem;
            _xSysUp.Text = up;
            _xSysDown.Text = down;
            _xCpu.Text = cpu;
            _xMem.Text = mem;
            _xUpTotal.Text = Fmt.Bytes(s.SessionUpBytes);
            _xDownTotal.Text = Fmt.Bytes(s.SessionDownBytes);

            if (s.ProxyAvailable)
            {
                _xProxyUp.Text = Fmt.MBps(s.ProxyUpBps);
                _xProxyUp.Foreground = BrText;
                _xProxyDown.Text = Fmt.MBps(s.ProxyDownBps);
                _xProxyDown.Foreground = BrText;
                _xProxyDownTotal.Text = Fmt.Bytes(s.SessionProxyDownBytes);
                _xProxyDownTotal.Foreground = BrText;
                ToolTipService.SetToolTip(_xProxyUp, "来源：" + s.ProxySource);
            }
            else
            {
                _xProxyUp.Text = "--";
                _xProxyUp.Foreground = BrGroup;
                _xProxyDown.Text = "--";
                _xProxyDown.Foreground = BrGroup;
                _xProxyDownTotal.Text = "--";
                _xProxyDownTotal.Foreground = BrGroup;
                ToolTipService.SetToolTip(_xProxyUp, "未检测到代理（右键 → 代理设置）");
            }
        }

        // ---------- 生命周期与位置 ----------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _engine.Start();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _engine.Stop();
            SavePosition();
        }

        private void ApplyInitialPosition()
        {
            if (_cfg.WindowX >= 0 && _cfg.WindowY >= 0)
            {
                Left = _cfg.WindowX;
                Top = _cfg.WindowY;
                return;
            }
            // 默认位置：屏幕右上角（可见卡片距上/右边缘各 16px）
            Rect wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 8;
            Top = wa.Top + 8;
        }

        private void SavePosition()
        {
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            _cfg.WindowX = (int)Math.Round(Left);
            _cfg.WindowY = (int)Math.Round(Top);
            _cfg.Save();
        }

        private static SolidColorBrush MakeBrush(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }
    }
}