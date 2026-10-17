using System;
using System.Windows;
using System.Windows.Media;

namespace CavaWin
{
    /// <summary>Дополнительные стили отрисовки для BarsVisual (partial-часть).</summary>
    public sealed partial class BarsVisual
    {
        // ── Monstercat-волны: сплошной сглаженный профиль ─────────────────────
        private void DrawMonstercat(DrawingContext dc, double w, double h)
        {
            int n = _values.Length;
            if (n == 0) return;

            double avail = w - _margin * 2;
            double slot = avail / n;
            double baseY = h - 2;

            var pts = new Point[n];
            for (int i = 0; i < n; i++)
            {
                double v = Math.Min(_values[i], 1.0);
                double barH = Math.Max(v <= 0.001 ? 0 : 2, v * (h - 4));
                pts[i] = new Point(_margin + slot * (i + 0.5), baseY - barH);
            }

            var dim = new SolidColorBrush(_dim);
            dc.DrawRoundedRectangle(dim, null,
                new Rect(_margin, baseY - 2, avail, 2), _corner, _corner);

            // заливка от гребня до основания
            var fill = new StreamGeometry();
            using (var ctx = fill.Open())
            {
                ctx.BeginFigure(new Point(pts[0].X, baseY), true, true);
                ctx.LineTo(pts[0], true, false);
                AppendSmoothCurve(ctx, pts);
                ctx.LineTo(new Point(pts[n - 1].X, baseY), true, false);
            }
            fill.Freeze();
            dc.DrawGeometry(VGrad(_from, _to), null, fill);

            // светлая линия по гребню
            var crest = new StreamGeometry();
            using (var ctx = crest.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                AppendSmoothCurve(ctx, pts);
            }
            crest.Freeze();
            var pen = new Pen(new SolidColorBrush(_peak), 1.4)
            {
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            dc.DrawGeometry(null, pen, crest);
        }

        // ── Зеркало: полосы симметрично от центра ─────────────────────────────
        private void DrawMirror(DrawingContext dc, double w, double h)
        {
            int n = _values.Length;
            if (n == 0) return;

            double avail = w - _margin * 2;
            double slot = avail / n;
            double barW = Math.Max(1.5, slot - _gap);
            double cy = h / 2;
            double maxHalf = h / 2 - 4;

            var up = new LinearGradientBrush(_from, _to, new Point(0, 0.5), new Point(0, 0));
            var down = new LinearGradientBrush(_from, _to, new Point(0, 0.5), new Point(0, 1));
            up.Freeze(); down.Freeze();
            var peakBrush = new SolidColorBrush(_peak);
            var dim = new SolidColorBrush(_dim);
            double x = _margin;

            for (int i = 0; i < n; i++)
            {
                double v = Math.Min(_values[i], 1.0);
                double barH = Math.Max(v <= 0.001 ? 0 : 2, v * maxHalf);

                // центральная «призрачная» ось
                dc.DrawRoundedRectangle(dim, null,
                    new Rect(x, cy - 1, barW, 2), _corner, _corner);

                if (barH > 0)
                {
                    dc.DrawRoundedRectangle(up, null,
                        new Rect(x, cy - barH, barW, barH), _corner, _corner);
                    dc.DrawRoundedRectangle(down, null,
                        new Rect(x, cy, barW, barH), _corner, _corner);
                }

                if (_peaks != null && _peaks.Length == n)
                {
                    double pv = Math.Min(_peaks[i], 1.0);
                    if (pv > 0.01)
                    {
                        double ph = pv * maxHalf;
                        dc.DrawRoundedRectangle(peakBrush, null,
                            new Rect(x, cy - ph - 1.6, barW, 1.6), 0.8, 0.8);
                        dc.DrawRoundedRectangle(peakBrush, null,
                            new Rect(x, cy + ph, barW, 1.6), 0.8, 0.8);
                    }
                }
                x += slot;
            }
        }

        // ── Осциллограф: форма волны во времени ───────────────────────────────
        private void DrawWave(DrawingContext dc, double w, double h)
        {
            var s = _wave;
            double midY = h / 2;
            double avail = w - _margin * 2;
            double amp = h / 2 - 5;

            var dim = new SolidColorBrush(_dim);
            dc.DrawRoundedRectangle(dim, null,
                new Rect(_margin, midY - 1, avail, 2), _corner, _corner);

            if (s == null || s.Length < 2) return;

            var pts = new Point[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                double x = _margin + avail * i / (s.Length - 1);
                double y = midY - Math.Max(-1.0, Math.Min(1.0, s[i])) * amp;
                pts[i] = new Point(x, y);
            }

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(pts[0], false, false);
                AppendSmoothCurve(ctx, pts);
            }
            geo.Freeze();

            var grad = new LinearGradientBrush(_from, _to, new Point(0, 0), new Point(1, 0));
            grad.Freeze();
            var pen = new Pen(grad, 2.2)
            {
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            dc.DrawGeometry(null, pen, geo);
        }

        // ── LED: ретро-эквалайзер из сегментов ────────────────────────────────
        private void DrawLed(DrawingContext dc, double w, double h)
        {
            int n = _values.Length;
            if (n == 0) return;

            double avail = w - _margin * 2;
            double slot = avail / n;
            double barW = Math.Max(2, slot - _gap);
            const double segH = 5, segGap = 2;
            int segs = (int)Math.Floor((h - 6) / (segH + segGap));
            if (segs < 1) return;

            // ── кэш: кисти пересоздаются только при смене темы или размера окна ──
            if (_ledSegBrushes.Length != segs || _ledDimBrush == null || _ledPeakBrush == null)
            {
                var arr = new SolidColorBrush[segs];
                for (int sg = 0; sg < segs; sg++)
                    arr[sg] = new SolidColorBrush(Lerp(_from, _to, (sg + 1) / (double)segs));
                for (int sg = 0; sg < segs; sg++) arr[sg].Freeze();
                _ledSegBrushes = arr;
                var dim = new SolidColorBrush(_dim); dim.Freeze(); _ledDimBrush = dim;
                var pk = new SolidColorBrush(_peak); pk.Freeze(); _ledPeakBrush = pk;
                _ledGridBrush = null; // цвет подложки сменился — сетку пересобираем
            }
            var segBrush = _ledSegBrushes;
            var peakBrush = _ledPeakBrush;

            // ── «тлеющая» сетка: DrawingBrush-плитка (одна операция заливки) ─────
            // Плитка = один сегмент с зазором; Viewport привязан к (_margin, y0),
            // поэтому клетки сетки гарантированно совпадают с позициями сегментов
            // по всей высоте окна.
            double gridH = segs * (segH + segGap) - segGap;
            double y0 = h - 3 - gridH;
            if (_ledGridBrush == null || _ledGridN != n ||
                Math.Abs(_ledGridW - w) > 0.5 || Math.Abs(_ledGridH - h) > 0.5 ||
                Math.Abs(_ledGridSlot - slot) > 0.2)
            {
                var tile = new System.Windows.Media.DrawingGroup();
                using (var gdc = tile.Open())
                    gdc.DrawRectangle(_ledDimBrush, null, new Rect(0, 0, barW, segH));
                var b = new DrawingBrush
                {
                    Drawing = tile,
                    TileMode = TileMode.Tile,
                    ViewportUnits = BrushMappingMode.Absolute,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(_margin, y0, slot, segH + segGap),
                    Viewbox = new Rect(0, 0, slot, segH + segGap)
                };
                b.Freeze();
                _ledGridBrush = b;
                _ledGridN = n; _ledGridW = w; _ledGridH = h; _ledGridSlot = slot;
            }
            dc.DrawRectangle(_ledGridBrush, null, new Rect(_margin, y0, avail, gridH));

            // ── горящие сегменты + пики: плоские прямоугольники ─────────────────
            // Индекс sg = 0 — нижний сегмент колонки; сетка покрывает нижние
            // gridRows строк, горящие сегменты рисуются поверх неё сверху.
            float[] pks = _ctlPeakVals;
            bool hasPeaks = _cfgPeaksOn && pks != null && pks.Length == n;
            double x = _margin;
            for (int i = 0; i < n; i++, x += slot)
            {
                double v = _values[i] > 1 ? 1 : (_values[i] < 0 ? 0 : _values[i]);
                int lit = (int)(v * segs + 0.4);
                for (int sg = 0; sg < lit; sg++)
                {
                    double y = h - 3 - (sg + 1) * segH - sg * segGap;
                    dc.DrawRectangle(segBrush[sg], null, new Rect(x, y, barW, segH));
                }

                if (hasPeaks)
                {
                    double pv = pks[i] > 1 ? 1 : (pks[i] < 0 ? 0 : pks[i]);
                    int pkSeg = (int)Math.Round(pv * segs) - 1;
                    if (pkSeg >= 0 && pkSeg < segs)
                    {
                        double py = h - 3 - (pkSeg + 1) * segH - pkSeg * segGap;
                        dc.DrawRectangle(peakBrush, null, new Rect(x, py, barW, segH));
                    }
                }
            }
        }

        // ── Помощники ──────────────────────────────────────────────────────────
        private LinearGradientBrush VGrad(Color from, Color to)
        {
            var b = new LinearGradientBrush(from, to, new Point(0, 1), new Point(0, 0));
            b.Freeze();
            return b;
        }

        /// <summary>Гладкая кривая через точки (квадратичные Безье по средним точкам).</summary>
        private static void AppendSmoothCurve(StreamGeometryContext ctx, Point[] pts)
        {
            if (pts.Length == 2)
            {
                ctx.LineTo(pts[1], true, false);
                return;
            }
            for (int i = 1; i < pts.Length - 1; i++)
            {
                var mid = new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2);
                ctx.QuadraticBezierTo(pts[i], mid, true, false);
            }
            ctx.LineTo(pts[pts.Length - 1], true, false);
        }

        private static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (byte)Math.Round(a.A + (b.A - a.A) * t),
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }
    }
}