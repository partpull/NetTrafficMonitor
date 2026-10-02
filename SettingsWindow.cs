using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NetTrafficMonitor
{
    /// <summary>代理流量设置：自动检测 / 指定 Clash API 端口与密钥 / 指定统计网卡。</summary>
    internal sealed class SettingsWindow : Window
    {
        private static readonly Brush BrBg = MakeBrush(0xF2, 0x17, 0x17, 0x1C);
        private static readonly Brush BrBorder = MakeBrush(0x24, 0xFF, 0xFF, 0xFF);
        private static readonly Brush BrText = MakeBrush(0xFF, 0xED, 0xED, 0xF2);
        private static readonly Brush BrLabel = MakeBrush(0xFF, 0x9A, 0x9A, 0xA6);
        private static readonly Brush BrInput = MakeBrush(0xFF, 0x26, 0x26, 0x2C);
        private static readonly Brush BrInputBorder = MakeBrush(0xFF, 0x3A, 0x3A, 0x42);
        private static readonly FontFamily FFCn = new FontFamily("Microsoft YaHei UI");

        private readonly Config _cfg;
        private readonly MonitorEngine _engine;
        private RadioButton _rbAuto;
        private RadioButton _rbApi;
        private RadioButton _rbAdapter;
        private TextBox _txtPort;
        private TextBox _txtSecret;
        private ComboBox _cmbAdapter;
        private TextBlock _status;

        public SettingsWindow(Config cfg, MonitorEngine engine)
        {
            _cfg = cfg;
            _engine = engine;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Width = 352;
            SizeToContent = SizeToContent.Height;
            Title = "代理设置";

            Border card = new Border();
            card.CornerRadius = new CornerRadius(12);
            card.Background = BrBg;
            card.BorderBrush = BrBorder;
            card.BorderThickness = new Thickness(1);
            DropShadowEffect shadow = new DropShadowEffect();
            shadow.BlurRadius = 14;
            shadow.ShadowDepth = 0;
            shadow.Opacity = 0.35;
            shadow.Color = Colors.Black;
            card.Effect = shadow;
            card.MouseLeftButtonDown += OnCardMouseDown;

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(16);
            card.Child = sp;

            TextBlock title = new TextBlock();
            title.Text = "代理流量设置";
            title.FontFamily = FFCn;
            title.FontSize = 14;
            title.FontWeight = FontWeights.Medium;
            title.Foreground = BrText;
            sp.Children.Add(title);

            TextBlock hint = new TextBlock();
            hint.Text = "代理速率来源：优先 Clash 系 API；其次 TUN / 虚拟网卡";
            hint.FontFamily = FFCn;
            hint.FontSize = 12;
            hint.Foreground = BrLabel;
            hint.Margin = new Thickness(0, 4, 0, 12);
            sp.Children.Add(hint);

            _rbAuto = MakeRadio("自动检测（推荐）", _cfg.ProxyMode == "auto");
            _rbAuto.Checked += OnModeChanged;
            sp.Children.Add(_rbAuto);

            _rbApi = MakeRadio("手动指定 Clash API 端口", _cfg.ProxyMode == "api");
            _rbApi.Checked += OnModeChanged;
            sp.Children.Add(_rbApi);

            StackPanel apiRow = new StackPanel();
            apiRow.Orientation = Orientation.Horizontal;
            apiRow.Margin = new Thickness(22, 6, 0, 10);
            _txtPort = MakeTextBox(_cfg.ProxyPort > 0 ? _cfg.ProxyPort.ToString(CultureInfo.InvariantCulture) : "");
            _txtPort.Width = 64;
            _txtSecret = MakeTextBox(_cfg.ProxySecret == null ? "" : _cfg.ProxySecret);
            _txtSecret.Width = 128;
            apiRow.Children.Add(MakeCaption("端口"));
            apiRow.Children.Add(_txtPort);
            apiRow.Children.Add(MakeCaption("密钥"));
            apiRow.Children.Add(_txtSecret);
            sp.Children.Add(apiRow);

            _rbAdapter = MakeRadio("手动指定统计网卡", _cfg.ProxyMode == "adapter");
            _rbAdapter.Checked += OnModeChanged;
            sp.Children.Add(_rbAdapter);

            _cmbAdapter = new ComboBox();
            _cmbAdapter.FontFamily = FFCn;
            _cmbAdapter.FontSize = 12;
            _cmbAdapter.Margin = new Thickness(22, 6, 0, 10);
            FillAdapters();
            sp.Children.Add(_cmbAdapter);

            _status = new TextBlock();
            _status.FontFamily = FFCn;
            _status.FontSize = 12;
            _status.Foreground = BrLabel;
            _status.TextWrapping = TextWrapping.Wrap;
            _status.Margin = new Thickness(0, 2, 0, 12);
            _status.Text = "当前状态：" + engine.Proxy.StatusText;
            sp.Children.Add(_status);

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            Button btnCancel = MakeButton("取消", false);
            btnCancel.Click += OnCancel;
            Button btnSave = MakeButton("保存", true);
            btnSave.Click += OnSave;
            buttons.Children.Add(btnCancel);
            buttons.Children.Add(btnSave);
            sp.Children.Add(buttons);

            Content = card;
            KeyDown += OnKeyDown;
            UpdateEnabled();
        }

        private void FillAdapters()
        {
            List<string> names = new List<string>();
            List<InterfaceDelta> deltas = _engine.LastDeltas;
            for (int i = 0; i < deltas.Count; i++)
            {
                InterfaceDelta d = deltas[i];
                if (d.IsLoopback) continue;
                if (!names.Contains(d.DisplayName)) names.Add(d.DisplayName);
            }
            if (!string.IsNullOrEmpty(_cfg.ProxyAdapter) && !names.Contains(_cfg.ProxyAdapter))
            {
                names.Add(_cfg.ProxyAdapter);
            }
            for (int i = 0; i < names.Count; i++) _cmbAdapter.Items.Add(names[i]);
            if (!string.IsNullOrEmpty(_cfg.ProxyAdapter))
            {
                _cmbAdapter.SelectedItem = _cfg.ProxyAdapter;
            }
            else if (_cmbAdapter.Items.Count > 0)
            {
                _cmbAdapter.SelectedIndex = 0;
            }
        }

        private void OnModeChanged(object sender, RoutedEventArgs e)
        {
            UpdateEnabled();
        }

        private void UpdateEnabled()
        {
            _txtPort.IsEnabled = _rbApi.IsChecked == true;
            _txtSecret.IsEnabled = _rbApi.IsChecked == true;
            _cmbAdapter.IsEnabled = _rbAdapter.IsChecked == true;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (_rbApi.IsChecked == true)
            {
                int port;
                if (!int.TryParse(_txtPort.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
                    || port <= 0 || port > 65535)
                {
                    MessageBox.Show(this, "请填写 1-65535 之间的端口号。", "代理设置");
                    return;
                }
                _cfg.ProxyMode = "api";
                _cfg.ProxyPort = port;
                _cfg.ProxySecret = _txtSecret.Text.Trim();
            }
            else if (_rbAdapter.IsChecked == true)
            {
                if (_cmbAdapter.SelectedItem == null)
                {
                    MessageBox.Show(this, "请先选择一个网卡。", "代理设置");
                    return;
                }
                _cfg.ProxyMode = "adapter";
                _cfg.ProxyAdapter = (string)_cmbAdapter.SelectedItem;
            }
            else
            {
                _cfg.ProxyMode = "auto";
            }
            _cfg.Save();
            _engine.RestartProxy();
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch { }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        private RadioButton MakeRadio(string text, bool isChecked)
        {
            RadioButton rb = new RadioButton();
            rb.Content = text;
            rb.FontFamily = FFCn;
            rb.FontSize = 12;
            rb.Foreground = BrText;
            rb.IsChecked = isChecked;
            rb.VerticalContentAlignment = VerticalAlignment.Center;
            return rb;
        }

        private TextBox MakeTextBox(string text)
        {
            TextBox t = new TextBox();
            t.Text = text;
            t.FontFamily = FFCn;
            t.FontSize = 12;
            t.Background = BrInput;
            t.Foreground = BrText;
            t.BorderBrush = BrInputBorder;
            t.CaretBrush = BrText;
            t.Padding = new Thickness(4, 2, 4, 2);
            t.Margin = new Thickness(0, 0, 12, 0);
            t.VerticalContentAlignment = VerticalAlignment.Center;
            return t;
        }

        private TextBlock MakeCaption(string text)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = FFCn;
            t.FontSize = 12;
            t.Foreground = BrLabel;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(0, 0, 6, 0);
            return t;
        }

        private Button MakeButton(string text, bool primary)
        {
            Button b = new Button();
            b.Content = text;
            b.FontFamily = FFCn;
            b.FontSize = 12;
            b.Foreground = BrText;
            b.Background = primary ? MakeBrush(0xFF, 0x1F, 0x7A, 0x4D) : MakeBrush(0xFF, 0x2A, 0x2A, 0x32);
            b.BorderThickness = new Thickness(0);
            b.Padding = new Thickness(16, 5, 16, 5);
            b.Cursor = Cursors.Hand;
            b.Margin = new Thickness(8, 0, 0, 0);
            return b;
        }

        private static SolidColorBrush MakeBrush(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }
    }
}