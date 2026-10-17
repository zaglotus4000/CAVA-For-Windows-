using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CavaWin
{
    /// <summary>Иконка в системном трее (Shell_NotifyIcon).</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const int WM_TRAY = 0x0400 + 1001;
        private const int NIM_ADD = 0, NIM_DELETE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize, hWnd, uID, uFlags, uCallbackMessage;
            public int hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string tip;
            public int state, stateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string info;
            public int timeout, version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string infoTitle;
            public int infoFlags;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hObject);

        private readonly TraySink _sink;
        private NOTIFYICONDATA _nid;
        private IntPtr _icon;
        private bool _added;

        public event Action RestoreRequested;
        public event Action ExitRequested;

        public TrayIcon(string tip)
        {
            _icon = MakeIcon();
            _sink = new TraySink(WM_TRAY,
                () => RestoreRequested?.Invoke(),
                () => ExitRequested?.Invoke());
            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = TraySink.ToInt32(_sink.Handle),
                uID = 1,
                uFlags = 0x01 | 0x02 | 0x04, // MESSAGE | ICON | TIP
                uCallbackMessage = WM_TRAY,
                hIcon = TraySink.ToInt32(_icon),
                tip = tip
            };
            _added = Shell_NotifyIcon(NIM_ADD, ref _nid);
        }

        public static IntPtr MakeIcon()
        {
            try
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    var pen = new Pen(new SolidColorBrush(Colors.White), 2);
                    dc.DrawRoundedRectangle(null, pen, new Rect(2, 2, 28, 28), 6, 6);
                    var b1 = new SolidColorBrush(Color.FromRgb(0x2B, 0xFF, 0x9B));
                    var b2 = new SolidColorBrush(Color.FromRgb(0x4D, 0x7C, 0xFF));
                    dc.DrawRoundedRectangle(b1, null, new Rect(7, 16, 4, 10), 1.5, 1.5);
                    dc.DrawRoundedRectangle(b2, null, new Rect(14, 9, 4, 17), 1.5, 1.5);
                    dc.DrawRoundedRectangle(b1, null, new Rect(21, 13, 4, 13), 1.5, 1.5);
                }
                var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(visual);
                bmp.Freeze();

                int stride = 32 * 4;
                var pixels = new byte[stride * 32];
                bmp.CopyPixels(pixels, stride, 0);

                IntPtr color = CreateBitmap(32, 32, 1, 32, pixels);
                var mask = new byte[32 * 4]; // нулевая маска — используем альфу
                IntPtr mono = CreateBitmap(32, 32, 1, 1, mask);
                var info = new ICONINFO { fIcon = 1, xHotspot = 0, yHotspot = 0, hbmMask = mono, hbmColor = color };
                IntPtr icon = CreateIconIndirect(ref info);
                if (color != IntPtr.Zero) DeleteObject(color);
                if (mono != IntPtr.Zero) DeleteObject(mono);
                return icon;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public int fIcon;
            public int xHotspot, yHotspot;
            public IntPtr hbmMask, hbmColor;
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        public void Dispose()
        {
            if (_added)
            {
                try { Shell_NotifyIcon(NIM_DELETE, ref _nid); } catch { }
                _added = false;
            }
            if (_icon != IntPtr.Zero)
            {
                try { DestroyIcon(_icon); } catch { }
                _icon = IntPtr.Zero;
            }
            _sink?.Dispose();
        }
    }
}