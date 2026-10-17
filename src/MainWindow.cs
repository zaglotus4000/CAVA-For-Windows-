using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CavaWin
{
    /// <summary>
    /// Окно визуализатора: скруглённая рамка в теме «капучино», перетаскивание
    /// за любую точку фона, ресайз за края (WM_NCHITTEST), контекстное меню,
    /// галочки которого всегда отражают текущие настройки.
    /// </summary>
    public sealed class MainWindow : Window
    {
        private const double Edge = 6; // ширина зоны ресайза, px

        private readonly Config _cfg;
        private readonly DspEngine _dsp;
        private readonly BarsVisual _visual = new BarsVisual();
        private readonly TextBlock _status = new TextBlock();
        private readonly TextBlock _title = new TextBlock();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private readonly Border _chrome = new Border();
        private DateTime _last = DateTime.UtcNow;
        private DateTime _lastSave = DateTime.MinValue;
        private TrayIcon _tray;
        private HwndSource _source;
        private ContextMenu _menu;
        private readonly List<Action> _menuRefresh = new List<Action>();
        private MenuItem _fullItem;    // «Во весь экран»
        private MenuItem _restoreItem; // «Свернуть в исходный размер»
        private bool _full;            // окно растянуто на весь монитор
        // Геометрия окна до входа в полноэкранный режим.
        private double _savedLeft, _savedTop, _savedWidth, _savedHeight;

        /// <summary>Тестовый режим: не перезаписывать пользовательский конфиг позицией окна.</summary>
        public bool TestMode { get; set; }

        public MainWindow(Config cfg, DspEngine dsp)
        {
            _cfg = cfg;
            _dsp = dsp;

            Title = "CavaWin";
            Width = cfg.Width;
            Height = cfg.Height;
            if (cfg.WindowLeft >= 0 && cfg.WindowTop >= 0)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = cfg.WindowLeft;
                Top = cfg.WindowTop;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = true;
            Topmost = cfg.Topmost;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Opacity = Math.Clamp(cfg.Opacity, 0.2, 1.0);

            BuildChrome();
            BuildMenu();
            _visual.SetMode(_cfg.Mode);
            MouseLeftButtonDown += Window_MouseLeftButtonDown;
            PreviewKeyDown += OnPreviewKeyDown;
            StateChanged += (s, e) => RefreshMenu();

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += OnClosing;
            LocationChanged += (s, e) => SavePos();
            SizeChanged += (s, e) => SavePos();
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(1000.0 / Math.Max(15, cfg.Framerate), 8, 60));
            _timer.Tick += OnFrame;
        }

        private void BuildChrome()
        {
            _chrome.CornerRadius = new CornerRadius(10);
            _chrome.BorderThickness = new Thickness(1);
            _chrome.Padding = new Thickness(Edge);
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition());
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_visual, 0);
            var statusHost = new Border { Child = _status, Padding = new Thickness(10, 0, 10, 6) };
            _status.FontSize = 11;
            Grid.SetRow(statusHost, 1);
            panel.Children.Add(_visual);
            panel.Children.Add(statusHost);
            _chrome.Child = panel;
            Content = _chrome;

            _title.Text = "CavaWin";
            _title.FontSize = 11;
            _title.Opacity = 0.55;
            _title.VerticalAlignment = VerticalAlignment.Top;
            _title.HorizontalAlignment = HorizontalAlignment.Left;
            _title.Margin = new Thickness(12, 6, 12, 0);
        }

        private void ApplyTheme()
        {
            string theme = string.IsNullOrEmpty(_cfg.Theme) ? "cappuccino" : _cfg.Theme;
            switch (theme)
            {
                case "neon":
                    _chrome.Background = new SolidColorBrush(Color.FromArgb(0x2A, 0x0A, 0x0A, 0x14));
                    _chrome.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x7A, 0x9A, 0xD0));
                    _title.Foreground = new SolidColorBrush(Color.FromArgb(0x90, 0xD0, 0xE0, 0xFF));
                    _status.Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 90, 90));
                    _visual.SetDim(Color.FromArgb(38, 255, 255, 255));
                    _visual.SetPeak(Color.FromArgb(210, 255, 255, 255));
                    _visual.SetColors("#2BFF9B", "#4D7CFF"); // неон: зелёный → синий
                    break;

                case "dracula":
                    _chrome.Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x28, 0x2A, 0x36)); // #282A36
                    _chrome.BorderBrush = new SolidColorBrush(Color.FromRgb(0x62, 0x72, 0xA4));       // #6272A4
                    _title.Foreground = new SolidColorBrush(Color.FromArgb(0xD0, 0xBD, 0x93, 0xF9)); // #BD93F9
                    _status.Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0x79, 0xC6)); // #FF79C6
                    _visual.SetDim(Color.FromArgb(0x5A, 0x44, 0x47, 0x5A));                          // #44475A
                    _visual.SetPeak(Color.FromArgb(0xE6, 0xF8, 0xF8, 0xF2));                         // #F8F8F2
                    _visual.SetColors("#50FA7B", "#BD93F9"); // зелёный → фиолетовый
                    break;

                default: // cappuccino
                    _chrome.Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xF1, 0xE4));
                    _chrome.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xC3, 0xA5));
                    _title.Foreground = new SolidColorBrush(Color.FromRgb(0x6F, 0x5B, 0x41));
                    _status.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x6B, 0x5C));
                    _visual.SetDim(Color.FromArgb(60, 0x8C, 0x6D, 0x51));
                    _visual.SetPeak(Color.FromArgb(230, 0x5A, 0x46, 0x32));
                    _visual.SetColors("#8A5A33", "#DFA35F"); // эспрессо → карамель
                    break;
            }

            // Пользовательский градиент из конфига имеет приоритет, только если он
            // отличается от старых значений по умолчанию (#2BFF9B → #4D7CFF).
            if (_cfg.ColorFrom != "#2BFF9B" || _cfg.ColorTo != "#4D7CFF")
                _visual.SetColors(_cfg.ColorFrom, _cfg.ColorTo);
        }
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _source = (HwndSource)PresentationSource.FromDependencyObject(this);
            _source.AddHook(WndProc);
            ApplyTheme();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int wmNcHitTest = 0x0084;
            if (msg == wmNcHitTest && !_full) // в полном экране края не ресайзят
            {
                // lParam — экранные физические px курсора; PointFromScreen переводит их
                // в локальные координаты окна (учитывает и позицию окна, и DPI монитора).
                var pt = ClientPointFromScreenDip(GetSignedX(lParam), GetSignedY(lParam));
                double w = ActualWidth, h = ActualHeight;
                bool top = pt.Y <= Edge, bottom = pt.Y >= h - Edge;
                bool left = pt.X <= Edge, right = pt.X >= w - Edge;
                if (top && left) { handled = true; return new IntPtr(13); }
                if (top && right) { handled = true; return new IntPtr(14); }
                if (bottom && left) { handled = true; return new IntPtr(16); }
                if (bottom && right) { handled = true; return new IntPtr(17); }
                if (top) { handled = true; return new IntPtr(12); }
                if (bottom) { handled = true; return new IntPtr(15); }
                if (left) { handled = true; return new IntPtr(10); }
                if (right) { handled = true; return new IntPtr(11); }
            }
            return IntPtr.Zero;
        }

        private static int GetSignedX(IntPtr l) => (short)((long)l & 0xFFFF);
        private static int GetSignedY(IntPtr l) => (short)(((long)l >> 16) & 0xFFFF);

        /// <summary>Экранные физические пиксели → точка внутри окна в DIP.</summary>
        private Point ClientPointFromScreenDip(double sx, double sy)
        {
            try { return PointFromScreen(new Point(sx, sy)); }
            catch
            {
                // запасной путь: делим на DPI окна и вычитаем его позицию
                double k = 1.0;
                try
                {
                    var ct = _source?.CompositionTarget;
                    if (ct != null) k = ct.TransformToDevice.M11;
                }
                catch { }
                if (k <= 0) k = 1.0;
                return new Point(sx / k - Left, sy / k - Top);
            }
        }

        /// <summary>Перетаскивание окна за любую точку фона/рамки.</summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_full) return; // в полном экране окно не перетаскиваем
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Win32.HideFromAltTab(hwnd);
            _tray = new TrayIcon("CavaWin — визуализатор звука");
            _tray.RestoreRequested += () =>
            {
                try { Show(); WindowState = WindowState.Normal; Activate(); } catch { }
            };
            _tray.ExitRequested += () => Close();
            _timer.Start();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _timer.Stop();
            _tray?.Dispose();
            _source?.RemoveHook(WndProc);
            if (_menu != null) _menu.IsOpen = false;
            SavePos(true); // финальное сохранение позиции/размера
        }

        private void SavePos(bool force = false)
        {
            if (TestMode || _full || WindowState != WindowState.Normal) return;
            try
            {
                _cfg.Width = (int)Math.Max(200, ActualWidth);
                _cfg.Height = (int)Math.Max(80, ActualHeight);
                if (Left >= -16000 && Left <= 16000) _cfg.WindowLeft = (int)Left;
                if (Top >= -16000 && Top <= 16000) _cfg.WindowTop = (int)Top;
                var now = DateTime.UtcNow;
                if (!force && (now - _lastSave).TotalMilliseconds < 400) return;
                _lastSave = now;
                _cfg.Save();
            }
            catch { }
        }

        private void BuildMenu()
        {
            _menu = new ContextMenu();
            _menuRefresh.Clear();

            _menu.Items.Add(_title);

            _menu.Items.Add(new Separator());

            AddFlag("Поверх всех окон (Topmost)", c => c.Topmost, (c, v) => { c.Topmost = v; Topmost = v; });
            AddFlag("Сквозь клики (не мешает мыши)", c => c.ClickThrough, (c, v) => ApplyClickThrough(v));
            AddFlag("Шапки-пики над полосами", c => c.Peaks, (c, v) => { _cfg.Peaks = v; _visual.SetPeaksVisible(v); });

            // Стиль визуализации (режимы в духе оригинального cava).
            var style = new MenuItem { Header = "Стиль" };
            AddRadio(style, "Полосы", "bars", c => c.Mode, (c, v) => _cfg.Mode = v);
            AddRadio(style, "Monstercat-волны", "monstercat", c => c.Mode, (c, v) => _cfg.Mode = v);
            AddRadio(style, "Зеркало", "mirror", c => c.Mode, (c, v) => _cfg.Mode = v);
            AddRadio(style, "Осциллограф", "wave", c => c.Mode, (c, v) => _cfg.Mode = v);
            AddRadio(style, "LED-сегменты", "led", c => c.Mode, (c, v) => _cfg.Mode = v);
            _menu.Items.Add(style);

            // Полноэкранный режим: галочка отражает текущее состояние, а второй
            // пункт — возврат к исходному размеру (окно безрамочное, у него нет
            // своей кнопки «свернуть», поэтому выход делаем через меню).
            _fullItem = new MenuItem { Header = "Во весь экран" };
            _fullItem.Click += (s, e) =>
            {
                if (_full || WindowState != WindowState.Normal) ExitFullscreen();
                else EnterFullscreen();
            };
            _menu.Items.Add(_fullItem);

            _restoreItem = new MenuItem { Header = "Свернуть в исходный размер" };
            _restoreItem.Click += (s, e) => ExitFullscreen();
            _menu.Items.Add(_restoreItem);

            _menuRefresh.Add(SyncFullscreen);
            SyncFullscreen();

            var theme = new MenuItem { Header = "Тема" };
            AddRadio(theme, "Капучино", "cappuccino", c => c.Theme, (c, v) => { c.Theme = v; ApplyTheme(); });
            AddRadio(theme, "Неон", "neon", c => c.Theme, (c, v) => { c.Theme = v; ApplyTheme(); });
            AddRadio(theme, "Дракула", "dracula", c => c.Theme, (c, v) => { c.Theme = v; ApplyTheme(); });
            _menu.Items.Add(theme);

            var bars = new MenuItem { Header = "Полос" };
            for (int n = 16; n <= 96; n += 16)
            {
                int count = n;
                AddRadio(bars, count.ToString(), count, c => c.Bars, (c, nv) => _cfg.Bars = nv);
            }
            _menu.Items.Add(bars);

            var sens = new MenuItem { Header = "Чувствительность" };
            foreach (var v in new double[] { 0.5, 0.75, 1.0, 1.5, 2.0 })
            {
                double val = v;
                AddRadio(sens, "x" + val.ToString("0.##"), val, c => c.Sensitivity, (c, nv) => _cfg.Sensitivity = nv);
            }
            _menu.Items.Add(sens);

            var fps = new MenuItem { Header = "FPS" };
            foreach (var v in new int[] { 30, 45, 60, 90 })
            {
                int val = v;
                AddRadio(fps, val.ToString(), val, c => c.Framerate, (c, nv) => _cfg.Framerate = nv);
            }
            _menu.Items.Add(fps);

            _menu.Items.Add(new Separator());
            var quit = new MenuItem { Header = "Выход" };
            quit.Click += (s, e) => Close();
            _menu.Items.Add(quit);

            ContextMenu = _menu;
        }

        /// <summary>Радио-пункт: галочка всегда на текущем значении конфига.</summary>
        private void AddRadio<T>(MenuItem parent, string header, T value,
                                 Func<Config, T> get, Action<Config, T> set)
        {
            var mi = new MenuItem { Header = header, IsCheckable = true };
            void Sync() { mi.IsChecked = EqualityComparer<T>.Default.Equals(get(_cfg), value); }
            mi.Click += (s, e) => { set(_cfg, value); ApplyConfig(); SavePos(); RefreshMenu(); };
            _menuRefresh.Add(Sync);
            Sync();
            parent.Items.Add(mi);
        }

        private void AddFlag(string header, Func<Config, bool> get, Action<Config, bool> set)
        {
            var mi = new MenuItem { IsCheckable = true, Header = header };
            void Sync() { mi.IsChecked = get(_cfg); }
            mi.Checked += (s, e) => { set(_cfg, true); ApplyConfig(); SavePos(); };
            mi.Unchecked += (s, e) => { set(_cfg, false); ApplyConfig(); SavePos(); };
            _menuRefresh.Add(Sync);
            Sync();
            _menu.Items.Add(mi);
        }

        /// <summary>Синхронизирует все галочки меню с текущим конфигом.</summary>
        private void RefreshMenu()
        {
            foreach (var sync in _menuRefresh) sync();
        }

        private void ApplyConfig()
        {
            ApplyTheme();
            _visual.SetMode(_cfg.Mode);
            _visual.SetPeaksVisible(_cfg.Peaks);
            Opacity = Math.Clamp(_cfg.Opacity, 0.2, 1.0);
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(1000.0 / Math.Max(15, _cfg.Framerate), 8, 60));
            ApplyClickThrough(_cfg.ClickThrough);
            if (!string.IsNullOrEmpty(_status.Text)) _status.Text = "";
        }

        private void ApplyClickThrough(bool on)
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                if (on) ex |= Win32.WS_EX_TRANSPARENT;
                else ex &= ~Win32.WS_EX_TRANSPARENT;
                Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex);
            }
            catch { }
        }

        /// <summary>Растянуть окно на весь монитор, запомнив исходную геометрию.</summary>
        private void EnterFullscreen()
        {
            if (_full) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;

            // Монитор, на котором сейчас окно (учитывает несколько экранов).
            var mi = new Win32.MONITORINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>()
            };
            if (!Win32.GetMonitorInfo(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), ref mi))
                return;

            // Физические пиксели монитора → DIP: масштаб берём у самого окна
            // (манифест PerMonitorV2, DPI окна совпадает с DPI его монитора).
            double k = 1.0;
            try
            {
                var ct = _source?.CompositionTarget;
                if (ct != null) k = ct.TransformToDevice.M11;
            }
            catch { }
            if (k <= 0) k = 1.0;

            _savedLeft = Left; _savedTop = Top;
            _savedWidth = ActualWidth; _savedHeight = ActualHeight;
            _full = true; // до смены геометрии — чтобы SavePos не записал её в конфиг
            _chrome.CornerRadius = new CornerRadius(0);
            Left = mi.Monitor.Left / k;
            Top = mi.Monitor.Top / k;
            Width = (mi.Monitor.Right - mi.Monitor.Left) / k;
            Height = (mi.Monitor.Bottom - mi.Monitor.Top) / k;
            RefreshMenu();
        }

        /// <summary>Вернуть окно к исходному размеру и позиции (выход из полного экрана).</summary>
        private void ExitFullscreen()
        {
            bool wasFull = _full;
            _full = false;
            _chrome.CornerRadius = new CornerRadius(10);
            if (wasFull)
            {
                Left = _savedLeft; Top = _savedTop;
                Width = _savedWidth; Height = _savedHeight;
            }
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            RefreshMenu();
        }

        /// <summary>Синхронизирует пункты «Во весь экран» / «Свернуть в исходный размер».</summary>
        private void SyncFullscreen()
        {
            bool full = _full || WindowState == WindowState.Maximized;
            if (_fullItem != null) _fullItem.IsChecked = full;
            if (_restoreItem != null) _restoreItem.IsEnabled = full;
        }

        /// <summary>F11 — переключить полный экран, Esc — выйти из него.</summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                if (_full || WindowState != WindowState.Normal) ExitFullscreen();
                else EnterFullscreen();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && (_full || WindowState != WindowState.Normal))
            {
                ExitFullscreen();
                e.Handled = true;
            }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double dt = (now - _last).TotalSeconds;
            _last = now;
            if (dt <= 0 || dt > 0.5) dt = 0.016;

            var bars = _dsp.Update(_cfg, dt);
            var wave = _cfg.Mode == "wave" ? _dsp.Waveform((int)_visual.ActualWidth / 4) : null;
            _visual.SetData(bars, _cfg.Peaks ? _dsp.Peaks : null, wave);

            string st = _dsp.Status;
            if (!string.IsNullOrEmpty(st)) _status.Text = st;
            else if (_status.Text.Length > 0) _status.Text = "";
        }
    }
}