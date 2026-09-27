using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StyleOS
{
    /// <summary>One (simulated) kernel module StyleOS "loads" at boot.</summary>
    public class KernelModule
    {
        public string Name;
        public string Description;
        public int SizeBytes;
        public string[] Requires = Array.Empty<string>();
    }

    /// <summary>
    /// A small, consistent module table used two ways: to print Linux-style dmesg lines
    /// during boot ("[    0.012345] styleos_vfs: ... [ OK ]"), and to back the `modules` /
    /// `lsmod` command afterwards. Load order respects Requires, the way real module
    /// dependencies do.
    /// </summary>
    public static class ModuleSystem
    {
        public static readonly List<KernelModule> Registry = new List<KernelModule>
        {
            new KernelModule { Name = "styleos_core",    Description = "kernel core and process supervisor" , SizeBytes = 186368 },
            new KernelModule { Name = "styleos_vfs",     Description = "virtual filesystem layer",            SizeBytes = 98304,  Requires = new[] { "styleos_core" } },
            new KernelModule { Name = "styleos_tty",     Description = "console and line discipline driver",  SizeBytes = 53248,  Requires = new[] { "styleos_core" } },
            new KernelModule { Name = "styleos_journal", Description = "system logging and journal",          SizeBytes = 40960,  Requires = new[] { "styleos_vfs" } },
            new KernelModule { Name = "styleos_auth",    Description = "user authentication subsystem",       SizeBytes = 73728,  Requires = new[] { "styleos_vfs" } },
            new KernelModule { Name = "styleos_net",     Description = "network stack (tcp/ip, dns)",         SizeBytes = 217088, Requires = new[] { "styleos_core" } },
            new KernelModule { Name = "styleos_pkg",     Description = "package manager backend (pacman)",    SizeBytes = 90112,  Requires = new[] { "styleos_net", "styleos_vfs" } },
            new KernelModule { Name = "styleos_shell",   Description = "interactive command shell",           SizeBytes = 145408, Requires = new[] { "styleos_tty", "styleos_auth" } },
        };

        /// <summary>Names in the order they were "loaded" this boot - what `modules`/`lsmod` shows.</summary>
        public static List<string> Loaded { get; } = new List<string>();

        public static async Task BootLoadAll()
        {
            Loaded.Clear();
            double elapsed = 0;
            var random = new Random();

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"[    0.000000] {Kernel.KernelString} (tim@timd.site) #1 SMP PREEMPT");
            Console.ResetColor();

            foreach (var module in Registry)
            {
                elapsed += random.NextDouble() * 0.035 + 0.004;

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("[");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{elapsed,10:0.000000}");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("] ");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(module.Name);
                Console.ForegroundColor = Kernel.Config.DefaultTextColor;
                Console.Write($": {module.Description}");

                await Task.Delay(Kernel.DebugMode ? 160 : 55);

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" [");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(" OK ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("]");
                Console.ResetColor();

                Loaded.Add(module.Name);
                SystemLogger.Log("KERNEL", $"{module.Name}: module loaded ({module.Description})");
            }
        }

        /// <summary>Reverse-dependency list for the "Used by" column, same idea as real lsmod.</summary>
        private static List<string> UsedBy(string name) =>
            Registry.Where(m => m.Requires.Contains(name)).Select(m => m.Name).ToList();

        public static void PrintLsmod()
        {
            if (Loaded.Count == 0) { Console.WriteLine("No modules loaded."); return; }

            Console.WriteLine($"{"Module",-20}{"Size",10}  Used by");

            foreach (var name in Loaded)
            {
                var module = Registry.FirstOrDefault(m => m.Name == name);
                if (module == null) continue;

                var users = UsedBy(name);
                string usedBy = users.Count == 0 ? "0" : $"{users.Count} {string.Join(",", users)}";

                Console.WriteLine($"{module.Name,-20}{module.SizeBytes,10}  {usedBy}");
            }
        }
    }
}
