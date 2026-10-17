using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CavaWin
{
    /// <summary>
    /// Рисует спектр одним из стилей: bars (полосы с пиками), monstercat
    /// (сплошные волны), mirror (зеркало от центра), wave (осциллограф), led (сегменты).
    /// Дополнительные стили — в partial-части Styles.cs.
    /// </summary>
    public sealed partial class BarsVisual : FrameworkElement
    {
        private float[] _values = Array.Empty<float>();
        private float[] _peaks = Array.Empty<float>();
        private float[] _wave; // отсчёты сигнала для стиля «осциллограф»
        private string _mode = "bars";
        private Color _from = Colors.Lime, _to = Colors.CornflowerBlue;
        // кэш кистей LED-стиля: зависит только от цветов/высоты окна, а не от данных
        private SolidColorBrush[] _ledSegBrushes = Array.Empty<SolidColorBrush>();
        private SolidColorBrush _ledDimBrush, _ledPeakBrush;
        // кэш «тлеющей» сетки LED: DrawingBrush-плитка вместо тысяч примитивов
        private System.Windows.Media.DrawingBrush _ledGridBrush;
        private int _ledGridN = -1;
        private double _ledGridH = -1, _ledGridW = -1, _ledGridSlot = -1;
        private bool _cfgPeaksOn = true;    // учёт настройки «Шапки-пики» в LED-стиле
        private Color _dim = Color.FromArgb(38, 255, 255, 255);
        private Color _peak = Color.FromArgb(210, 255, 255, 255);
        private double _gap = 3, _margin = 6, _corner = 2.5;

        public BarsVisual()
        {
            SnapsToDevicePixels = true;
        }

        public void SetColors(string fromHex, string toHex)
        {
            _from = ParseColor(fromHex, Colors.Lime);
            _to = ParseColor(toHex, Colors.CornflowerBlue);
            _ledSegBrushes = Array.Empty<SolidColorBrush>(); // сброс кэша LED
        }

        public void SetDim(Color c) { _dim = c; _ledDimBrush = null; }

        public void SetPeak(Color c) { _peak = c; _ledPeakBrush = null; }

        /// <summary>Включены ли пики (галочка «Шапки-пики») — для LED-стиля.</summary>
        public void SetPeaksVisible(bool on) { _cfgPeaksOn = on; }

        /// <summary>Стиль отрисовки; неизвестное значение откатывается к «bars».</summary>
        public void SetMode(string mode)
        {
            switch (mode)
            {
                case "monstercat":
                case "mirror":
                case "wave":
                case "led":
                    _mode = mode;
                    break;
                default:
                    _mode = "bars";
                    break;
            }
            InvalidateVisual();
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex.Trim()); }
            catch { return fallback; }
        }

        public void SetData(float[] values, float[] peaks, float[] wave = null)
        {
            _values = values ?? Array.Empty<float>();
            _peaks = peaks ?? Array.Empty<float>();
            _wave = wave;
            UpdatePeakControl(); // управляемые пики (для LED-сегментов)
            InvalidateVisual();
        }

        private float[] _ctlPeakVals = Array.Empty<float>(); // пики LED в долях высоты (0..1)
        private float[] _ctlPeakHold = Array.Empty<float>(); // остаток удержания пика, сек
        private DateTime _lastPeakT = DateTime.UtcNow;

        /// <summary>
        /// Пики для LED-стиля: поднимаются вместе с полосой, держатся на месте
        /// полсекунды и затем плавно падают — независимо от громкости кадра.
        /// </summary>
        private void UpdatePeakControl()
        {
            int n = _values.Length;
            if (n != _ctlPeakVals.Length)
            {
                var ov = _ctlPeakVals;
                _ctlPeakVals = new float[n];
                _ctlPeakHold = new float[n];
                int copy = Math.Min(n, ov.Length);
                for (int i = 0; i < copy; i++) _ctlPeakVals[i] = ov[i];
            }

            var now = DateTime.UtcNow;
            double dt = (now - _lastPeakT).TotalSeconds;
            _lastPeakT = now;
            if (dt <= 0 || dt > 0.5) dt = 0.016;
            const double fall = 0.5, hold = 0.5; // сегментов/с и секунд удержания

            for (int i = 0; i < n; i++)
            {
                float v = _values[i];
                if (v > 1) v = 1; else if (v < 0) v = 0;
                if (v >= _ctlPeakVals[i] - 0.001f)
                {
                    _ctlPeakVals[i] = v;      // пик следует за полосой вверх
                    _ctlPeakHold[i] = (float)hold;
                }
                else if (_ctlPeakHold[i] > 0)
                {
                    _ctlPeakHold[i] -= (float)dt; // стоит на месте
                }
                else
                {
                    float p = _ctlPeakVals[i] - (float)(fall * dt);
                    _ctlPeakVals[i] = p < 0 ? 0 : p; // плавно падает
                }
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

            if (w < 10 || h < 10) return;
            switch (_mode)
            {
                case "monstercat": DrawMonstercat(dc, w, h); break;
                case "mirror": DrawMirror(dc, w, h); break;
                case "wave": DrawWave(dc, w, h); break;
                case "led": DrawLed(dc, w, h); break;
                default: DrawBars(dc, w, h); break;
            }
        }

        // ── Полосы (классика cava) ────────────────────────────────────────────
        private void DrawBars(DrawingContext dc, double w, double h)
        {
            int n = _values.Length;
            if (n == 0) return;

            double avail = w - _margin * 2;
            double slot = avail / n;
            double barW = Math.Max(1.5, slot - _gap);
            double x = _margin;

            var brush = new LinearGradientBrush(_from, _to, new Point(0, 1), new Point(0, 0));
            brush.Freeze();
            var peakPen = new SolidColorBrush(_peak);
            var dim = new SolidColorBrush(_dim);

            for (int i = 0; i < n; i++)
            {
                double v = _values[i];
                double barH = Math.Max(v <= 0.001 ? 0 : 2, Math.Min(v, 1.0) * (h - 4));

                // «призрачная» подложка — видно сетку полос при тишине
                dc.DrawRoundedRectangle(dim, null,
                    new Rect(x, h - 3 - 2, barW, 2), _corner, _corner);

                if (barH > 0)
                {
                    dc.DrawRoundedRectangle(brush, null,
                        new Rect(x, h - 2 - barH, barW, barH), _corner, _corner);
                }

                if (_peaks != null && _peaks.Length == n)
                {
                    double pv = Math.Min(_peaks[i], 1.0);
                    if (pv > 0.01)
                    {
                        double py = h - 2 - pv * (h - 4);
                        dc.DrawRoundedRectangle(peakPen, null,
                            new Rect(x, py - 1.5, barW, 1.6), 0.8, 0.8);
                    }
                }
                x += slot;
            }
        }
    }
}