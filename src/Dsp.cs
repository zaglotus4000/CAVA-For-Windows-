using System;

namespace CavaWin
{
    /// <summary>
    /// DSP-ядро в стиле cava: кольцевой буфер, FFT, логарифмические полосы,
    /// гравитационное падение, горизонтальное сглаживание (monstercat), автогейн.
    /// </summary>
    public sealed class DspEngine
    {
        public const int FftSize = 4096;

        private readonly object _lock = new object();
        private readonly float[] _ring = new float[FftSize * 4];
        private int _ringPos;
        private int _ringFill;

        public int SampleRate { get; set; } = 48000;
        public string Status { get; set; } = "запуск аудио...";
        public double SawSignal { get; private set; }

        private readonly float[] _re = new float[FftSize];
        private readonly float[] _im = new float[FftSize];
        private readonly float[] _hann = new float[FftSize];
        private readonly int[] _rev = new int[FftSize];
        private readonly float[] _mag = new float[FftSize / 2];
        private readonly float[] _snap = new float[FftSize]; // окно отсчётов для FFT
        private float[] _sm = Array.Empty<float>();          // копия полос для monstercat-сглаживания

        private int _bars = -1;
        private float[] _cur = Array.Empty<float>();
        private float[] _peaks = Array.Empty<float>();
        private float[] _tmp = Array.Empty<float>();
        private int[] _bandLo = Array.Empty<int>();
        private int[] _bandHi = Array.Empty<int>();
        private int _barsSampleRate = -1;
        private int _barsFmin = -1;
        private int _barsFmax = -1;
        private double _autoMult = 1.0;
        private bool _autoStarted;

        public DspEngine()
        {
            for (int i = 0; i < FftSize; i++)
            {
                _hann[i] = 0.5f * (1.0f - (float)Math.Cos(2 * Math.PI * i / (FftSize - 1)));
                int r = 0, x = i;
                for (int b = 0; b < 12; b++) { r = (r << 1) | (x & 1); x >>= 1; }
                _rev[i] = r;
            }
        }

