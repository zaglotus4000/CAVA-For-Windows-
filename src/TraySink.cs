using System;
using System.Runtime.InteropServices;

namespace CavaWin
{
    /// <summary>Скрытое окно-приёмник сообщений трея (клик — восстановить, ПКМ — меню выхода).</summary>
    internal sealed class TraySink : IDisposable
    {
        private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public int style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern short RegisterClassW(ref WNDCLASS wc);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string cls, string name,
            int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProcW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string name);

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_APP_EXIT = 0x0400 + 2001;
        private const int WS_POPUP = unchecked((int)0x80000000);

        public IntPtr Handle { get; }
        private readonly WndProcDelegate _proc;
        private readonly Action _restore;
        private readonly Action _exit;
        private static WNDCLASS _keepAlive;
        private bool _disposed;

        public TraySink(int callbackMsg, Action restore, Action exit)
        {
            _restore = restore;
            _exit = exit;
            _proc = Proc;
            string cls = "CavaWinSink_" + Guid.NewGuid().ToString("N");
            _keepAlive.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc);
            _keepAlive.lpszClassName = cls;
            _keepAlive.hInstance = GetModuleHandle(null);
            RegisterClassW(ref _keepAlive);
            Handle = CreateWindowEx(0, cls, "", WS_POPUP, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, _keepAlive.hInstance, IntPtr.Zero);
        }

        public static int ToInt32(IntPtr p) => unchecked((int)(long)p);

        private IntPtr Proc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_LBUTTONUP) { try { _restore?.Invoke(); } catch { } }
            else if (msg == WM_RBUTTONUP) { try { PostMessage(hwnd, WM_APP_EXIT, IntPtr.Zero, IntPtr.Zero); _exit?.Invoke(); } catch { } }
            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Handle != IntPtr.Zero) { try { DestroyWindow(Handle); } catch { } }
        }
    }
}