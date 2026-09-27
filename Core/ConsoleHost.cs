using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace StyleOS
{
    /// <summary>
    /// Everything that touches the real terminal window.
    /// Contains the Windows 11 maximize fix: on Win11 the default host is Windows Terminal,
    /// where GetConsoleWindow() returns a hidden pseudo-console window, so the old
    /// ShowWindow(SW_MAXIMIZE) call silently did nothing.
    /// </summary>
    public static class ConsoleHost
    {
        private const int STD_OUTPUT_HANDLE = -11;
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
        private const int SW_MAXIMIZE = 3;
        private const int SW_SHOW = 5;
        private const uint GA_ROOTOWNER = 3;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>True when we are hosted by Windows Terminal (default on Windows 11).</summary>
        public static bool IsWindowsTerminal =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_PROFILE_ID"));

        public static bool MaximizedNatively { get; private set; }

        public static int Width
        {
            get { try { return Math.Max(20, Console.WindowWidth); } catch { return 80; } }
        }

        public static int Height
        {
            get { try { return Math.Max(10, Console.WindowHeight); } catch { return 25; } }
        }

        /// <summary>Enable ANSI/VT sequences. Safe to call on any platform.</summary>
        public static void EnableAnsi()
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch { }

            try
            {
                // Some terminals reject setting the input encoding (redirected stdin) - not fatal.
                Console.InputEncoding = System.Text.Encoding.UTF8;
            }
            catch { }

            if (!Kernel.IsWindows) return;

            try
            {
                IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle != IntPtr.Zero && GetConsoleMode(handle, out uint mode))
                    SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING | ENABLE_PROCESSED_OUTPUT);
            }
            catch { }
        }

        /// <summary>
        /// Make the terminal take the whole screen. Tries every strategy that exists,
        /// because no single one works on both conhost (Win10) and Windows Terminal (Win11).
        /// </summary>
        public static void GoFullscreen()
        {
            if (!Kernel.IsWindows)
            {
                TryGrowBuffer();
                return;
            }

            MaximizedNatively = false;

            // 1) Classic conhost: the console window is a real, visible top level window.
            try
            {
                IntPtr hwnd = GetConsoleWindow();
                if (hwnd != IntPtr.Zero)
                {
                    IntPtr owner = GetAncestor(hwnd, GA_ROOTOWNER);
                    if (owner != IntPtr.Zero) hwnd = owner;

                    if (IsWindowVisible(hwnd))
                    {
                        ShowWindow(hwnd, SW_SHOW);
                        ShowWindow(hwnd, SW_MAXIMIZE);
                        SetForegroundWindow(hwnd);
                        MaximizedNatively = IsZoomed(hwnd);
                    }
                }
            }
            catch { }

            // 2) Windows Terminal / ConPTY: the window handle above is hidden, so ask the
            //    terminal itself over VT. CSI 9;3t = maximize (XTWINOPS). Unsupported hosts
            //    ignore it instead of printing garbage, since VT processing is on by now.
            if (!MaximizedNatively)
            {
                try
                {
                    Console.Write("\x1b[9;3t");
                    Console.Out.Flush();
                    Thread.Sleep(60); // let the host apply the resize before we measure
                }
                catch { }
            }

            // 3) Last resort: grow the text area to the largest size the host allows.
            TryGrowBuffer();

            try { Console.Clear(); } catch { }
        }

        private static void TryGrowBuffer()
        {
            try
            {
                int w = Console.LargestWindowWidth;
                int h = Console.LargestWindowHeight;
                if (w <= 0 || h <= 0) return;

                if (Kernel.IsWindows)
                {
                    // Buffer must never be smaller than the window, order matters.
                    if (Console.BufferWidth < w || Console.BufferHeight < h)
                        Console.SetBufferSize(Math.Max(Console.BufferWidth, w), Math.Max(Console.BufferHeight, h));
                }

                if (Console.WindowWidth < w || Console.WindowHeight < h)
                    Console.SetWindowSize(w, h);
            }
            catch { }
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
