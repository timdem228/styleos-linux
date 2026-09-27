using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace StyleOS
{
    public static class DiskCommands
    {
        public static void Df(List<string> args)
        {
            bool human = Io.HasFlag(args, "-h");
            Console.WriteLine($"{"Filesystem",-16}{(human ? "Size" : "1K-blocks"),12}{"Used",12}{"Avail",12}{"Use%",6}  Mounted on");

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                try
                {
                    long total = drive.TotalSize;
                    long free = drive.TotalFreeSpace;
                    long used = total - free;

                    // total can legitimately be 0 on some virtual drives - that used to divide by zero.
                    int percent = total <= 0 ? 0 : (int)Math.Round(used * 100.0 / total);

                    string Fmt(long bytes) => human ? PathUtil.HumanSize(bytes) : (bytes / 1024).ToString();

                    Console.WriteLine($"{drive.Name,-16}{Fmt(total),12}{Fmt(used),12}{Fmt(free),12}{percent + "%",6}  {drive.RootDirectory}");
                }
                catch { }
            }
        }

        public static void Mount()
        {
            Console.WriteLine("sysfs on /sys type sysfs (rw,nosuid,nodev,noexec,relatime)");
            Console.WriteLine("proc on /proc type proc (rw,nosuid,nodev,noexec,relatime)");
            Console.WriteLine($"styleos on / type styleosfs (rw,relatime,kernel={Kernel.Version})");

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                try { Console.WriteLine($"{drive.Name} on /mnt/{drive.Name.TrimEnd('\\', ':').ToLower()} type {drive.DriveFormat} (rw,relatime)"); }
                catch { }
            }
        }

        public static void Lsblk()
        {
            Console.WriteLine($"{"NAME",-10}{"SIZE",10} {"TYPE",-6} {"FSTYPE",-8} MOUNTPOINT");
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    string name = drive.Name.TrimEnd('\\', ':').ToLower();
                    string size = drive.IsReady ? PathUtil.HumanSize(drive.TotalSize) : "-";
                    string fs = drive.IsReady ? drive.DriveFormat : "-";
                    Console.WriteLine($"{"sd" + name,-10}{size,10} {drive.DriveType.ToString().ToLower(),-6} {fs,-8} {drive.Name}");
                }
                catch { }
            }
        }
    }

    public static class ProcessCommands
    {
        public static void Kill(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("kill", "usage: kill [-9] <pid>"); return; }

            foreach (var op in operands)
            {
                if (!int.TryParse(op, out int pid)) { Io.Error("kill", $"{op}: arguments must be process ids"); continue; }
                if (pid == Environment.ProcessId) { Io.Error("kill", "refusing to kill the StyleOS session"); continue; }

                try
                {
                    var process = Process.GetProcessById(pid);
                    process.Kill();
                    SystemLogger.Log("PROC", $"killed pid {pid} ({process.ProcessName})");
                }
                catch (ArgumentException) { Io.Error("kill", $"({pid}) - No such process"); }
                catch (Exception ex) { Io.Error("kill", $"({pid}) - {ex.Message}"); }
            }
        }

        public static void Killall(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("killall", "usage: killall <name>"); return; }

            foreach (var name in operands)
            {
                var processes = Process.GetProcessesByName(name.Replace(".exe", ""));
                if (processes.Length == 0) { Io.Error("killall", $"{name}: no process found"); continue; }

                foreach (var p in processes)
                {
                    try { if (p.Id != Environment.ProcessId) p.Kill(); }
                    catch (Exception ex) { Io.Error("killall", ex.Message); }
                }
            }
        }

        public static void Pgrep(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("pgrep", "usage: pgrep <pattern>"); return; }

            bool found = false;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.ProcessName.IndexOf(operands[0], StringComparison.OrdinalIgnoreCase) < 0) continue;
                    found = true;
                    Console.WriteLine(Io.HasFlag(args, "-l") ? $"{p.Id} {p.ProcessName}" : p.Id.ToString());
                }
                catch { }
            }
            if (!found) ShellEnv.ExitCode = 1;
        }

        public static void Jobs() => Console.WriteLine("[1]+  Running                 styleshell");

        public static void Sleep(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("sleep", "missing operand"); return; }

            string spec = operands[0];
            double multiplier = 1;
            if (spec.EndsWith("m")) { multiplier = 60; spec = spec.TrimEnd('m'); }
            else if (spec.EndsWith("h")) { multiplier = 3600; spec = spec.TrimEnd('h'); }
            else spec = spec.TrimEnd('s');

            if (!double.TryParse(spec, out double seconds)) { Io.Error("sleep", $"invalid time interval '{operands[0]}'"); return; }

            double total = Math.Min(seconds * multiplier, 3600);
            var end = DateTime.Now.AddSeconds(total);
            while (DateTime.Now < end)
            {
                if (ConsoleHost.KeyReady() && Console.ReadKey(true).Key == ConsoleKey.C) break;
                System.Threading.Thread.Sleep(50);
            }
        }

        public static void Watch(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("watch", "usage: watch <command>"); return; }

            string command = string.Join(" ", operands);
            Console.WriteLine($"watch: running '{command}' every 2s, press q to stop");

            while (true)
            {
                Console.Clear();
                Console.WriteLine($"Every 2,0s: {command}{new string(' ', 4)}{DateTime.Now:HH:mm:ss}\n");
                CommandRouter.ExecuteLine(command).GetAwaiter().GetResult();

                for (int i = 0; i < 20; i++)
                {
                    if (ConsoleHost.KeyReady())
                    {
                        var key = Console.ReadKey(true).Key;
                        if (key == ConsoleKey.Q || key == ConsoleKey.Escape) { Console.Clear(); return; }
                    }
                    System.Threading.Thread.Sleep(100);
                }
            }
        }
    }

    public static class UserCommands
    {
        public static void Passwd(List<string> args)
        {
            string target = Io.Operands(args).FirstOrDefault() ?? Kernel.CurrentUser?.Username;
            if (target == null) { Io.Error("passwd", "no session"); return; }

            if (!string.Equals(target, Kernel.CurrentUser.Username, StringComparison.OrdinalIgnoreCase) && !Kernel.CurrentUser.IsRoot)
            {
                Io.Error("passwd", "You may not view or modify password information for other users.");
                return;
            }

            Console.WriteLine($"Changing password for {target}.");
            Console.Write("New password: ");
            string p1 = AuthSystem.ReadPassword();
            Console.Write("\nRetype new password: ");
            string p2 = AuthSystem.ReadPassword();
            Console.WriteLine();

            if (p1 != p2) { Io.Error("passwd", "passwords do not match"); return; }

            try
            {
                AuthSystem.ChangePassword(target, p1);
                Io.Info("passwd: password updated successfully", ConsoleColor.Green);
            }
            catch (Exception ex) { Io.Error("passwd", ex.Message); }
        }

        public static void UserAdd(List<string> args)
        {
            if (Kernel.CurrentUser?.IsRoot != true) { Io.Error("useradd", "Permission denied."); return; }

            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("useradd", "usage: useradd <username>"); return; }

            Console.Write("Set password: ");
            string pass = AuthSystem.ReadPassword();
            Console.WriteLine();

            try
            {
                AuthSystem.AddUser(operands[0], pass);
                Console.WriteLine($"User '{operands[0]}' created successfully.");
            }
            catch (Exception ex) { Io.Error("useradd", ex.Message); }
        }

        public static void UserDel(List<string> args)
        {
            if (Kernel.CurrentUser?.IsRoot != true) { Io.Error("userdel", "Permission denied."); return; }

            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("userdel", "usage: userdel <username>"); return; }

            try
            {
                AuthSystem.DeleteUser(operands[0]);
                Console.WriteLine($"User '{operands[0]}' deleted.");
            }
            catch (Exception ex) { Io.Error("userdel", ex.Message); }
        }

        public static void Users()
        {
            foreach (var u in AuthSystem.All)
                Console.WriteLine($"{u.Username,-16}uid={u.Uid,-6}{(u.IsRoot ? "root" : "user")}");
        }

        public static void Groups()
        {
            var u = Kernel.CurrentUser;
            Console.WriteLine(u == null ? "" : (u.IsRoot ? "root wheel sudo" : "users"));
        }

        public static async System.Threading.Tasks.Task Sudo(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("sudo", "usage: sudo <command>"); return; }

            if (Kernel.CurrentUser?.IsRoot == true)
            {
                await CommandRouter.ExecuteLine(string.Join(" ", operands));
                return;
            }

            if (!AuthSystem.RequirePassword()) return;

            var saved = Kernel.CurrentUser;
            try
            {
                var root = AuthSystem.Find("root");
                if (root != null) Kernel.CurrentUser = root;
                await CommandRouter.ExecuteLine(string.Join(" ", operands));
            }
            finally { Kernel.CurrentUser = saved; }
        }

        public static void Su(List<string> args)
        {
            string target = Io.Operands(args).FirstOrDefault() ?? "root";
            var user = AuthSystem.Find(target);
            if (user == null) { Io.Error("su", $"user {target} does not exist"); return; }

            Console.Write("Password: ");
            string password = AuthSystem.ReadPassword();
            Console.WriteLine();

            if (AuthSystem.HashPassword(password) != user.PasswordHash) { Io.Error("su", "Authentication failure"); return; }

            Kernel.CurrentUser = user;
            ShellEnv.Set("USER", user.Username);
            Console.WriteLine($"Switched to {user.Username}.");
        }
    }

    public static class ServiceCommands
    {
        private static readonly Dictionary<string, bool> Services = new Dictionary<string, bool>
        {
            { "styleos-core", true },
            { "styleos-shell", true },
            { "styleos-update", true },
            { "networking", true },
            { "logging", true },
            { "pacman-repo", true }
        };

        public static void Systemctl(List<string> args)
        {
            var operands = Io.Operands(args);
            string action = operands.Count > 0 ? operands[0].ToLower() : "status";
            string unit = operands.Count > 1 ? operands[1].Replace(".service", "") : null;

            switch (action)
            {
                case "list-units":
                case "list-unit-files":
                case "status" when unit == null:
                    Console.WriteLine($"{"UNIT",-22}{"LOAD",-10}{"ACTIVE",-10}DESCRIPTION");
                    foreach (var service in Services)
                    {
                        Console.ForegroundColor = service.Value ? ConsoleColor.Green : ConsoleColor.DarkGray;
                        Console.Write("●  ");
                        Console.ResetColor();
                        Console.WriteLine($"{service.Key + ".service",-22}{"loaded",-10}{(service.Value ? "active" : "inactive"),-10}StyleOS unit");
                    }
                    return;

                case "status":
                    if (!Services.ContainsKey(unit)) { Io.Error("systemctl", $"Unit {unit}.service could not be found."); return; }
                    Console.WriteLine($"● {unit}.service - StyleOS unit");
                    Console.WriteLine($"     Loaded: loaded (/usr/lib/systemd/system/{unit}.service; enabled)");
                    Console.WriteLine($"     Active: {(Services[unit] ? "active (running)" : "inactive (dead)")} since {Kernel.BootTime:ddd yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"   Main PID: {Environment.ProcessId} ({unit})");
                    return;

                case "start":
                case "stop":
                case "restart":
                case "enable":
                case "disable":
                    if (unit == null) { Io.Error("systemctl", "Too few arguments."); return; }
                    if (!Services.ContainsKey(unit)) { Io.Error("systemctl", $"Unit {unit}.service not found."); return; }
                    if (Kernel.CurrentUser?.IsRoot != true && !AuthSystem.RequirePassword()) return;

                    Services[unit] = action == "start" || action == "restart" || action == "enable";
                    Console.WriteLine($"Unit {unit}.service {action}ed.");
                    SystemLogger.Log("SYSTEMD", $"{action} {unit}");
                    return;

                default:
                    Io.Error("systemctl", $"Unknown command verb {action}.");
                    return;
            }
        }

        public static void Journalctl(List<string> args)
        {
            int count = 50;
            int nIndex = args.IndexOf("-n");
            if (nIndex >= 0 && nIndex + 1 < args.Count) int.TryParse(args[nIndex + 1], out count);

            foreach (var line in SystemLogger.Tail(count)) Console.WriteLine(line);
        }
    }
}
