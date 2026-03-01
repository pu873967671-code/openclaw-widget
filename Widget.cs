using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace OpenClawWidget
{
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            new App().Run(new IconWindow());
        }
    }

    public class IconWindow : Window
    {
        Ellipse bgCircle, dotCircle;
        DropShadowEffect ringGlow;
        System.Windows.Controls.Image lobsterImage;
        bool _healthyVisual = true;
        BitmapImage _lobsterImageSource;
        PanelWindow panelWin;
        System.Windows.Forms.NotifyIcon trayIcon;
        DispatcherTimer hideTimer;
        DateTime _lastRecoverAttempt = DateTime.MinValue;
        bool _recoverInProgress = false;
        static readonly TimeSpan RecoverCooldown = TimeSpan.FromMinutes(2);
        const string API = "http://localhost:4200/status";
        const int ICON = 48;

        public IconWindow()
        {
            Title = "OC";
            Width = ICON + 4; Height = ICON + 4;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;

            Left = SystemParameters.WorkArea.Width - ICON - 20;
            Top = SystemParameters.WorkArea.Height - ICON - 20;

            var grid = new Grid();
            Content = grid;

            bgCircle = new Ellipse
            {
                Width = ICON, Height = ICON,
                Fill = MakeBrush("#182633"),
                Stroke = MakeBrush("#4ADE80"),
                StrokeThickness = 2,
            };
            ringGlow = new DropShadowEffect
            {
                Color = System.Windows.Media.Color.FromRgb(74, 222, 128),
                BlurRadius = 9,
                ShadowDepth = 0,
                Opacity = 0.75,
            };
            bgCircle.Effect = ringGlow;
            grid.Children.Add(bgCircle);

            lobsterImage = new System.Windows.Controls.Image
            {
                Width = 30,
                Height = 30,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                SnapsToDevicePixels = true,
            };
            grid.Children.Add(lobsterImage);
            LoadLobsterImage();

            dotCircle = new Ellipse
            {
                Width = 10, Height = 10,
                Fill = MakeBrush("#4ADE80"),
                Stroke = MakeBrush("#306230"),
                StrokeThickness = 2,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 2),
            };
            grid.Children.Add(dotCircle);

            panelWin = new PanelWindow(this);

            MouseEnter += (s, e) => ShowPanel();
            MouseLeave += (s, e) => ScheduleHide();
            MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { PlayFireAnimation(); DoRefresh(); } else { DragMove(); } };
            MouseRightButtonUp += (s, e) => ShowContextMenu();

            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(60);
            timer.Tick += (s, e) => DoRefresh();
            timer.Start();

            BuildTray();
            Loaded += (s, e) => DoRefresh();
            Closing += (s, e) =>
            {
                if (trayIcon != null) trayIcon.Dispose();
                panelWin.ForceClose();
            };
            LocationChanged += (s, e) =>
            {
                if (panelWin.IsVisible) PositionPanel();
            };
        }

        void PlayFireAnimation()
        {
            // Emoji 龙虾弹跳
            var scale = lobsterImage != null ? lobsterImage.RenderTransform as ScaleTransform : null;
            if (scale != null)
            {
                var sx = new DoubleAnimation(1.0, 1.28, TimeSpan.FromMilliseconds(140));
                sx.AutoReverse = true;
                var sy = new DoubleAnimation(1.0, 1.28, TimeSpan.FromMilliseconds(140));
                sy.AutoReverse = true;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            }

            // 背景闪光
            var fire1 = new ColorAnimation(
                System.Windows.Media.Color.FromRgb(15, 56, 15),
                System.Windows.Media.Color.FromRgb(139, 172, 15),
                TimeSpan.FromMilliseconds(180));
            var fire2 = new ColorAnimation(
                System.Windows.Media.Color.FromRgb(139, 172, 15),
                System.Windows.Media.Color.FromRgb(155, 188, 15),
                TimeSpan.FromMilliseconds(120));
            fire2.BeginTime = TimeSpan.FromMilliseconds(180);
            var fire3 = new ColorAnimation(
                System.Windows.Media.Color.FromRgb(155, 188, 15),
                System.Windows.Media.Color.FromRgb(15, 56, 15),
                TimeSpan.FromMilliseconds(220));
            fire3.BeginTime = TimeSpan.FromMilliseconds(300);

            var brush = bgCircle.Fill as SolidColorBrush;
            if (brush != null && brush.IsFrozen)
            {
                brush = brush.Clone();
                bgCircle.Fill = brush;
            }
            if (brush != null)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, fire1);
                var t2 = new DispatcherTimer();
                t2.Interval = TimeSpan.FromMilliseconds(180);
                t2.Tick += delegate { t2.Stop(); brush.BeginAnimation(SolidColorBrush.ColorProperty, fire2); };
                t2.Start();
                var t3 = new DispatcherTimer();
                t3.Interval = TimeSpan.FromMilliseconds(300);
                t3.Tick += delegate { t3.Stop(); brush.BeginAnimation(SolidColorBrush.ColorProperty, fire3); };
                t3.Start();
            }
        }

        void LoadLobsterImage()
        {
            if (lobsterImage == null) return;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string fp = System.IO.Path.Combine(baseDir, "assets\\lobster_static\\lobster.png");
            if (!System.IO.File.Exists(fp))
            {
                fp = System.IO.Path.Combine(baseDir, "assets\\lobster_static\\组 1.png");
                if (!System.IO.File.Exists(fp)) return;
            }
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(fp, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                _lobsterImageSource = bi;
                lobsterImage.Source = _lobsterImageSource;
            }
            catch { }
        }

        void ShowPanel()
        {
            CancelHide();
            if (!panelWin.IsVisible)
            {
                PositionPanel();
                panelWin.Show();
                DoRefresh();
            }
        }

        void PositionPanel()
        {
            double sw = SystemParameters.WorkArea.Width;
            double sh = SystemParameters.WorkArea.Height;
            double px, py;
            double pw = 300, ph = 360;

            if (Left + Width / 2 > sw / 2)
                px = Left - pw - 8;
            else
                px = Left + Width + 8;

            if (Top + Height / 2 > sh / 2)
                py = Top + Height - ph;
            else
                py = Top;

            if (px < 0) px = 0;
            if (py < 0) py = 0;
            if (px + pw > sw) px = sw - pw;
            if (py + ph > sh) py = sh - ph;

            panelWin.Left = px;
            panelWin.Top = py;
        }

        public void ScheduleHide()
        {
            CancelHide();
            hideTimer = new DispatcherTimer();
            hideTimer.Interval = TimeSpan.FromMilliseconds(400);
            hideTimer.Tick += (s2, e2) => { hideTimer.Stop(); panelWin.Hide(); };
            hideTimer.Start();
        }

        public void CancelHide()
        {
            if (hideTimer != null) { hideTimer.Stop(); hideTimer = null; }
        }

        public void SetDot(string hex)
        {
            var b = MakeBrush(hex);
            dotCircle.Fill = b;
            bgCircle.Stroke = b;
            var sb = b as SolidColorBrush;
            if (ringGlow != null && sb != null)
            {
                ringGlow.Color = sb.Color;
            }
            _healthyVisual = string.Equals(hex, "#E0F8CF", StringComparison.OrdinalIgnoreCase) || string.Equals(hex, "#9BBC0F", StringComparison.OrdinalIgnoreCase) || string.Equals(hex, "#4ADE80", StringComparison.OrdinalIgnoreCase);
        }

        void ShowContextMenu()
        {
            var cm = new ContextMenu();
            cm.Background = MakeBrush("#1E293B");
            cm.BorderBrush = MakeBrush("#334155");
            cm.Foreground = MakeBrush("#E2E8F0");

            var refresh = new MenuItem(); refresh.Header = "Refresh";
            refresh.Foreground = MakeBrush("#E2E8F0");
            refresh.Click += delegate { DoRefresh(); };
            cm.Items.Add(refresh);

            var opMenu = new MenuItem(); opMenu.Header = "Opacity";
            opMenu.Foreground = MakeBrush("#E2E8F0");
            int[] vals = new int[] { 100, 80, 60, 40 };
            for (int i = 0; i < vals.Length; i++)
            {
                int v = vals[i];
                var mi = new MenuItem();
                mi.Header = v.ToString() + "%";
                mi.Foreground = MakeBrush("#94A3B8");
                mi.Click += delegate { Opacity = v / 100.0; panelWin.Opacity = v / 100.0; };
                opMenu.Items.Add(mi);
            }
            cm.Items.Add(opMenu);

            cm.Items.Add(new Separator());

            var exit = new MenuItem(); exit.Header = "Exit";
            exit.Foreground = MakeBrush("#EF4444");
            exit.Click += delegate { Close(); };
            cm.Items.Add(exit);

            cm.IsOpen = true;
        }

        const string REG_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string REG_NAME = "OpenClawWidget";

        static bool GetAutoStart()
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_KEY, false);
                if (key == null) return false;
                object val = key.GetValue(REG_NAME);
                key.Close();
                return val != null;
            }
            catch { return false; }
        }

        static void SetAutoStart(bool enable)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(REG_KEY, true);
                if (key == null) return;
                if (enable)
                {
                    string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    key.SetValue(REG_NAME, "\"" + exePath + "\"");
                }
                else
                {
                    key.DeleteValue(REG_NAME, false);
                }
                key.Close();
            }
            catch { }
        }

        void BuildTray()
        {
            trayIcon = new System.Windows.Forms.NotifyIcon();
            trayIcon.Text = "OpenClaw";
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(new SolidBrush(System.Drawing.Color.FromArgb(26, 26, 46)), 0, 0, 16, 16);
                g.DrawEllipse(new System.Drawing.Pen(System.Drawing.Color.OrangeRed, 1), 0, 0, 15, 15);
            }
            trayIcon.Icon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Show", null, (s, e) => { Show(); Activate(); });
            menu.Items.Add("Refresh", null, (s, e) => DoRefresh());
            menu.Items.Add("-");
            var autoItem = new System.Windows.Forms.ToolStripMenuItem("Start with Windows");
            autoItem.Checked = GetAutoStart();
            autoItem.Click += delegate
            {
                bool next = !autoItem.Checked;
                SetAutoStart(next);
                autoItem.Checked = next;
            };
            menu.Items.Add(autoItem);
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, (s, e) => Close());
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += (s, e) => { Show(); Activate(); };
            trayIcon.Visible = true;
        }

        void TryAutoRecoverGateway(string reason)
        {
            if (_recoverInProgress) return;
            var now = DateTime.Now;
            if (now - _lastRecoverAttempt < RecoverCooldown) return;

            _recoverInProgress = true;
            _lastRecoverAttempt = now;

            Task.Run(() =>
            {
                try
                {
                    // 打开一个新的 WSL 窗口并执行网关拉起命令（等价于“打开 WSL + 回车执行”）
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c start \"\" wsl.exe -d Ubuntu -- bash -lc \"openclaw gateway start || openclaw gateway\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    Process.Start(psi);

                    Dispatcher.BeginInvoke((Action)(() =>
                    {
                        if (trayIcon != null)
                        {
                            trayIcon.ShowBalloonTip(2500, "OpenClaw", "Health Offline，已自动尝试拉起 Gateway", System.Windows.Forms.ToolTipIcon.Warning);
                        }
                    }));
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    _recoverInProgress = false;
                }
            });
        }

        public async void DoRefresh()
        {
            try
            {
                string json = await Task.Run(() =>
                {
                    using (var wc = new WebClient())
                    {
                        wc.Encoding = System.Text.Encoding.UTF8;
                        return wc.DownloadString(API);
                    }
                });
                panelWin.UpdateData(json);
                var ser = new JavaScriptSerializer();
                var d = ser.Deserialize<Dictionary<string, object>>(json);
                bool healthy = false;
                if (d.ContainsKey("health"))
                {
                    var h = (Dictionary<string, object>)d["health"];
                    healthy = h.ContainsKey("ok") && (bool)h["ok"];
                }
                SetDot(healthy ? "#4ADE80" : "#EF4444");
                if (trayIcon != null)
                    trayIcon.Text = healthy ? "OpenClaw: Healthy" : "OpenClaw: Unhealthy";

                if (!healthy)
                    TryAutoRecoverGateway("unhealthy");
            }
            catch
            {
                SetDot("#EF4444");
                if (trayIcon != null) trayIcon.Text = "OpenClaw: Offline";
                TryAutoRecoverGateway("offline");
            }
        }

        static SolidColorBrush MakeBrush(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6) hex = "FF" + hex;
            return new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16)));
        }
    }

    public class PanelWindow : Window
    {
        TextBlock tsBlock;
        StackPanel modelsPanel, networkPanel, tasksPanel;
        IconWindow owner;
        bool canClose;

        public PanelWindow(IconWindow parent)
        {
            owner = parent;
            Title = "OC Panel";
            Width = 300; Height = 360;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;

            var border = new Border
            {
                Background = B("#16213E"),
                BorderBrush = B("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(2),
            };

            var sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.Margin = new Thickness(10, 8, 10, 8);

            var st = new StackPanel();
            st.Children.Add(Lbl("OpenClaw", 13, "#FF6B35", true));
            tsBlock = Lbl("--:--:--", 9, "#64748B");
            tsBlock.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
            tsBlock.Margin = new Thickness(0, -14, 0, 3);
            st.Children.Add(tsBlock);
            st.Children.Add(Sep());

            // Models 标题 + 切换最快按钮
            var modelsHeader = new StackPanel();
            modelsHeader.Orientation = System.Windows.Controls.Orientation.Horizontal;
            modelsHeader.Margin = new Thickness(0, 3, 0, 1);
            modelsHeader.Children.Add(Lbl("Models", 11, "#E2E8F0", true));
            var fastBtn = new System.Windows.Controls.Button();
            fastBtn.Content = "⚡";
            fastBtn.FontSize = 10;
            fastBtn.Padding = new Thickness(4, 0, 4, 0);
            fastBtn.Margin = new Thickness(8, 0, 0, 0);
            fastBtn.Background = B("#3B82F6");
            fastBtn.Foreground = B("#FFFFFF");
            fastBtn.BorderBrush = B("#2563EB");
            fastBtn.Cursor = System.Windows.Input.Cursors.Hand;
            fastBtn.ToolTip = "切换到延迟最低的模型";
            fastBtn.Click += delegate { SwitchToFastest(fastBtn); };
            modelsHeader.Children.Add(fastBtn);
            st.Children.Add(modelsHeader);
            modelsPanel = new StackPanel(); st.Children.Add(modelsPanel);
            st.Children.Add(Sep());

            st.Children.Add(Lbl("Network", 11, "#E2E8F0", true));
            networkPanel = new StackPanel(); st.Children.Add(networkPanel);
            st.Children.Add(Sep());

            st.Children.Add(Lbl("Tasks", 11, "#E2E8F0", true));
            tasksPanel = new StackPanel(); st.Children.Add(tasksPanel);

            sv.Content = st;
            border.Child = sv;
            Content = border;

            MouseEnter += (s, e) => owner.CancelHide();
            MouseLeave += (s, e) => owner.ScheduleHide();

            Closing += (s, e) => { if (!canClose) { e.Cancel = true; Hide(); } };
        }

        public void ForceClose() { canClose = true; Close(); }

        public void UpdateData(string json)
        {
            var ser = new JavaScriptSerializer();
            var d = ser.Deserialize<Dictionary<string, object>>(json);

            tsBlock.Text = d.ContainsKey("ts") ? d["ts"].ToString() : "--:--:--";
            modelsPanel.Children.Clear();
            networkPanel.Children.Clear();
            tasksPanel.Children.Clear();

            if (d.ContainsKey("models"))
            {
                var list = (System.Collections.ArrayList)d["models"];
                foreach (Dictionary<string, object> m in list)
                {
                    bool ok = m.ContainsKey("ok") && (bool)m["ok"];
                    string name = m.ContainsKey("name") ? m["name"].ToString() : "?";
                    string lat = m.ContainsKey("latency") ? m["latency"].ToString() : "";
                    string ic = ok ? "[OK]" : "[X]";
                    string cl = ok ? "#4ADE80" : "#EF4444";

                    var row = new StackPanel();
                    row.Orientation = System.Windows.Controls.Orientation.Horizontal;
                    row.Children.Add(Row(" " + ic + " " + name + " (" + lat + ")", cl));
                    var btn = SmallBtn("~");
                    string n2 = name;
                    btn.Click += delegate { RefreshSingle("model", n2, row); };
                    row.Children.Add(btn);
                    // 切换按钮
                    var switchBtn = SmallBtn("⚡");
                    switchBtn.Background = B("#3B82F6");
                    switchBtn.Foreground = B("#FFFFFF");
                    switchBtn.ToolTip = "切换到此模型";
                    string n3 = name;
                    switchBtn.Click += delegate { SwitchToModel(n3, switchBtn); };
                    row.Children.Add(switchBtn);
                    modelsPanel.Children.Add(row);
                }
            }

            if (d.ContainsKey("network"))
            {
                var list = (System.Collections.ArrayList)d["network"];
                foreach (Dictionary<string, object> n in list)
                {
                    bool ok = n.ContainsKey("ok") && (bool)n["ok"];
                    string name = n.ContainsKey("name") ? n["name"].ToString() : "?";
                    string cl = ok ? "#4ADE80" : "#EF4444";

                    var row = new StackPanel();
                    row.Orientation = System.Windows.Controls.Orientation.Horizontal;
                    row.Children.Add(Row(" " + (ok ? "[OK]" : "[X]") + " " + name, cl));
                    var btn = SmallBtn("~");
                    string n2 = name;
                    btn.Click += delegate { RefreshSingle("network", n2, row); };
                    row.Children.Add(btn);
                    networkPanel.Children.Add(row);
                }
            }

            if (d.ContainsKey("tasks"))
            {
                var list = (System.Collections.ArrayList)d["tasks"];
                foreach (Dictionary<string, object> t in list)
                {
                    bool en = t.ContainsKey("enabled") && (bool)t["enabled"];
                    string name = t.ContainsKey("name") ? t["name"].ToString() : "?";

                    var row = new StackPanel();
                    row.Orientation = System.Windows.Controls.Orientation.Horizontal;
                    row.Children.Add(Row(" " + (en ? "[ON]" : "[OFF]") + " " + name, "#94A3B8"));
                    var btn = SmallBtn(">");
                    string n2 = name;
                    btn.Click += delegate { RunTask(n2, btn); };
                    row.Children.Add(btn);
                    tasksPanel.Children.Add(row);
                }
            }
        }

        void RefreshSingle(string type, string name, StackPanel row)
        {
            string ename = System.Uri.EscapeDataString(name);
            string url = "http://localhost:4200/check?type=" + type + "&name=" + ename;
            Task.Run(delegate
            {
                try
                {
                    var wc = new WebClient();
                    wc.Encoding = System.Text.Encoding.UTF8;
                    string json = wc.DownloadString(url);
                    Dispatcher.Invoke(delegate
                    {
                        var ser = new JavaScriptSerializer();
                        var r = ser.Deserialize<Dictionary<string, object>>(json);
                        bool ok = r.ContainsKey("ok") && (bool)r["ok"];
                        string lat = r.ContainsKey("latency") ? r["latency"].ToString() : "";
                        string ic = ok ? "[OK]" : "[X]";
                        string cl = ok ? "#4ADE80" : "#EF4444";
                        string txt = " " + ic + " " + name;
                        if (lat.Length > 0) txt = txt + " (" + lat + ")";
                        if (row.Children.Count > 0)
                        {
                            var tb = row.Children[0] as TextBlock;
                            if (tb != null)
                            {
                                tb.Text = txt;
                                tb.Foreground = B(cl);
                            }
                        }
                    });
                }
                catch { }
            });
        }

        void RunTask(string name, System.Windows.Controls.Button btn)
        {
            btn.IsEnabled = false;
            btn.Content = "...";
            string ename = System.Uri.EscapeDataString(name);
            string url = "http://localhost:4200/run-task?name=" + ename;
            Task.Run(delegate
            {
                try
                {
                    var wc = new WebClient();
                    wc.DownloadString(url);
                }
                catch { }
                Dispatcher.Invoke(delegate
                {
                    btn.IsEnabled = true;
                    btn.Content = ">";
                });
            });
        }

        void SwitchToFastest(System.Windows.Controls.Button btn)
        {
            btn.IsEnabled = false;
            btn.Content = "...";
            string url = "http://localhost:4200/switch-fastest";
            Task.Run(delegate
            {
                try
                {
                    var wc = new WebClient();
                    wc.Encoding = System.Text.Encoding.UTF8;
                    string json = wc.DownloadString(url);
                    Dispatcher.Invoke(delegate
                    {
                        var ser = new JavaScriptSerializer();
                        var r = ser.Deserialize<Dictionary<string, object>>(json);
                        bool ok = r.ContainsKey("ok") && (bool)r["ok"];
                        if (ok)
                        {
                            string model = r.ContainsKey("model") ? r["model"].ToString() : "?";
                            string lat = r.ContainsKey("latency") ? r["latency"].ToString() : "";
                            btn.Content = "✓ " + model + " (" + lat + ")";
                            btn.Background = B("#22C55E");
                            // 2秒后恢复
                            var t = new DispatcherTimer();
                            t.Interval = TimeSpan.FromSeconds(2);
                            t.Tick += delegate { t.Stop(); btn.Content = "⚡"; btn.Background = B("#3B82F6"); btn.IsEnabled = true; };
                            t.Start();
                        }
                        else
                        {
                            btn.Content = "✗";
                            btn.Background = B("#EF4444");
                            var t = new DispatcherTimer();
                            t.Interval = TimeSpan.FromSeconds(2);
                            t.Tick += delegate { t.Stop(); btn.Content = "⚡"; btn.Background = B("#3B82F6"); btn.IsEnabled = true; };
                            t.Start();
                        }
                    });
                }
                catch
                {
                    Dispatcher.Invoke(delegate
                    {
                        btn.Content = "✗";
                        btn.Background = B("#EF4444");
                        var t = new DispatcherTimer();
                        t.Interval = TimeSpan.FromSeconds(2);
                        t.Tick += delegate { t.Stop(); btn.Content = "⚡"; btn.Background = B("#3B82F6"); btn.IsEnabled = true; };
                        t.Start();
                    });
                }
            });
        }

        void SwitchToModel(string modelName, System.Windows.Controls.Button btn)
        {
            btn.IsEnabled = false;
            btn.Content = "...";
            string ename = System.Uri.EscapeDataString(modelName);
            string url = "http://localhost:4200/switch-model?name=" + ename;
            Task.Run(delegate
            {
                try
                {
                    var wc = new WebClient();
                    wc.Encoding = System.Text.Encoding.UTF8;
                    string json = wc.DownloadString(url);
                    Dispatcher.Invoke(delegate
                    {
                        var ser = new JavaScriptSerializer();
                        var r = ser.Deserialize<Dictionary<string, object>>(json);
                        bool ok = r.ContainsKey("ok") && (bool)r["ok"];
                        if (ok)
                        {
                            btn.Content = "✓";
                            btn.Background = B("#22C55E");
                            var t = new DispatcherTimer();
                            t.Interval = TimeSpan.FromSeconds(1.5);
                            t.Tick += delegate { t.Stop(); btn.Content = "⚡"; btn.Background = B("#3B82F6"); btn.IsEnabled = true; };
                            t.Start();
                        }
                        else
                        {
                            btn.Content = "✗";
                            btn.Background = B("#EF4444");
                            var t = new DispatcherTimer();
                            t.Interval = TimeSpan.FromSeconds(1.5);
                            t.Tick += delegate { t.Stop(); btn.Content = "⚡"; btn.Background = B("#3B82F6"); btn.IsEnabled = true; };
                            t.Start();
                        }
                    });
                }
                catch
                {
                    Dispatcher.Invoke(delegate
                    {
                        btn.Content = "✗";
                        btn.Background = B("#EF4444");
                        var t = new DispatcherTimer();
                        t.Interval = TimeSpan.FromSeconds(1.5);
                        t.Tick += delegate { t.Stop(); btn.Content = "⚡"; btn.Background = B("#3B82F6"); btn.IsEnabled = true; };
                        t.Start();
                    });
                }
            });
        }

        static System.Windows.Controls.Button SmallBtn(string text)
        {
            var b = new System.Windows.Controls.Button();
            b.Content = text;
            b.FontSize = 9;
            b.Padding = new Thickness(3, 0, 3, 0);
            b.Margin = new Thickness(4, 0, 0, 0);
            b.Background = B("#1E293B");
            b.Foreground = B("#94A3B8");
            b.BorderBrush = B("#334155");
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        static SolidColorBrush B(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6) hex = "FF" + hex;
            return new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16)));
        }

        static TextBlock Lbl(string t, double sz, string hex, bool bold = false)
        {
            return new TextBlock
            {
                Text = t, FontSize = sz, Foreground = B(hex),
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Margin = new Thickness(0, 3, 0, 1),
            };
        }

        static TextBlock Row(string t, string hex)
        {
            return new TextBlock
            {
                Text = t, FontSize = 10, Foreground = B(hex),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Margin = new Thickness(2, 1, 0, 1),
            };
        }

        static System.Windows.Shapes.Rectangle Sep()
        {
            return new System.Windows.Shapes.Rectangle
            {
                Height = 1, Fill = B("#334155"),
                Margin = new Thickness(0, 3, 0, 3),
            };
        }
    }
}