        public void WriteBlock(float[] samples, int count)
        {
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    float a = samples[i];
                    if (a < 0) a = -a;
                    if (a > SawSignal) SawSignal = a;
                    _ring[_ringPos] = samples[i];
                    _ringPos = (_ringPos + 1) % _ring.Length;
                    if (_ringFill < _ring.Length) _ringFill++;
                }
            }
        }

        public float[] Peaks => _peaks;

        private float _waveNorm = 1f;

        /// <summary>
        /// Последние отсчёты сигнала, нормированные в [-1..1] — для стиля «осциллограф».
        /// Возвращает null, пока буфер ещё не заполнен.
        /// </summary>
        public float[] Waveform(int count)
        {
            lock (_lock)
            {
                if (_ringFill < FftSize) return null;
                count = Math.Max(32, Math.Min(count, 512));
                int window = FftSize / 2;          // окно ~43 мс при 48 кГц
                int stride = Math.Max(1, window / count);
                count = window / stride;
                var res = new float[count];
                float max = 0.0001f;
                for (int i = 0; i < count; i++)
                {
                    int idx = _ringPos - window + i * stride;
                    if (idx < 0) idx += _ring.Length;
                    float s = _ring[idx];
                    res[i] = s;
                    float a = s < 0 ? -s : s;
                    if (a > max) max = a;
                }
                // плавная нормировка громкости: волна занимает большую часть высоты
                _waveNorm += (max - _waveNorm) * 0.08f;
                if (_waveNorm < 0.002f) _waveNorm = 0.002f;
                float k = 1f / _waveNorm;
                for (int i = 0; i < count; i++) res[i] = Math.Max(-1f, Math.Min(1f, res[i] * k));
                return res;
            }
        }

        private float _dc;

        private void SetBars(int bars, int sampleRate, int fmin, int fmax)
        {
            if (bars == _bars && sampleRate == _barsSampleRate && fmin == _barsFmin && fmax == _barsFmax) return;
            lock (_lock)
            {
                _bars = bars;
                _barsSampleRate = sampleRate;
                _barsFmin = fmin;
                _barsFmax = fmax;
                _cur = new float[bars];
                _peaks = new float[bars];
                _tmp = new float[bars];
                _sm = new float[bars];
                _bandLo = new int[bars];
                _bandHi = new int[bars];
                RecomputeBands();
            }
        }

        private void RecomputeBands()
        {
            int n = FftSize / 2 - 1;
            double binHz = (double)_barsSampleRate / FftSize;
            double ratio = (double)Math.Max(_barsFmax, _barsFmin + 1) / _barsFmin;
            for (int i = 0; i < _bars; i++)
            {
                double f0 = _barsFmin * Math.Pow(ratio, (double)i / _bars);
                double f1 = _barsFmin * Math.Pow(ratio, (double)(i + 1) / _bars);
                int b0 = Math.Max(1, (int)Math.Round(f0 / binHz));
                int b1 = Math.Max(b0 + 1, (int)Math.Round(f1 / binHz));
                if (b1 > n) b1 = n;
                if (b0 >= b1) { b0 = Math.Min(b0, n - 1); b1 = b0 + 1; }
                _bandLo[i] = b0;
                _bandHi[i] = b1;
            }
        }

        public float[] Update(Config cfg, double dt)
        {
            SetBars(cfg.Bars, SampleRate, cfg.MinFreq, cfg.MaxFreq);
            if (_bars <= 0) return Array.Empty<float>();
            lock (_lock)
            {
                if (_ringFill < FftSize) return _cur;
                int len = _ring.Length;
                for (int i = 0; i < FftSize; i++)
                {
                    int idx = _ringPos - FftSize + i;
                    if (idx < 0) idx += len;
                    float s = _ring[idx];
                    _dc = 0.995f * _dc + 0.005f * s;
                    _snap[i] = (s - _dc) * _hann[i];
                }
            }

            Fft();

            int half = FftSize / 2;
            for (int i = 0; i < half; i++)
                _mag[i] = (float)Math.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) / (FftSize / 4f);

            double maxAvg = 0;
            for (int b = 0; b < _bars; b++)
            {
                double sum = 0; int cnt = 0;
                int lo = Math.Min(_bandLo[b], half - 1), hi = Math.Min(_bandHi[b], half);
                for (int k = lo; k < hi; k++) { sum += _mag[k]; cnt++; }
                if (cnt == 0) { sum = _mag[lo]; cnt = 1; }
                double avg = sum / cnt;
                if (avg > maxAvg) maxAvg = avg;
                _tmp[b] = (float)avg;
            }

            // автогейн: держим максимум полос около 0.72 полной высоты
            if (maxAvg > 0.0008)
            {
                double want = Math.Clamp(0.72 / maxAvg, 0.02, 80);
                if (!_autoStarted) { _autoMult = want; _autoStarted = true; }
                else _autoMult += (want - _autoMult) * 0.05;
            }

            float fall = (float)(Math.Max(0.05, cfg.Gravity) * 2.2 * dt);
            float peakFall = (float)(Math.Max(0.05, cfg.Gravity) * 0.5 * dt);
            float dh = (float)Math.Max(0.0, Math.Min(0.45, 0.05 * cfg.Smoothing));

            // monstercat-сглаживание: перетекание значения к соседним полосам
            if (dh > 0.001)
            {
                Array.Copy(_tmp, _sm, _bars);
                for (int b = 0; b < _bars; b++)
                {
                    double v = _sm[b];
                    for (int k = 1; k <= 3 && b + k < _bars; k++)
                    {
                        double dec = v / (1.0 + k * cfg.Smoothing);
                        if (dec > _tmp[b + k]) _tmp[b + k] = (float)dec;
                    }
                    for (int k = 1; k <= 3 && b - k >= 0; k++)
                    {
                        double dec = _sm[b] / (1.0 + k * cfg.Smoothing);
                        if (dec > _tmp[b - k]) _tmp[b - k] = (float)dec;
                    }
                }
            }

            for (int b = 0; b < _bars; b++)
            {
                double x = _tmp[b] * _autoMult * cfg.Sensitivity;
                double v = x < 0.02 ? 0 : Math.Pow(x, 0.6);
                v = Math.Min(1.25, v);
                float cur = _cur[b];
                if (v >= cur) cur = (float)v;
                else { cur -= fall; if (cur < v) cur = (float)v; if (cur < 0) cur = 0; }
                _cur[b] = cur;

                // шапки-пики: медленно сползают вниз
                float pk = _peaks[b];
                if (cur >= pk) _peaks[b] = cur;
                else { pk -= peakFall; _peaks[b] = pk < cur ? cur : pk; }
            }
            return _cur;
        }

        private void Fft()
        {
            int n = FftSize;
            for (int i = 0; i < n; i++)
            {
                int j = _rev[i];
                _re[i] = _snap[j];
                _im[i] = 0f;
            }
            for (int size = 2; size <= n; size <<= 1)
            {
                int half = size >> 1;
                double step = 2 * Math.PI / size;
                for (int i = 0; i < n; i += size)
                {
                    for (int j = 0; j < half; j++)
                    {
                        double ang = step * j;
                        float wr = (float)Math.Cos(ang), wi = -(float)Math.Sin(ang);
                        int a = i + j, b2 = a + half;
                        float xr = _re[b2] * wr - _im[b2] * wi;
                        float xi = _re[b2] * wi + _im[b2] * wr;
                        _re[b2] = _re[a] - xr; _im[b2] = _im[a] - xi;
                        _re[a] += xr; _im[a] += xi;
                    }
                }
            }
        }
    }
}