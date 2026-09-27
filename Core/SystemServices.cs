using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace StyleOS
{
    public class SystemConfig
    {
        public ConsoleColor PromptUserColor { get; set; } = ConsoleColor.Green;
        public ConsoleColor PromptDirColor { get; set; } = ConsoleColor.Blue;
        public ConsoleColor DefaultTextColor { get; set; } = ConsoleColor.White;
        public ConsoleColor DefaultBgColor { get; set; } = ConsoleColor.Black;
        /// <summary>"stable" or "beta" - which release channel pacman follows.</summary>
        public string UpdateChannel { get; set; } = "stable";
        public bool CheckUpdatesOnBoot { get; set; } = true;
        public string Hostname { get; set; } = "";
    }

    public static class ConfigManager
    {
        public static void LoadConfig()
        {
            if (File.Exists(Kernel.ConfigFile))
            {
                try
                {
                    // Deserialize returns null for a file containing "null" - that used to
                    // blow up later with a NullReferenceException on every Config access.
                    var cfg = JsonSerializer.Deserialize<SystemConfig>(File.ReadAllText(Kernel.ConfigFile));
                    Kernel.Config = cfg ?? new SystemConfig();
                }
                catch { Kernel.Config = new SystemConfig(); }
            }
            ApplyTheme();
        }

        public static void SaveConfig()
        {
            try
            {
                File.WriteAllText(Kernel.ConfigFile,
                    JsonSerializer.Serialize(Kernel.Config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { SystemLogger.Log("CONFIG", "Save failed: " + ex.Message); }
        }

        public static void ApplyTheme(bool clear = true)
        {
            try
            {
                Console.ForegroundColor = Kernel.Config.DefaultTextColor;
                Console.BackgroundColor = Kernel.Config.DefaultBgColor;
                if (clear) Console.Clear();
            }
            catch { }
        }
    }

    public static class SystemLogger
    {
        private static readonly object Sync = new object();

        public static void Log(string module, string message)
        {
            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{module}] {message}";
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(Kernel.LogFile, logLine + Environment.NewLine);
                }
            }
            catch { }

            if (Kernel.DebugMode)
            {
                try
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(logLine);
                    Console.ResetColor();
                }
                catch { }
            }
        }

        public static string[] Tail(int count)
        {
            try
            {
                var all = File.ReadAllLines(Kernel.LogFile);
                return all.Skip(Math.Max(0, all.Length - count)).ToArray();
            }
            catch { return new string[0]; }
        }
    }

    public static class CrashHandler
    {
        public static void HandleCrash(Exception ex)
        {
            try { SystemLogger.Log("PANIC", ex.ToString()); } catch { }

            try
            {
                Console.ResetColor();
                Console.Clear();
                Console.BackgroundColor = ConsoleColor.Red;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(ConsoleHost.FullLine(""));
                Console.WriteLine(ConsoleHost.FullLine(" FATAL SYSTEM ERROR "));
                Console.WriteLine(ConsoleHost.FullLine(""));
                Console.ResetColor();

                Console.WriteLine("\nПрограмма вылетела по неизвестной ошибке.");
                Console.WriteLine("Вы можете написать о баге разработчикам!");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"\nDetails: {ex.Message}");
                Console.ResetColor();

                Console.WriteLine("\n[ OK ]   Закрыть ошибку (завершить работу)");
                Console.WriteLine("[ TEXT ] Написать разработчикам (откроется mail.google.com)");

                while (true)
                {
                    Console.Write("\nSelect action (ok/text): ");
                    string act = Console.ReadLine();

                    // ReadLine() returns null when stdin is closed - the old loop spun
                    // forever at 100% CPU in that case.
                    if (act == null) Environment.Exit(1);
                    act = act.ToLower().Trim();

                    if (act == "ok" || act == "") Environment.Exit(1);

                    if (act == "text")
                    {
                        SendReport(ex);
                        Environment.Exit(1);
                    }
                }
            }
            catch
            {
                Environment.Exit(1);
            }
        }

        private static void SendReport(Exception ex)
        {
            try
            {
                string logs = string.Join("\n", SystemLogger.Tail(30));
                string subject = Uri.EscapeDataString("StyleOS Crash Report");
                string body = Uri.EscapeDataString(
                    $"Система крашнулась!\n\nВерсия: {Kernel.Version}\n\nОшибка:\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nПоследние логи:\n{logs}");
                string url = $"https://mail.google.com/mail/?view=cm&fs=1&to=admin@timd.site&su={subject}&body={body}";
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                Thread.Sleep(500);
            }
            catch { }
        }
    }
}
