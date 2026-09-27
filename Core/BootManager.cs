using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StyleOS
{
    public static class BootManager
    {
        /// <summary>The F6 recovery menu shown before the system boots.</summary>
        public static void CheckDebugKey()
        {
            Console.Clear();
            Console.WriteLine($"Starting {Kernel.DistroName} {Kernel.Version}...");
            Console.WriteLine("Press F6 for the Debug Menu...");

            bool pressed = false;
            DateTime start = DateTime.Now;

            // The old loop spun the CPU at 100% for 1.5 seconds on every boot.
            while ((DateTime.Now - start).TotalMilliseconds < 1500)
            {
                if (ConsoleHost.KeyReady())
                {
                    if (Console.ReadKey(true).Key == ConsoleKey.F6) { pressed = true; break; }
                }
                else Thread.Sleep(25);
            }

            if (!pressed) return;

            int selected = 0;
            while (true)
            {
                string[] options =
                {
                    "Boot " + Kernel.DistroName,
                    $"Debug Mode: {(Kernel.DebugMode ? "ON" : "OFF")}",
                    $"Update channel: {Kernel.Config.UpdateChannel}",
                    "Reset theme",
                    "View last boot log"
                };

                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=== SYSTEM RECOVERY & DEBUG MENU ===\n");
                Console.ResetColor();

                for (int i = 0; i < options.Length; i++)
                {
                    if (i == selected)
                    {
                        Console.BackgroundColor = ConsoleColor.White;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($"> {options[i]}");
                        Console.ResetColor();
                    }
                    else Console.WriteLine($"  {options[i]}");
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow) selected = Math.Max(0, selected - 1);
                else if (key == ConsoleKey.DownArrow) selected = Math.Min(options.Length - 1, selected + 1);
                else if (key == ConsoleKey.Escape) return;
                else if (key == ConsoleKey.Enter)
                {
                    switch (selected)
                    {
                        case 0: return;

                        case 1:
                            AuthSystem.Init();
                            Console.Write("\nRoot password required: ");
                            if (AuthSystem.VerifyRootForDebug(AuthSystem.ReadPassword())) Kernel.DebugMode = !Kernel.DebugMode;
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("\nAccess denied.");
                                Console.ResetColor();
                                Thread.Sleep(900);
                            }
                            break;

                        case 2:
                            Kernel.Config.UpdateChannel = Kernel.Config.UpdateChannel == UpdateSystem.BetaChannel
                                ? UpdateSystem.StableChannel
                                : UpdateSystem.BetaChannel;
                            ConfigManager.SaveConfig();
                            break;

                        case 3:
                            Kernel.Config = new SystemConfig();
                            ConfigManager.SaveConfig();
                            Console.WriteLine("\nTheme reset to defaults.");
                            Thread.Sleep(700);
                            break;

                        case 4:
                            Console.Clear();
                            foreach (var line in SystemLogger.Tail(30)) Console.WriteLine(line);
                            Console.WriteLine("\nPress any key...");
                            Console.ReadKey(true);
                            break;
                    }
                }
            }
        }

        public static async Task BootSequence()
        {
            Console.Clear();
            ConsoleHost.SetCursorVisible(true);

            SystemLogger.Log("BOOT", $"{Kernel.KernelString} starting");
            await ModuleSystem.BootLoadAll();
            await Task.Delay(Kernel.DebugMode ? 500 : 200);

            Console.Clear();
            ConsoleHost.SetCursorVisible(false);

            if (Kernel.DebugMode) SystemLogger.Log("BOOT", "Debug mode: skipping the splash screen");
            else DrawCenteredLogo();

            bool booting = true;
            var spinner = Task.Run(() =>
            {
                char[] frames = { '/', '-', '\\', '|' };
                int i = 0;
                while (booting)
                {
                    if (!Kernel.DebugMode)
                    {
                        ConsoleHost.SetCursor(0, ConsoleHost.Height - 1);
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"booting up {frames[i]} ");
                        Console.ResetColor();
                    }
                    i = (i + 1) % frames.Length;
                    Thread.Sleep(120);
                }
            });

            SystemLogger.Log("BOOT", "Initializing network stack...");
            await UpdateSystem.CheckOnBootAsync();

            SystemLogger.Log("BOOT", $"Update check finished ({Kernel.Config.UpdateChannel} channel)");
            await Task.Delay(Kernel.DebugMode ? 700 : 900);

            booting = false;
            await spinner;

            Console.Clear();
            ConsoleHost.SetCursorVisible(true);
        }

        private static void DrawCenteredLogo()
        {
            if (TryDrawImage()) return;

            string[] logo =
            {
                @"  ____  _         _        ____   _____ ",
                @" / ___|| |_ _   _| | ___  / __ \ / ___| ",
                @" \___ \| __| | | | |/ _ \| |  | |\___ \ ",
                @"  ___) | |_| |_| | |  __/| |__| | ___) |",
                @" |____/ \__|\__, |_|\___| \____/ |____/ ",
                @"            |___/                       ",
                "",
                $"v{Kernel.Version}  -  {Kernel.CoreName}"
            };

            int top = Math.Max(0, (ConsoleHost.Height - logo.Length) / 2);
            Console.ForegroundColor = ConsoleColor.Cyan;

            for (int i = 0; i < logo.Length; i++)
            {
                int left = Math.Max(0, (ConsoleHost.Width - logo[i].Length) / 2);
                ConsoleHost.WriteAt(left, top + i, logo[i]);
            }
            Console.ResetColor();
        }

        private static bool TryDrawImage()
        {
            string[] candidates =
            {
                Path.Combine(Kernel.ImageDir, "StyleOS.jpg"),
                Path.Combine(Kernel.ImageDir, "styleos.jpg"),
                Kernel.LogoFile
            };

            string target = null;
            foreach (var file in candidates)
                if (File.Exists(file)) { target = file; break; }

            if (target == null) return false;

            try
            {
#pragma warning disable CA1416
                using var bitmap = new Bitmap(target);

                int maxWidth = Math.Max(10, ConsoleHost.Width - 4);
                int maxHeight = Math.Max(10, ConsoleHost.Height - 6) * 2;

                float ratio = Math.Min((float)maxWidth / bitmap.Width, (float)maxHeight / bitmap.Height);
                int width = Math.Max(1, (int)(bitmap.Width * ratio));
                int height = Math.Max(2, (int)(bitmap.Height * ratio));

                using var resized = new Bitmap(bitmap, new Size(width, height));

                int startLeft = Math.Max(0, (ConsoleHost.Width - width) / 2);
                int startTop = Math.Max(0, (ConsoleHost.Height - height / 2) / 2);

                for (int y = 0; y + 1 < height; y += 2)
                {
                    int row = startTop + y / 2;
                    if (row >= ConsoleHost.Height - 1) break;

                    ConsoleHost.SetCursor(startLeft, row);

                    for (int x = 0; x < width && startLeft + x < ConsoleHost.Width - 1; x++)
                    {
                        Color top = resized.GetPixel(x, y);
                        Color bottom = resized.GetPixel(x, y + 1);

                        bool topClear = top.A < 128 || (top.R < 15 && top.G < 15 && top.B < 15);
                        bool bottomClear = bottom.A < 128 || (bottom.R < 15 && bottom.G < 15 && bottom.B < 15);

                        if (topClear && bottomClear) Console.Write("\x1b[0m ");
                        else if (topClear) Console.Write($"\x1b[38;2;{bottom.R};{bottom.G};{bottom.B}m▄\x1b[0m");
                        else if (bottomClear) Console.Write($"\x1b[38;2;{top.R};{top.G};{top.B}m▀\x1b[0m");
                        else Console.Write($"\x1b[38;2;{top.R};{top.G};{top.B};48;2;{bottom.R};{bottom.G};{bottom.B}m▀\x1b[0m");
                    }
                }
#pragma warning restore CA1416
                return true;
            }
            catch (Exception ex)
            {
                SystemLogger.Log("BOOT", "Splash image failed: " + ex.Message);
                return false;
            }
        }
    }
}
