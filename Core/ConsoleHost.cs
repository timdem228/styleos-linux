using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace StyleOS
{
    /// <summary>
    /// Everything that touches the real terminal. Linux build: no Win32 calls at all.
    /// ANSI/VT is native on every Linux terminal (and in Termux), so there is nothing to
    /// switch on; "fullscreen" is an XTWINOPS request that terminals which allow it honour
    /// and everything else silently ignores.
    /// </summary>
    public static class ConsoleHost
    {
        // Kept alive for the lifetime of the process - disposing them unregisters the handler.
        private static PosixSignalRegistration _sigTerm, _sigHup;
        private static bool _handlersInstalled;

        /// <summary>True when running inside Windows Terminal (WSL). Kept for compatibility.</summary>
        public static bool IsWindowsTerminal =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"));

        public static bool MaximizedNatively { get; private set; }

        public static int Width
        {
            get { try { return Math.Max(20, Console.WindowWidth); } catch { return 80; } }
        }

        public static int Height
        {
            get { try { return Math.Max(10, Console.WindowHeight); } catch { return 25; } }
        }

        /// <summary>
        /// UTF-8 in and out, plus signal handling. With interactive = true (a real session)
        /// Ctrl+C is delivered as a key, the way the Windows console did it: the line editor
        /// discards the line and nano/top/watch see the key, instead of the whole OS dying.
        /// </summary>
        public static void EnableAnsi(bool interactive = true)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            try
            {
                // Some terminals reject setting the input encoding (redirected stdin) - not fatal.
                Console.InputEncoding = System.Text.Encoding.UTF8;
            }
            catch { }

            if (!interactive) return;

            try
            {
                if (!Console.IsInputRedirected) Console.TreatControlCAsInput = true;
            }
            catch { }

            InstallSignalHandlers();
        }

        /// <summary>
        /// SIGINT (only reaches us while a host program runs in the foreground) cancels that
        /// program, not StyleOS. SIGTERM / SIGHUP put the terminal colours and cursor back
        /// before the process goes away.
        /// </summary>
        public static void InstallSignalHandlers()
        {
            if (_handlersInstalled) return;
            _handlersInstalled = true;

            try
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    Kernel.CancelRequested = true;
                };
            }
            catch { }

            try
            {
                _sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => Reset());
                _sigHup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, ctx => Reset());
            }
            catch { }
        }

        /// <summary>
        /// Ask the terminal to maximize itself (CSI 9;1 t). Termux is always fullscreen
        /// already, and redirected output has no window at all.
        /// </summary>
        public static void GoFullscreen()
        {
            MaximizedNatively = false;
            if (Console.IsOutputRedirected) return;

            if (!HostPlatform.IsTermux)
            {
                try
                {
                    Console.Write("\x1b[9;1t");
                    Console.Out.Flush();
                    Thread.Sleep(60); // let the terminal apply the resize before we measure
                }
                catch { }
            }
            else MaximizedNatively = true;

            try { Console.Clear(); } catch { }
        }

        /// <summary>Cursor move that never throws when the window is smaller than expected.</summary>
        public static void SetCursor(int left, int top)
        {
            try
            {
                int x = Math.Max(0, Math.Min(left, Width - 1));
                int y = Math.Max(0, Math.Min(top, Height - 1));
                Console.SetCursorPosition(x, y);
            }
            catch { }
        }

        public static void SetCursorVisible(bool visible)
        {
            try { Console.CursorVisible = visible; } catch { }
        }

        public static void WriteAt(int left, int top, string text)
        {
            SetCursor(left, top);
            int room = Math.Max(0, Width - left);
            if (text.Length > room) text = text.Substring(0, room);
            Console.Write(text);
        }

        /// <summary>
        /// Line that fills the row but stops one column short: writing the very last cell
        /// makes the terminal auto-wrap and scrolls the whole screen (this is what made the
        /// nano / bug reporter status bars jump).
        /// </summary>
        public static string FullLine(string text)
        {
            int w = Math.Max(1, Width - 1);
            if (text.Length > w) return text.Substring(0, w);
            return text.PadRight(w);
        }

        public static void Reset()
        {
            try
            {
                Console.ResetColor();
                Console.CursorVisible = true;
                if (!Console.IsOutputRedirected) Console.Write("\x1b[0m");
            }
            catch { }
        }

        /// <summary>
        /// True when a key is waiting AND input hasn't been redirected. Console.KeyAvailable
        /// throws InvalidOperationException on redirected stdin (piped input, some launchers,
        /// non-interactive shells) - every polling loop in the app goes through this instead
        /// of calling it directly, so those environments degrade gracefully instead of crashing.
        /// </summary>
        public static bool KeyReady()
        {
            try { return Console.KeyAvailable; }
            catch { return false; }
        }
    }
}
