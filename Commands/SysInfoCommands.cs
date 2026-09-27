using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace StyleOS
{
    public static class SysInfoCommands
    {
        private static (string Cpu, string Gpu, long TotalMemMb, long FreeMemMb)? _cache;

        private static readonly string[] Logo =
        {
            @"    _______    ",
            @"   /  ___  \   ",
            @"  |  (__ \_|   ",
            @"   \___  \     ",
            @"  |\___)  |    ",
            @"   \_____/     ",
            @"               ",
            @"   StyleOS     "
        };

        /// <summary>fastfetch / neofetch. The kernel line always reports StyleOS Core.</summary>
        public static void Fetch(string toolName)
        {
            var hw = GetHardwareInfo();
            string user = Kernel.CurrentUser?.Username ?? "root";
            string host = ShellEnv.HostName;
            string title = $"{user}@{host}";

            var rows = new List<(string Label, string Value)>
            {
                ("", title),
                ("", new string('-', title.Length)),
                ("OS", $"{Kernel.DistroName} {Kernel.Version} ({Kernel.Arch})"),
                ("Kernel", Kernel.KernelString),
                ("Host", HostModel()),
                ("Uptime", FormatUptime(Kernel.Uptime)),
                ("Shell", "styleshell 1.1"),
                ("Terminal", ConsoleHost.IsWindowsTerminal ? "Windows Terminal" : "console"),
                ("Resolution", $"{ConsoleHost.Width}x{ConsoleHost.Height} cells"),
                ("Packages", $"{PackageManager.InstalledCount()} (pacman)"),
                ("CPU", $"{hw.Cpu} ({Environment.ProcessorCount})"),
                ("GPU", hw.Gpu),
                ("Memory", $"{hw.TotalMemMb - hw.FreeMemMb}MiB / {hw.TotalMemMb}MiB"),
                ("Runtime", RuntimeInformation.FrameworkDescription),
                ("Locale", System.Globalization.CultureInfo.CurrentCulture.Name)
            };

            Console.WriteLine();
            int lineCount = Math.Max(Logo.Length, rows.Count);

            for (int i = 0; i < lineCount; i++)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(i < Logo.Length ? Logo[i].PadRight(18) : new string(' ', 18));

                if (i < rows.Count)
                {
                    var row = rows[i];
                    if (row.Label.Length == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(row.Value);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Write(row.Label + ": ");
                        Console.ForegroundColor = Kernel.Config.DefaultTextColor;
                        Console.WriteLine(row.Value);
                    }
                }
                else Console.WriteLine();
            }

            Console.ResetColor();
            Console.WriteLine();
            PrintPalette();
            Console.WriteLine();
        }

        private static void PrintPalette()
        {
            Console.Write(new string(' ', 18));
            ConsoleColor[] colors =
            {
                ConsoleColor.Black, ConsoleColor.Red, ConsoleColor.Green, ConsoleColor.Yellow,
                ConsoleColor.Blue, ConsoleColor.Magenta, ConsoleColor.Cyan, ConsoleColor.White
            };
            foreach (var c in colors)
            {
                Console.BackgroundColor = c;
                Console.Write("   ");
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        private static string HostModel()
        {
            try
            {
                string model = WmiValue("Win32_ComputerSystem", "Model");
                return string.IsNullOrWhiteSpace(model) ? Environment.MachineName : model;
            }
            catch { return Environment.MachineName; }
        }

        public static string FormatUptime(TimeSpan t)
        {
            var parts = new List<string>();
            if (t.Days > 0) parts.Add($"{t.Days} day{(t.Days == 1 ? "" : "s")}");
            if (t.Hours > 0) parts.Add($"{t.Hours} hour{(t.Hours == 1 ? "" : "s")}");
            parts.Add($"{t.Minutes} min{(t.Minutes == 1 ? "" : "s")}");
            return string.Join(", ", parts);
        }

        public static (string Cpu, string Gpu, long TotalMemMb, long FreeMemMb) GetHardwareInfo()
        {
            if (_cache.HasValue)
            {
                var c = _cache.Value;
                return (c.Cpu, c.Gpu, c.TotalMemMb, FreeMemoryMb(c.TotalMemMb));
            }

            string cpu = $"{Environment.ProcessorCount}-core CPU";
            string gpu = "Standard Graphics";
            long totalMb = 0;

            try
            {
                var info = GC.GetGCMemoryInfo();
                totalMb = info.TotalAvailableMemoryBytes / (1024 * 1024);
            }
            catch { }

            if (Kernel.IsWindows)
            {
                try
                {
                    string c = WmiValue("Win32_Processor", "Name");
                    if (!string.IsNullOrWhiteSpace(c)) cpu = c.Trim();

                    string g = WmiValue("Win32_VideoController", "Name");
                    if (!string.IsNullOrWhiteSpace(g)) gpu = g.Trim();

                    string mem = WmiValue("Win32_OperatingSystem", "TotalVisibleMemorySize");
                    if (long.TryParse(mem, out long kb)) totalMb = kb / 1024;
                }
                catch (Exception ex) { SystemLogger.Log("SYSINFO", "WMI unavailable: " + ex.Message); }
            }

            if (totalMb <= 0) totalMb = 1;
            _cache = (cpu, gpu, totalMb, 0);
            return (cpu, gpu, totalMb, FreeMemoryMb(totalMb));
        }

        private static long FreeMemoryMb(long totalMb)
        {
            if (Kernel.IsWindows)
            {
                string free = WmiValue("Win32_OperatingSystem", "FreePhysicalMemory");
                if (long.TryParse(free, out long kb)) return kb / 1024;
            }
            try
            {
                long used = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                return Math.Max(0, totalMb - used);
            }
            catch { return totalMb / 2; }
        }

        private static string WmiValue(string wmiClass, string property)
        {
            if (!Kernel.IsWindows) return "";
            try
            {
#pragma warning disable CA1416
                using (var searcher = new System.Management.ManagementObjectSearcher($"select {property} from {wmiClass}"))
                {
                    foreach (var item in searcher.Get())
                    {
                        var value = item[property];
                        if (value != null) return value.ToString();
                    }
                }
#pragma warning restore CA1416
            }
            catch { }
            return "";
        }

        public static void Uname(List<string> args)
        {
            bool all = Io.HasFlag(args, "-a", "--all");
            if (args.Count == 0) { Console.WriteLine("StyleOS"); return; }

            var parts = new List<string>();
            if (all || Io.HasFlag(args, "-s")) parts.Add("StyleOS");
            if (all || Io.HasFlag(args, "-n")) parts.Add(ShellEnv.HostName);
            if (all || Io.HasFlag(args, "-r")) parts.Add($"{Kernel.Version}-styleos");
            if (all || Io.HasFlag(args, "-v")) parts.Add($"#{Kernel.Version} {Kernel.CoreName}");
            if (all || Io.HasFlag(args, "-m") || Io.HasFlag(args, "-p")) parts.Add(Kernel.Arch);
            if (all || Io.HasFlag(args, "-o")) parts.Add(Kernel.DistroName);

            Console.WriteLine(parts.Count == 0 ? "StyleOS" : string.Join(" ", parts));
        }

        public static void Uptime()
        {
            var t = Kernel.Uptime;
            Console.WriteLine($" {DateTime.Now:HH:mm:ss} up {FormatUptime(t)},  1 user,  load average: 0.00, 0.01, 0.05");
        }

        public static void Free(List<string> args)
        {
            var hw = GetHardwareInfo();
            bool human = Io.HasFlag(args, "-h");
            bool giga = Io.HasFlag(args, "-g");

            long total = hw.TotalMemMb, free = hw.FreeMemMb, used = Math.Max(0, total - free);

            string Fmt(long mb)
            {
                if (giga) return $"{mb / 1024.0:0.0}G";
                if (human) return mb > 1024 ? $"{mb / 1024.0:0.0}Gi" : $"{mb}Mi";
                return (mb * 1024).ToString();
            }

            Console.WriteLine($"{"",-10}{"total",12}{"used",12}{"free",12}{"shared",12}{"available",12}");
            Console.WriteLine($"{"Mem:",-10}{Fmt(total),12}{Fmt(used),12}{Fmt(free),12}{Fmt(0),12}{Fmt(free),12}");
            Console.WriteLine($"{"Swap:",-10}{Fmt(total / 2),12}{Fmt(0),12}{Fmt(total / 2),12}");
        }

        public static void Lscpu()
        {
            var hw = GetHardwareInfo();
            Console.WriteLine($"Architecture:          {Kernel.Arch}");
            Console.WriteLine($"CPU(s):                {Environment.ProcessorCount}");
            Console.WriteLine($"Model name:            {hw.Cpu}");
            Console.WriteLine($"Byte Order:            {(BitConverter.IsLittleEndian ? "Little Endian" : "Big Endian")}");
            Console.WriteLine($"Virtualization:        {(Environment.ProcessorCount > 2 ? "VT-x" : "none")}");
            Console.WriteLine($"Kernel:                {Kernel.KernelString}");
        }

        public static void Top()
        {
            Console.Clear();
            ConsoleHost.SetCursorVisible(false);
            var hw = GetHardwareInfo();

            try
            {
                while (true)
                {
                    var procs = Process.GetProcesses()
                        .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
                        .Take(Math.Max(5, ConsoleHost.Height - 8))
                        .ToList();

                    ConsoleHost.SetCursor(0, 0);
                    Console.WriteLine(ConsoleHost.FullLine($"top - {DateTime.Now:HH:mm:ss} up {FormatUptime(Kernel.Uptime)},  load average: 0.00, 0.01, 0.05"));
                    Console.WriteLine(ConsoleHost.FullLine($"Tasks: {procs.Count} shown, {Process.GetProcesses().Length} total"));
                    Console.WriteLine(ConsoleHost.FullLine($"MiB Mem : {hw.TotalMemMb} total, {hw.FreeMemMb} free, {hw.TotalMemMb - hw.FreeMemMb} used"));

                    Console.BackgroundColor = ConsoleColor.Gray;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.WriteLine(ConsoleHost.FullLine($"{"PID",7} {"USER",-12} {"VIRT",10} {"RES",10} {"S",2} {"%MEM",6}  COMMAND"));
                    Console.ResetColor();

                    string user = Kernel.CurrentUser?.Username ?? "root";
                    foreach (var p in procs)
                    {
                        try
                        {
                            long res = p.WorkingSet64 / 1024 / 1024;
                            double memPercent = hw.TotalMemMb == 0 ? 0 : res * 100.0 / hw.TotalMemMb;
                            Console.WriteLine(ConsoleHost.FullLine(
                                $"{p.Id,7} {user,-12} {p.VirtualMemorySize64 / 1024 / 1024 + "M",10} {res + "M",10} {"S",2} {memPercent,6:0.0}  {p.ProcessName}"));
                        }
                        catch { }
                    }

                    Console.WriteLine(ConsoleHost.FullLine(""));
                    Console.Write(ConsoleHost.FullLine("Press q to quit, any other key to refresh"));

                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape) break;
                }
            }
            finally
            {
                ConsoleHost.SetCursorVisible(true);
                Console.Clear();
            }
        }

        public static void Ps(List<string> args)
        {
            bool all = Io.HasFlag(args, "-e", "-a") || args.Contains("aux");
            string user = Kernel.CurrentUser?.Username ?? "root";

            Console.WriteLine($"{"PID",7} {"TTY",-8} {"TIME",10} CMD");

            IEnumerable<Process> list = all
                ? Process.GetProcesses().OrderBy(p => p.Id)
                : new[] { Process.GetCurrentProcess() };

            foreach (var p in list.Take(all ? 200 : 5))
            {
                try
                {
                    string time = "00:00:00";
                    try { time = (DateTime.Now - p.StartTime).ToString(@"hh\:mm\:ss"); } catch { }
                    Console.WriteLine($"{p.Id,7} {"pts/0",-8} {time,10} {p.ProcessName}");
                }
                catch { }
            }

            if (!all) Console.WriteLine($"{Environment.ProcessId + 1,7} {"pts/0",-8} {"00:00:00",10} styleshell");
        }

        public static void Dmesg()
        {
            foreach (var line in SystemLogger.Tail(200))
            {
                Console.ForegroundColor = line.Contains("[PANIC]") ? ConsoleColor.Red
                    : line.Contains("[BOOT]") ? ConsoleColor.Cyan
                    : ConsoleColor.DarkGray;
                Console.WriteLine(line);
            }
            Console.ResetColor();
        }

        public static void Env()
        {
            foreach (var kv in ShellEnv.Vars.OrderBy(k => k.Key))
                Console.WriteLine($"{kv.Key}={kv.Value}");
        }

        public static void Id()
        {
            var u = Kernel.CurrentUser;
            if (u == null) { Io.Error("id", "no session"); return; }
            string group = u.IsRoot ? "root" : "users";
            Console.WriteLine($"uid={u.Uid}({u.Username}) gid={u.Uid}({group}) groups={u.Uid}({group})");
        }

        public static void Date(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count > 0 && operands[0].StartsWith("+"))
            {
                string format = operands[0].Substring(1)
                    .Replace("%Y", "yyyy").Replace("%m", "MM").Replace("%d", "dd")
                    .Replace("%H", "HH").Replace("%M", "mm").Replace("%S", "ss");
                try { Console.WriteLine(DateTime.Now.ToString(format)); return; }
                catch { }
            }
            Console.WriteLine(DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy"));
        }

        public static void Cal()
        {
            var now = DateTime.Now;
            var first = new DateTime(now.Year, now.Month, 1);
            int days = DateTime.DaysInMonth(now.Year, now.Month);

            string header = $"{now:MMMM yyyy}";
            Console.WriteLine(header.PadLeft(10 + header.Length / 2));
            Console.WriteLine("Mo Tu We Th Fr Sa Su");

            int start = ((int)first.DayOfWeek + 6) % 7;
            var sb = new StringBuilder(new string(' ', start * 3));

            for (int day = 1; day <= days; day++)
            {
                sb.Append(day == now.Day ? $"{("*" + day),2} " : $"{day,2} ");
                if ((start + day) % 7 == 0) { Console.WriteLine(sb.ToString().TrimEnd()); sb.Clear(); }
            }
            if (sb.Length > 0) Console.WriteLine(sb.ToString().TrimEnd());
        }
    }
}
