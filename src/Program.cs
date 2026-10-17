using System;
using System.Windows;
using System.Windows.Threading;

namespace CavaWin
{
    public static class Program
    {
        private static readonly System.Collections.Generic.List<string> _trace =
            new System.Collections.Generic.List<string>();

        public static void Log(string line)
        {
            lock (_trace) _trace.Add(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + line);
        }

        internal static void FlushResult(string headline)
        {
            lock (_trace)
            {
                string text = headline + "\r\n" + string.Join("\r\n", _trace);
                try
                {
                    string p1 = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                        AppContext.BaseDirectory, "..", "..", "..", "..", "test-result.txt"));
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p1));
                    System.IO.File.WriteAllText(p1, text);
                }
                catch { }
                try
                {
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(AppContext.BaseDirectory, "test-result.txt"), text);
                }
                catch { }
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            bool testMode = args.Length > 0 && args[0] == "--test";
            Log("старт, testMode=" + testMode);

            DspEngine dsp = null;
            WasapiLoopback capture = null;
            try
            {
                var cfg = Config.Load();
                if (testMode) { cfg.Opacity = 1.0; cfg.Topmost = true; }
                Log("конфиг загружен");

                dsp = new DspEngine();
                capture = new WasapiLoopback(dsp);
                capture.Start();
                Log("аудио-поток запущен");

                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    Log("DISPATCHER EXC: " + e.Exception);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    FlushResult("CRASH: " + e.ExceptionObject);

                var window = new MainWindow(cfg, dsp) { TestMode = testMode };
                Log("окно создано");
                if (testMode)
                {
                    window.Left = 40; window.Top = 40;
                    window.Width = 500; window.Height = 220;

                    var progress = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    progress.Tick += (s3, e3) =>
                        FlushResult("PROGRESS saw=" + dsp.SawSignal.ToString("0.0000"));
                    window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(progress.Start));

                    var closer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6.5) };
                    closer.Tick += (s2, e2) => { Log("таймер: закрываю окно"); closer.Stop(); window.Close(); };
                    window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(closer.Start));
                    window.Loaded += (s, e) => Log("Loaded");
                }

                app.Run(window);
                Log("app.Run завершён нормально");
            }
            catch (Exception ex)
            {
                Log("EXC: " + ex);
            }
            finally
            {
                try { capture?.Stop(); } catch { }
                if (testMode)
                    FlushResult((dsp != null && dsp.SawSignal > 0.001 ? "OK " : "FAIL ") +
                                "saw=" + (dsp != null ? dsp.SawSignal.ToString("0.0000") : "n/a"));
            }
        }
    }
}