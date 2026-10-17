using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CavaWin
{
    internal static class WaveSubFormat
    {
        public static readonly Guid IeeeFloat = new Guid("00000003-0000-0010-8000-00AA00389B71");
        public static readonly Guid Pcm = new Guid("00000001-0000-0010-8000-00AA00389B71");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WAVEFORMATEX
    {
        public short wFormatTag;
        public short nChannels;
        public int nSamplesPerSec;
        public int nAvgBytesPerSec;
        public short nBlockAlign;
        public short wBitsPerSample;
        public short cbSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WAVEFORMATEXTENSIBLE
    {
        public WAVEFORMATEX wfx;
        public short wValidBitsPerSample;
        public int dwChannelMask;
        public Guid SubFormat;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        void RegisterEndpointNotificationCallback(IntPtr client);
        void UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        void OpenPropertyStore(int access, out IntPtr store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int state);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodic, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out int bufferSize);
        long GetStreamLatency();
        void GetCurrentPadding(out int padding);
        void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        void ReleaseBuffer(int frames);
        void GetNextPacketSize(out int frames);
    }

    /// <summary>
    /// Захват системного звука (WASAPI loopback): слушает то, что играет Windows,
    /// без микрофона. Отдельный поток, автоперезапуск при смене устройства.
    /// </summary>
    internal sealed class WasapiLoopback : IDisposable
    {
        private const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        private const int AUDCLNT_BUFFERFLAGS_SILENT = 0x02;
        private const int CLSCTX_ALL = 0x17;
        private static readonly Guid IidAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        private static readonly Guid IidCaptureClient = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

        private readonly DspEngine _dsp;
        private Thread _thread;
        private volatile bool _running;
        private float[] _src = new float[1 << 16];
        private short[] _src16 = new short[1 << 17];
        private int[] _src32 = new int[1 << 16];
        private float[] _mono = new float[1 << 15];

        public WasapiLoopback(DspEngine dsp) { _dsp = dsp; }

        public void Start()
        {
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "wasapi-loopback" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _thread?.Join(2500); } catch { }
        }

        public void Dispose() => Stop();

        private void Run()
        {
            bool first = true;
            while (_running)
            {
                try
                {
                    CaptureOnce();
                }
                catch (Exception ex)
                {
                    _dsp.Status = first ? "нет звука: " + ex.Message : "аудио перезапуск...";
                    Program.Log("WASAPI " + (first ? "ошибка: " : "перезапуск: ") + ex.Message);
                    first = false;
                }
                for (int i = 0; i < 40 && _running; i++) Thread.Sleep(50);
            }
        }

        private void CaptureOnce()
        {
            try { CoInitializeEx(IntPtr.Zero, 0); } catch { }

            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 0 /*eConsole*/, out IMMDevice device);
            object activated;
            var iidClient = IidAudioClient;
            device.Activate(ref iidClient, CLSCTX_ALL, IntPtr.Zero, out activated);
            var client = (IAudioClient)activated;

            client.GetMixFormat(out IntPtr fmtPtr);
            var fmt = Marshal.PtrToStructure<WAVEFORMATEX>(fmtPtr);

            bool isFloat = false, isPcm16 = false, isPcm32 = false;
            if (fmt.wFormatTag == 3) isFloat = true;
            else if (fmt.wFormatTag == 1) { isPcm16 = fmt.wBitsPerSample == 16; isPcm32 = fmt.wBitsPerSample == 32; }
            else if (fmt.wFormatTag == (short)-2 /*EXTENSIBLE*/ && fmt.cbSize >= 22)
            {
                var ext = Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(fmtPtr);
                if (ext.SubFormat == WaveSubFormat.IeeeFloat) isFloat = true;
                else if (ext.SubFormat == WaveSubFormat.Pcm) { isPcm16 = fmt.wBitsPerSample == 16; isPcm32 = fmt.wBitsPerSample == 32; }
            }
            if (!isFloat && !isPcm16 && !isPcm32)
                throw new Exception("формат tag=" + fmt.wFormatTag + ", bits=" + fmt.wBitsPerSample);

            long duration = 1_000_000, zero = 0; // 100 мс в единицах 100 нс
            client.Initialize(0 /*SHARED*/, AUDCLNT_STREAMFLAGS_LOOPBACK, duration, zero, fmtPtr, IntPtr.Zero);

            var iidCapture = IidCaptureClient;
            client.GetService(ref iidCapture, out object svcObj);
            var capture = (IAudioCaptureClient)svcObj;

            _dsp.SampleRate = Math.Max(8000, fmt.nSamplesPerSec);
            int channels = Math.Max(1, (int)fmt.nChannels);
            client.Start();
            _dsp.Status = "";
            Program.Log("WASAPI поток: sr=" + fmt.nSamplesPerSec + " ch=" + fmt.nChannels +
                        " bits=" + fmt.wBitsPerSample + " float=" + isFloat);

            try
            {
                while (_running)
                {
                    capture.GetNextPacketSize(out int packets);
                    while (packets > 0 && _running)
                    {
                        capture.GetBuffer(out IntPtr data, out int frames, out int flags, out long devPos, out long qpcPos);
                        if (frames > 0) ReadFrames(data, frames, channels, flags, isFloat, isPcm16, isPcm32);
                        capture.ReleaseBuffer(frames);
                        capture.GetNextPacketSize(out packets);
                    }
                    Thread.Sleep(4);
                }
            }
            finally
            {
                try { client.Stop(); } catch { }
            }
        }

        private void ReadFrames(IntPtr data, int frames, int channels, int flags,
                                bool isFloat, bool isPcm16, bool isPcm32)
        {
            int count = frames * channels;
            if (_mono.Length < frames) _mono = new float[frames * 2];

            if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0)
            {
                Array.Clear(_mono, 0, frames);
                _dsp.WriteBlock(_mono, frames);
                return;
            }

            if (isFloat)
            {
                if (_src.Length < count) _src = new float[count * 2];
                Marshal.Copy(data, _src, 0, count);
            }
            else if (isPcm16)
            {
                if (_src16.Length < count) _src16 = new short[count * 2];
                Marshal.Copy(data, _src16, 0, count);
                if (_src.Length < count) _src = new float[count * 2];
                for (int i = 0; i < count; i++) _src[i] = _src16[i] / 32768f;
            }
            else
            {
                if (_src32.Length < count) _src32 = new int[count * 2];
                Marshal.Copy(data, _src32, 0, count);
                if (_src.Length < count) _src = new float[count * 2];
                for (int i = 0; i < count; i++) _src[i] = _src32[i] / 2147483648f;
            }

            if (channels == 1)
            {
                Buffer.BlockCopy(_src, 0, _mono, 0, frames * sizeof(float));
            }
            else
            {
                double inv = 1.0 / channels;
                for (int f = 0; f < frames; f++)
                {
                    double acc = 0;
                    for (int c = 0; c < channels; c++) acc += _src[f * channels + c];
                    _mono[f] = (float)(acc * inv);
                }
            }
            _dsp.WriteBlock(_mono, frames);
        }

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr reserved, int coInit);
    }
}