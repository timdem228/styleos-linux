#!/usr/bin/env python3
"""One-time Linux port of the imported Windows sources (CI runs it once, then deletes it).

Every edit is an exact-text replacement against the original timdem228/styleos- files,
so if anything drifted the script fails loudly instead of half-applying."""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
files = {}
errors = []


def text(path):
    if path not in files:
        raw = (ROOT / path).read_text(encoding="utf-8-sig")
        files[path] = raw.replace("\r\n", "\n")
    return files[path]


def rep(path, old, new, count=1):
    t = text(path)
    n = t.count(old)
    if n == 0:
        errors.append(f"{path}: text not found: {old[:100]!r}")
        return
    if count is not None and n != count:
        errors.append(f"{path}: expected {count} match(es), found {n}: {old[:70]!r}")
        return
    files[path] = t.replace(old, new)


def between(path, start, end, new):
    t = text(path)
    i = t.find(start)
    if i < 0:
        errors.append(f"{path}: start marker not found: {start[:90]!r}")
        return
    j = t.find(end, i + len(start))
    if j < 0:
        errors.append(f"{path}: end marker not found: {end[:90]!r}")
        return
    files[path] = t[:i] + new + t[j:]


# ============================================================== Core/Kernel.cs
K = "Core/Kernel.cs"
rep(K, 'public const string Repo = "timdem228/styleos-";', 'public const string Repo = "timdem228/styleos-linux";')
rep(K, "        public static SystemConfig Config { get; set; } = new SystemConfig();\n",
    "        public static SystemConfig Config { get; set; } = new SystemConfig();\n"
    "        /// <summary>Set by Ctrl+C / SIGINT while a host program runs; checked and cleared by whoever is waiting.</summary>\n"
    "        public static volatile bool CancelRequested;\n")
rep(K, 'public static readonly string SysDir = Path.Combine(BaseDir, ".styleos");',
    'public static readonly string SysDir = ResolveSysDir();')
rep(K, "        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);\n",
    "        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);\n"
    "        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);\n"
    "        public static bool IsTermux => HostPlatform.IsTermux;\n")
rep(K, 'try { return Path.GetPathRoot(BaseDir) ?? "/"; }',
    'try { return IsWindows ? (Path.GetPathRoot(BaseDir) ?? "/") : "/"; }')
rep(K, "        public static void EnsureSystemDirs()\n", r'''        /// <summary>
        /// Where StyleOS keeps users, config, logs and modules. The Windows build used a
        /// ".styleos" folder next to the exe; on Linux the install folder is often read-only
        /// (/opt, /usr/lib, a package prefix), so this follows XDG instead:
        /// $STYLEOS_HOME, else $XDG_DATA_HOME/styleos, else ~/.local/share/styleos.
        /// </summary>
        private static string ResolveSysDir()
        {
            try
            {
                string custom = Environment.GetEnvironmentVariable("STYLEOS_HOME");
                if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Path.Combine(BaseDir, ".styleos");

                string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (!string.IsNullOrWhiteSpace(xdg)) return Path.Combine(xdg, "styleos");

                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home)) return Path.Combine(home, ".local", "share", "styleos");
            }
            catch { }
            return Path.Combine(BaseDir, ".styleos");
        }

        public static void EnsureSystemDirs()
''')

# ============================================================== Core/BootManager.cs
B = "Core/BootManager.cs"
rep(B, "using System.Drawing;\n",
    "using SixLabors.ImageSharp;\nusing SixLabors.ImageSharp.PixelFormats;\nusing SixLabors.ImageSharp.Processing;\n")
rep(B, "                Kernel.LogoFile\n            };",
    '                Kernel.LogoFile,\n                Path.Combine(Kernel.BaseDir, "img", "styleos.png")\n            };')
between(B, "#pragma warning disable CA1416\n                using var bitmap = new Bitmap(target);",
        "#pragma warning restore CA1416", r'''                // ImageSharp instead of System.Drawing (GDI+ is Windows-only on .NET 7+).
                using var image = Image.Load<Rgba32>(target);

                int maxWidth = Math.Max(10, ConsoleHost.Width - 4);
                int maxHeight = Math.Max(10, ConsoleHost.Height - 6) * 2;

                float ratio = Math.Min((float)maxWidth / image.Width, (float)maxHeight / image.Height);
                int width = Math.Max(1, (int)(image.Width * ratio));
                int height = Math.Max(2, (int)(image.Height * ratio));

                image.Mutate(ctx => ctx.Resize(width, height));

                int startLeft = Math.Max(0, (ConsoleHost.Width - width) / 2);
                int startTop = Math.Max(0, (ConsoleHost.Height - height / 2) / 2);

                for (int y = 0; y + 1 < height; y += 2)
                {
                    int row = startTop + y / 2;
                    if (row >= ConsoleHost.Height - 1) break;

                    ConsoleHost.SetCursor(startLeft, row);

                    for (int x = 0; x < width && startLeft + x < ConsoleHost.Width - 1; x++)
                    {
                        Rgba32 top = image[x, y];
                        Rgba32 bottom = image[x, y + 1];

                        bool topClear = top.A < 128 || (top.R < 15 && top.G < 15 && top.B < 15);
                        bool bottomClear = bottom.A < 128 || (bottom.R < 15 && bottom.G < 15 && bottom.B < 15);

                        if (topClear && bottomClear) Console.Write("\x1b[0m ");
                        else if (topClear) Console.Write($"\x1b[38;2;{bottom.R};{bottom.G};{bottom.B}m▄\x1b[0m");
                        else if (bottomClear) Console.Write($"\x1b[38;2;{top.R};{top.G};{top.B}m▀\x1b[0m");
                        else Console.Write($"\x1b[38;2;{top.R};{top.G};{top.B};48;2;{bottom.R};{bottom.G};{bottom.B}m▀\x1b[0m");
                    }
                }
''')

# ============================================================== Core/SystemServices.cs
S = "Core/SystemServices.cs"
rep(S, "        public static void LoadConfig()\n        {", "        public static void LoadConfig(bool applyTheme = true)\n        {")
rep(S, "            ApplyTheme();\n        }\n\n        public static void SaveConfig()",
    "            if (applyTheme) ApplyTheme();\n        }\n\n        public static void SaveConfig()")
rep(S, "                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });\n                Thread.Sleep(500);", r'''                if (!HostPlatform.OpenExternal(url))
                {
                    // Headless Linux / no browser: keep the report instead of losing it.
                    string file = Path.Combine(Kernel.SysDir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                    try { File.WriteAllText(file, Uri.UnescapeDataString(body)); } catch { }
                    Console.WriteLine("\nНе удалось открыть браузер. Отчёт сохранён в: " + file);
                    Console.WriteLine("Отправьте его на admin@timd.site или откройте ссылку вручную:");
                    Console.WriteLine(url);
                    Console.WriteLine("\nНажмите Enter...");
                    Console.ReadLine();
                }
                Thread.Sleep(500);''')

# ============================================================== Commands/SysInfoCommands.cs
I = "Commands/SysInfoCommands.cs"
rep(I, '                ("Kernel", Kernel.KernelString),\n',
    '                ("Kernel", Kernel.KernelString),\n'
    '                ("Host OS", $"{HostPlatform.OsPrettyName()} (linux {HostPlatform.KernelRelease()})"),\n')
rep(I, '("Terminal", ConsoleHost.IsWindowsTerminal ? "Windows Terminal" : "console"),',
    '("Terminal", HostPlatform.TerminalName()),')
rep(I, 'string model = WmiValue("Win32_ComputerSystem", "Model");', 'string model = HostPlatform.HostModel();')
between(I, '            if (Kernel.IsWindows)\n            {\n                try\n                {\n                    string c = WmiValue',
        "            if (totalMb <= 0) totalMb = 1;", r'''            try
            {
                string c = HostPlatform.CpuModel();
                if (!string.IsNullOrWhiteSpace(c)) cpu = c.Trim();

                string g = HostPlatform.GpuName();
                if (!string.IsNullOrWhiteSpace(g)) gpu = g.Trim();

                var mem = HostPlatform.MemInfo();
                if (mem.TotalMb > 0) totalMb = mem.TotalMb;
            }
            catch (Exception ex) { SystemLogger.Log("SYSINFO", "/proc unavailable: " + ex.Message); }

''')
between(I, '            if (Kernel.IsWindows)\n            {\n                string free = WmiValue',
        "            try\n            {\n                long used = Process", r'''            var mem = HostPlatform.MemInfo();
            if (mem.TotalMb > 0) return mem.AvailableMb;

''')
between(I, "        private static string WmiValue(", "        public static void Uname(", "")
rep(I, "load average: 0.00, 0.01, 0.05", "load average: {HostPlatform.LoadAverage()}", count=2)
rep(I, '            Console.WriteLine($"{"Swap:",-10}{Fmt(total / 2),12}{Fmt(0),12}{Fmt(total / 2),12}");',
    '            var swap = HostPlatform.SwapInfo();\n'
    '            Console.WriteLine($"{"Swap:",-10}{Fmt(swap.TotalMb),12}{Fmt(Math.Max(0, swap.TotalMb - swap.FreeMb)),12}{Fmt(swap.FreeMb),12}");')
rep(I, '{(Environment.ProcessorCount > 2 ? "VT-x" : "none")}', '{HostPlatform.Virtualization()}')

# ============================================================== Commands/SystemCommands.cs (disks)
D = "Commands/SystemCommands.cs"
between(D, "    public static class DiskCommands", "    public static class ProcessCommands", r'''    public static class DiskCommands
    {
        /// <summary>Virtual filesystems df hides unless -a is given, like coreutils df.</summary>
        private static readonly HashSet<string> PseudoFs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "proc", "sysfs", "devpts", "cgroup", "cgroup2", "securityfs", "pstore", "debugfs", "tracefs",
            "configfs", "fusectl", "mqueue", "hugetlbfs", "bpf", "autofs", "binfmt_misc", "efivarfs",
            "rpc_pipefs", "nsfs", "selinuxfs", "functionfs", "ramfs"
        };

        private class MountEntry
        {
            public string Device;
            public string MountPoint;
            public string FsType;
            public string Options;
        }

        private static List<MountEntry> ReadMounts()
        {
            var result = new List<MountEntry>();
            string text = HostPlatform.ReadText("/proc/self/mounts") ?? HostPlatform.ReadText("/proc/mounts");
            if (string.IsNullOrEmpty(text)) return result;

            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split(' ');
                if (parts.Length < 4) continue;
                result.Add(new MountEntry
                {
                    Device = Unescape(parts[0]),
                    MountPoint = Unescape(parts[1]),
                    FsType = parts[2],
                    Options = parts[3]
                });
            }
            return result;
        }

        private static string Unescape(string s) =>
            s.Replace("\\040", " ").Replace("\\011", "\t").Replace("\\012", "\n").Replace("\\134", "\\");

        public static void Df(List<string> args)
        {
            bool human = Io.HasFlag(args, "-h");
            bool all = Io.HasFlag(args, "-a");
            bool showType = Io.HasFlag(args, "-T");

            var devices = new Dictionary<string, MountEntry>();
            foreach (var m in ReadMounts()) devices[m.MountPoint] = m;

            string typeHeader = showType ? $"{"Type",-10}" : "";
            Console.WriteLine($"{"Filesystem",-24}{typeHeader}{(human ? "Size" : "1K-blocks"),12}{"Used",12}{"Avail",12}{"Use%",6}  Mounted on");

            string Fmt(long bytes) => human ? PathUtil.HumanSize(bytes) : (bytes / 1024).ToString();

            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); }
            catch (Exception ex) { Io.Error("df", ex.Message); return; }

            foreach (var drive in drives)
            {
                try
                {
                    if (!drive.IsReady) continue;
                    string fsType = drive.DriveFormat;
                    if (!all && PseudoFs.Contains(fsType)) continue;

                    long total = drive.TotalSize;
                    if (!all && total <= 0) continue;

                    long avail = drive.AvailableFreeSpace;
                    long used = total - drive.TotalFreeSpace;

                    // total can legitimately be 0 on virtual filesystems - no divide by zero.
                    int percent = total <= 0 ? 0 : (int)Math.Ceiling(used * 100.0 / total);

                    string mountPoint = drive.Name;
                    string device = devices.TryGetValue(mountPoint, out var entry) ? entry.Device : fsType;
                    string type = showType ? $"{fsType,-10}" : "";

                    Console.WriteLine($"{device,-24}{type}{Fmt(total),12}{Fmt(used),12}{Fmt(avail),12}{percent + "%",6}  {mountPoint}");
                }
                catch { }
            }
        }

        public static void Mount()
        {
            var mounts = ReadMounts();
            Console.WriteLine($"styleos on / type styleosfs (rw,relatime,kernel={Kernel.Version})");

            if (mounts.Count == 0)
            {
                Console.WriteLine("sysfs on /sys type sysfs (rw,nosuid,nodev,noexec,relatime)");
                Console.WriteLine("proc on /proc type proc (rw,nosuid,nodev,noexec,relatime)");
                return;
            }

            foreach (var m in mounts)
                Console.WriteLine($"{m.Device} on {m.MountPoint} type {m.FsType} ({m.Options})");
        }

        public static void Lsblk()
        {
            Console.WriteLine($"{"NAME",-14}{"SIZE",9} {"RO",-3}{"TYPE",-6}MOUNTPOINTS");
            var mounts = ReadMounts();
            bool any = false;

            try
            {
                if (Directory.Exists("/sys/block"))
                {
                    foreach (var dev in Directory.GetDirectories("/sys/block").OrderBy(d => d, StringComparer.Ordinal))
                    {
                        string name = Path.GetFileName(dev);
                        long sectors = ReadLong(Path.Combine(dev, "size"));
                        if (sectors <= 0) continue;

                        string type = name.StartsWith("loop") ? "loop" : name.StartsWith("sr") ? "rom" : "disk";
                        PrintBlock(name, sectors, dev, type, mounts, "");
                        any = true;

                        var parts = Directory.GetDirectories(dev)
                            .Where(p => File.Exists(Path.Combine(p, "partition")))
                            .OrderBy(p => p, StringComparer.Ordinal)
                            .ToList();

                        for (int i = 0; i < parts.Count; i++)
                        {
                            PrintBlock(Path.GetFileName(parts[i]), ReadLong(Path.Combine(parts[i], "size")), parts[i],
                                "part", mounts, i == parts.Count - 1 ? "└─" : "├─");
                        }
                    }
                }
            }
            catch (Exception ex) { SystemLogger.Log("DISK", "lsblk: " + ex.Message); }

            if (any) return;

            // No /sys/block (Termux, most containers): show what the mount table knows.
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady || drive.TotalSize <= 0 || PseudoFs.Contains(drive.DriveFormat)) continue;
                        string device = mounts.FirstOrDefault(m => m.MountPoint == drive.Name)?.Device ?? drive.DriveFormat;
                        string name = Path.GetFileName(device.TrimEnd('/'));
                        if (string.IsNullOrEmpty(name)) name = device;
                        Console.WriteLine($"{name,-14}{PathUtil.HumanSize(drive.TotalSize),9} {"0",-3}{drive.DriveType.ToString().ToLower(),-6}{drive.Name}");
                    }
                    catch { }
                }
            }
            catch (Exception ex) { Io.Error("lsblk", ex.Message); }
        }

        private static long ReadLong(string path) =>
            long.TryParse(HostPlatform.ReadFirstLine(path), out long v) ? v : 0;

        private static void PrintBlock(string name, long sectors, string sysPath, string type, List<MountEntry> mounts, string prefix)
        {
            bool ro = HostPlatform.ReadFirstLine(Path.Combine(sysPath, "ro")) == "1";
            string points = string.Join(",", mounts
                .Where(m => m.Device == "/dev/" + name || m.Device == "/dev/mapper/" + name)
                .Select(m => m.MountPoint));
            Console.WriteLine($"{prefix + name,-14}{PathUtil.HumanSize(sectors * 512),9} {(ro ? "1" : "0"),-3}{type,-6}{points}");
        }
    }

''')

# ============================================================== Shell/CommandRouter.cs
R = "Shell/CommandRouter.cs"
rep(R, 'Add("chmod", files, "chmod <mode> <path>", "Change the read-only attribute", FileCommands.Chmod);',
    'Add("chmod", files, "chmod [-R] <mode> <path...>", "Change file permissions (755, u+x, go-w ...)", FileCommands.Chmod);')
rep(R, 'Add("ssh", net, "ssh user@host", "Remote shell (not implemented yet)", NetworkCommands.Ssh);',
    'Add("ssh", net, "ssh user@host", "Remote shell (uses the host ssh client)", NetworkCommands.Ssh, true);')

# ============================================================== Shell/PathUtil.cs
P = "Shell/PathUtil.cs"
rep(P, "path.StartsWith(home, StringComparison.OrdinalIgnoreCase))",
    "path.StartsWith(home, Kernel.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))")
rep(P, "            return path.Replace('\\\\', '/');", "            return Kernel.IsWindows ? path.Replace('\\\\', '/') : path;")

# ============================================================== Commands/NetworkCommands.cs
N = "Commands/NetworkCommands.cs"
rep(N, "        public static void IfConfig()\n        {\n", r'''        public static void IfConfig()
        {
            try { IfConfigManaged(); }
            catch (Exception ex)
            {
                // Android 11+ (Termux) blocks the netlink calls .NET uses here - fall back to
                // whatever tool the host has.
                SystemLogger.Log("NET", "managed ifconfig failed: " + ex.Message);
                if (HostPlatform.RunStreaming("ip", "addr") >= 0) return;
                if (HostPlatform.RunStreaming("ifconfig") >= 0) return;
                Io.Error("ifconfig", "network interfaces are not accessible here: " + ex.Message);
            }
        }

        private static void IfConfigManaged()
        {
''')
rep(N, '            string host = operands[0];\n            Console.WriteLine($"traceroute to {host}, 30 hops max, 60 byte packets");', r'''            string host = operands[0];

            // A real traceroute / tracepath on the host gives proper per-hop results; the
            // managed TTL loop below is only the fallback.
            foreach (var tool in new[] { "traceroute", "tracepath" })
            {
                if (HostPlatform.FindExecutable(tool) == null) continue;
                ShellEnv.ExitCode = HostPlatform.RunStreaming(tool, host) == 0 ? 0 : 1;
                return;
            }

            Console.WriteLine($"traceroute to {host}, 30 hops max, 60 byte packets");''')
rep(N, '            Io.Error("ssh", $"connect to host {operands[0]}: StyleOS has no ssh client yet");', r'''            if (HostPlatform.FindExecutable("ssh") == null)
            {
                Io.Error("ssh", $"connect to host {operands[0]}: no ssh client on the host (install openssh)");
                return;
            }
            ShellEnv.ExitCode = HostPlatform.RunInteractive("ssh", args.ToArray());''')

# ============================================================== Commands/FileCommands.cs
F = "Commands/FileCommands.cs"
rep(F, "            string ext = e.Extension.ToLower();\n",
    "            if (e is FileInfo fi && HostPlatform.IsExecutable(fi.FullName)) return ConsoleColor.Green;\n"
    "            string ext = e.Extension.ToLower();\n")
rep(F, '$"drwxr-xr-x  {', '$"{Mode(d)}  {')
rep(F, '$"-rw-r--r--  {size,10}', '$"{Mode(f)}  {size,10}')
rep(F, '                        Console.WriteLine($"Attrib: {f.Attributes}");',
    '                        Console.WriteLine($"  Mode: ({ModeOctal(f)}/{Mode(f)})");\n'
    '                        Console.WriteLine($"Attrib: {f.Attributes}");')
rep(F, '                        Console.WriteLine($"  Size: 4096         directory");',
    '                        Console.WriteLine($"  Size: 4096         directory");\n'
    '                        Console.WriteLine($"  Mode: ({ModeOctal(d)}/{Mode(d)})");')
between(F, "        /// <summary>Windows has no POSIX mode bits", "        public static void Chown(", r'''        /// <summary>"drwxr-xr-x"-style string built from the real POSIX mode bits.</summary>
        public static string Mode(FileSystemInfo entry)
        {
            char type = entry.LinkTarget != null ? 'l' : entry is DirectoryInfo ? 'd' : '-';
            try
            {
                var m = entry.UnixFileMode;
                var sb = new System.Text.StringBuilder();
                sb.Append(type);
                sb.Append(m.HasFlag(UnixFileMode.UserRead) ? 'r' : '-');
                sb.Append(m.HasFlag(UnixFileMode.UserWrite) ? 'w' : '-');
                sb.Append(m.HasFlag(UnixFileMode.UserExecute)
                    ? (m.HasFlag(UnixFileMode.SetUser) ? 's' : 'x')
                    : (m.HasFlag(UnixFileMode.SetUser) ? 'S' : '-'));
                sb.Append(m.HasFlag(UnixFileMode.GroupRead) ? 'r' : '-');
                sb.Append(m.HasFlag(UnixFileMode.GroupWrite) ? 'w' : '-');
                sb.Append(m.HasFlag(UnixFileMode.GroupExecute)
                    ? (m.HasFlag(UnixFileMode.SetGroup) ? 's' : 'x')
                    : (m.HasFlag(UnixFileMode.SetGroup) ? 'S' : '-'));
                sb.Append(m.HasFlag(UnixFileMode.OtherRead) ? 'r' : '-');
                sb.Append(m.HasFlag(UnixFileMode.OtherWrite) ? 'w' : '-');
                sb.Append(m.HasFlag(UnixFileMode.OtherExecute)
                    ? (m.HasFlag(UnixFileMode.StickyBit) ? 't' : 'x')
                    : (m.HasFlag(UnixFileMode.StickyBit) ? 'T' : '-'));
                return sb.ToString();
            }
            catch { return type + (entry is DirectoryInfo ? "rwxr-xr-x" : "rw-r--r--"); }
        }

        public static string ModeOctal(FileSystemInfo entry)
        {
            try { return Convert.ToString((int)entry.UnixFileMode, 8).PadLeft(4, '0'); }
            catch { return entry is DirectoryInfo ? "0755" : "0644"; }
        }

        /// <summary>Real chmod: octal ("755", "0644") or symbolic ("+x", "u+x", "go-w", "a=r", "u+rw,g-x").</summary>
        public static void Chmod(List<string> args)
        {
            // The mode itself ("-w", "+x", "444"...) commonly starts with '-' or '+', so this
            // reads positional args directly instead of Io.Operands(), which would otherwise
            // mistake the mode for a flag and filter it out.
            bool recursive = args.Contains("-R");
            var positional = args.Where(a => a != "-R").ToList();
            if (positional.Count < 2) { Io.Error("chmod", "missing operand"); return; }

            string mode = positional[0];
            foreach (var target in positional.Skip(1))
            {
                foreach (var candidate in PathUtil.Glob(target))
                {
                    string path = PathUtil.Resolve(candidate);
                    if (!PathUtil.Exists(path)) { Io.Error("chmod", $"cannot access '{candidate}': No such file or directory"); continue; }

                    try
                    {
                        ApplyMode(path, mode);
                        if (recursive && Directory.Exists(path))
                            foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
                                ApplyMode(child, mode);
                    }
                    catch (FormatException) { Io.Error("chmod", $"invalid mode: '{mode}'"); return; }
                    catch (Exception ex) { Io.Error("chmod", $"changing permissions of '{candidate}': {ex.Message}"); }
                }
            }
        }

        private static void ApplyMode(string path, string spec)
        {
            if (OperatingSystem.IsWindows())
            {
                bool readOnly = spec.Contains("-w") || spec == "444" || spec == "555";
                var attrs = File.GetAttributes(path);
                File.SetAttributes(path, readOnly ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly);
                return;
            }

            var current = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, ParseMode(spec, current, Directory.Exists(path)));
        }

        /// <summary>Parses a chmod mode spec against the current mode. Throws FormatException on garbage.</summary>
        public static UnixFileMode ParseMode(string spec, UnixFileMode current, bool isDirectory)
        {
            if (string.IsNullOrEmpty(spec)) throw new FormatException();

            if (spec.All(c => c >= '0' && c <= '7'))
            {
                if (spec.Length > 4) throw new FormatException();
                return (UnixFileMode)Convert.ToInt32(spec, 8);
            }

            int mode = (int)current;
            foreach (var clause in spec.Split(','))
            {
                int i = 0, who = 0;
                while (i < clause.Length && "ugoa".IndexOf(clause[i]) >= 0)
                {
                    who |= clause[i] switch { 'u' => 4, 'g' => 2, 'o' => 1, _ => 7 };
                    i++;
                }
                if (who == 0) who = 7;
                if (i >= clause.Length) throw new FormatException();

                while (i < clause.Length)
                {
                    char op = clause[i++];
                    if (op != '+' && op != '-' && op != '=') throw new FormatException();

                    int perms = 0;
                    bool setId = false, sticky = false;
                    while (i < clause.Length && "rwxXst".IndexOf(clause[i]) >= 0)
                    {
                        switch (clause[i])
                        {
                            case 'r': perms |= 4; break;
                            case 'w': perms |= 2; break;
                            case 'x': perms |= 1; break;
                            case 'X': if (isDirectory || (mode & 0x49) != 0) perms |= 1; break;
                            case 's': setId = true; break;
                            case 't': sticky = true; break;
                        }
                        i++;
                    }

                    int bits = 0, mask = 0;
                    if ((who & 4) != 0) { bits |= perms << 6; mask |= 7 << 6; if (setId) bits |= 0x800; }
                    if ((who & 2) != 0) { bits |= perms << 3; mask |= 7 << 3; if (setId) bits |= 0x400; }
                    if ((who & 1) != 0) { bits |= perms; mask |= 7; }
                    if (sticky) bits |= 0x200;

                    mode = op switch
                    {
                        '+' => mode | bits,
                        '-' => mode & ~bits,
                        _ => (mode & ~mask) | bits
                    };
                }
            }
            return (UnixFileMode)mode;
        }

''')

# ============================================================== Commands/MiscCommands.cs
M = "Commands/MiscCommands.cs"
rep(M, "using System.IO.Compression;\n", "using System.IO.Compression;\nusing System.Formats.Tar;\n")
between(M, "        /// <summary>tar mapped onto zip", "        public static void Gzip(", r'''        /// <summary>
        /// tar: real tar / tar.gz archives (System.Formats.Tar) that the host tar can read and
        /// write. Anything that is actually a zip - by name, or by its PK header, since old
        /// StyleOS "tar" archives were zips - still goes through unzip, so nothing old breaks.
        /// </summary>
        public static void Tar(List<string> args)
        {
            bool create = args.Any(a => a.StartsWith("-") && !a.StartsWith("--") && a.Contains('c'));
            bool extract = args.Any(a => a.StartsWith("-") && !a.StartsWith("--") && a.Contains('x'));
            bool listOnly = args.Any(a => a.StartsWith("-") && !a.StartsWith("--") && a.Contains('t'));
            bool gzipFlag = args.Any(a => a.StartsWith("-") && !a.StartsWith("--") && a.Contains('z'));

            string outDir = null;
            int cIndex = args.IndexOf("-C");
            if (cIndex >= 0 && cIndex + 1 < args.Count) outDir = args[cIndex + 1];

            var operands = Io.Operands(Io.StripOptionValues(args, "-C"));
            if (operands.Count == 0) { Io.Error("tar", "usage: tar -czf out.tar.gz <files> | tar -xzf archive [-C dir] | tar -tzf archive"); return; }
            if (!create && !extract && !listOnly) { Io.Error("tar", "you must specify one of -c, -x or -t"); return; }

            string archive = PathUtil.Resolve(operands[0]);

            try
            {
                if (create)
                {
                    if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { Zip(operands); return; }

                    bool gz = gzipFlag || archive.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                                       || archive.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

                    using var file = File.Create(archive);
                    using Stream stream = gz ? new GZipStream(file, CompressionLevel.Optimal, true) : file;
                    using (var writer = new TarWriter(stream, TarEntryFormat.Pax, true))
                    {
                        foreach (var operand in operands.Skip(1))
                        {
                            foreach (var candidate in PathUtil.Glob(operand))
                            {
                                string path = PathUtil.Resolve(candidate).TrimEnd('/');
                                string baseDir = Path.GetDirectoryName(path) ?? Kernel.CurrentDirectory;

                                if (File.Exists(path))
                                {
                                    writer.WriteEntry(path, Path.GetFileName(path));
                                    Console.WriteLine(Path.GetFileName(path));
                                }
                                else if (Directory.Exists(path))
                                {
                                    foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                                    {
                                        string entryName = Path.GetRelativePath(baseDir, f);
                                        writer.WriteEntry(f, entryName);
                                        Console.WriteLine(entryName);
                                    }
                                }
                                else Io.Error("tar", $"{candidate}: Cannot stat: No such file or directory");
                            }
                        }
                    }
                    return;
                }

                if (!File.Exists(archive)) { Io.Error("tar", $"{operands[0]}: Cannot open: No such file or directory"); return; }

                if (IsZipFile(archive))
                {
                    if (listOnly)
                    {
                        using var zip = ZipFile.OpenRead(archive);
                        foreach (var entry in zip.Entries) Console.WriteLine(entry.FullName);
                        return;
                    }
                    var unzipArgs = new List<string> { operands[0] };
                    if (outDir != null) { unzipArgs.Add("-d"); unzipArgs.Add(outDir); }
                    Unzip(unzipArgs);
                    return;
                }

                using (var file = File.OpenRead(archive))
                {
                    int b1 = file.ReadByte(), b2 = file.ReadByte();
                    file.Position = 0;
                    using Stream stream = b1 == 0x1F && b2 == 0x8B
                        ? new GZipStream(file, CompressionMode.Decompress, true)
                        : file;

                    if (listOnly)
                    {
                        using var reader = new TarReader(stream, true);
                        TarEntry entry;
                        while ((entry = reader.GetNextEntry()) != null) Console.WriteLine(entry.Name);
                        return;
                    }

                    string target = outDir != null ? PathUtil.Resolve(outDir) : Kernel.CurrentDirectory;
                    Directory.CreateDirectory(target);
                    TarFile.ExtractToDirectory(stream, target, true);
                }
            }
            catch (Exception ex) { Io.Error("tar", ex.Message); }
        }

        private static bool IsZipFile(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                return fs.ReadByte() == 0x50 && fs.ReadByte() == 0x4B;
            }
            catch { return false; }
        }

''')
between(M, "        public static void Open(List<string> args)", "        public static void Authors()", r'''        public static void Open(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("open", "missing operand"); return; }

            string target = operands[0];
            string path = PathUtil.Resolve(target);
            string what = PathUtil.Exists(path) ? path : target;

            if (!HostPlatform.OpenExternal(what))
                Io.Error("open", HostPlatform.IsTermux
                    ? "could not open it - install Termux:API (pkg install termux-api)"
                    : "no opener found (install xdg-utils) - open it yourself: " + what);
        }

''')
rep(M, '                else { Io.Error("which", $"no {name} in ({ShellEnv.Get("PATH")})"); }', r'''                else
                {
                    string host = HostPlatform.FindExecutable(name);
                    if (host != null) Console.WriteLine($"{host} (host system, not a StyleOS command)");
                    else Io.Error("which", $"no {name} in ({ShellEnv.Get("PATH")})");
                }''')

# ============================================================== Apps
A = "Apps/BugReportEditor.cs"
rep(A, '"^S Send    ^X Cancel    Arrows Move"', '"^S/^O Send    ^X Cancel    Arrows Move"')
rep(A, "if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.S)",
    "if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && (key.Key == ConsoleKey.S || key.Key == ConsoleKey.O))")
rep(A, '''                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Браузер с подготовленным письмом открыт. Нажмите 'Отправить' в почте.");
                SystemLogger.Log("BUGREPORT", "Draft opened in browser");''', r'''                if (HostPlatform.OpenExternal(url))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Браузер с подготовленным письмом открыт. Нажмите 'Отправить' в почте.");
                    SystemLogger.Log("BUGREPORT", "Draft opened in browser");
                }
                else
                {
                    // Headless Linux / Termux without Termux:API: save it instead of losing it.
                    string file = System.IO.Path.Combine(Kernel.SysDir, $"bugreport-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                    System.IO.File.WriteAllText(file, $"Версия: {Kernel.Version}\n\n{reportText}\n\nSystem Logs:\n{logs}");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Браузер не найден. Отчёт сохранён в: " + file);
                    Console.WriteLine("Отправьте его на admin@timd.site или откройте ссылку вручную:");
                    Console.ResetColor();
                    Console.WriteLine(url);
                    SystemLogger.Log("BUGREPORT", "Saved to " + file);
                    Console.WriteLine("\nНажмите любую клавишу...");
                    try { Console.ReadKey(true); } catch { }
                }''')

NA = "Apps/NanoEditor.cs"
# ^S is XOFF (flow control) on many Linux terminals, so ^O (real nano's "Write Out") saves too.
rep(NA, '"^S Save   ^X Exit   ^W Where Is   ^G Go To Line",', '"^O/^S Save   ^X Exit   ^W Where Is   ^G Go To Line",')
rep(NA, "                        case ConsoleKey.S:\n                            SaveFile(editor, filePath);",
    "                        case ConsoleKey.S:\n                        case ConsoleKey.O:\n                            SaveFile(editor, filePath);")

# ============================================================== Update/PythonModules.cs
PY = "Update/PythonModules.cs"
rep(PY, '        private static readonly string PythonPathFile = Path.Combine(Kernel.SysDir, "python_path.txt");\n',
    '        private static readonly string PythonPathFile = Path.Combine(Kernel.SysDir, "python_path.txt");\n\n'
    '        /// <summary>pip installs module dependencies here (pip --target): never into the system\n'
    '        /// Python, which Debian/Ubuntu/Fedora refuse anyway (PEP 668 "externally managed").</summary>\n'
    '        public static readonly string SitePackagesDir = Path.Combine(Kernel.SysDir, "pylib", "site-packages");\n')
rep(PY, '''Io.Error("modules", $"Python {version} was not found. On Windows, run 'py --list' to see installed versions.");''',
    '''Io.Error("modules", $"Python {version} was not found (looked for 'python{version}'). Run 'ls /usr/bin/python3*' on the host to see what is installed.");''')
rep(PY, 'Io.Error("modules", "Python was not found on PATH. Install Python 3 from python.org, then try again.");',
    'Io.Error("modules", "Python was not found on PATH. " + InstallHint("python"));')
rep(PY, 'Io.Error("modules", "pip is not available for this Python install.");',
    'Io.Error("modules", "pip is not available for this Python install. " + InstallHint("pip"));')
rep(PY, "            Directory.CreateDirectory(ModulesDir);\n",
    "            Directory.CreateDirectory(ModulesDir);\n            Directory.CreateDirectory(SitePackagesDir);\n")
rep(PY, 'if (!await RunOk(python, $"-m pip install {package}"))',
    'if (!await RunOk(python, $"-m pip install --disable-pip-version-check --no-warn-script-location --target \\"{SitePackagesDir}\\" {package}"))')
rep(PY, '''                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                await process.WaitForExitAsync();
                return process.ExitCode == 0;''', r'''                    CreateNoWindow = true
                };
                // pip show / pip install must see what earlier installs put in SitePackagesDir.
                psi.EnvironmentVariables["PYTHONPATH"] = SitePackagesDir;
                psi.EnvironmentVariables["PIP_DISABLE_PIP_VERSION_CHECK"] = "1";
                using var process = Process.Start(psi);
                // Drain both pipes: a chatty pip install used to fill the buffer and hang here.
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                return process.ExitCode == 0;''')
rep(PY, "                ? PyLibDir\n                : PyLibDir + Path.PathSeparator + existingPythonPath;",
    "                ? PyLibDir + Path.PathSeparator + SitePackagesDir\n"
    "                : PyLibDir + Path.PathSeparator + SitePackagesDir + Path.PathSeparator + existingPythonPath;")
rep(PY, "        // ---- helpers", r'''        private static string InstallHint(string what)
        {
            bool pipOnly = what == "pip";
            if (HostPlatform.IsTermux) return "In Termux: pkg install python";
            if (HostPlatform.FindExecutable("apt-get") != null)
                return pipOnly ? "Debian/Ubuntu: sudo apt install python3-pip" : "Debian/Ubuntu: sudo apt install python3 python3-pip";
            if (HostPlatform.FindExecutable("dnf") != null)
                return pipOnly ? "Fedora: sudo dnf install python3-pip" : "Fedora: sudo dnf install python3 python3-pip";
            if (HostPlatform.FindExecutable("pacman") != null)
                return pipOnly ? "Arch: sudo pacman -S python-pip" : "Arch: sudo pacman -S python python-pip";
            if (HostPlatform.FindExecutable("apk") != null)
                return pipOnly ? "Alpine: sudo apk add py3-pip" : "Alpine: sudo apk add python3 py3-pip";
            if (HostPlatform.FindExecutable("zypper") != null)
                return "openSUSE: sudo zypper install python3 python3-pip";
            return "Install Python 3 and pip with your distro's package manager.";
        }

        // ---- helpers''')

# ============================================================== Update/UpdateSystem.cs
U = "Update/UpdateSystem.cs"
rep(U, "using System.IO.Compression;\n", "using System.IO.Compression;\nusing System.Formats.Tar;\n")
rep(U, "using System.Text.Json;\n", "using System.Runtime.InteropServices;\nusing System.Text.Json;\n")
rep(U, '''                    var zip = assets.EnumerateArray()
                        .FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                    var chosen = zip.ValueKind == JsonValueKind.Object ? zip : assets[0];
                    info.DownloadUrl = chosen.GetProperty("browser_download_url").GetString();
                    info.ArchiveFileName = chosen.GetProperty("name").GetString();''', r'''                    // Only ever pick a build that runs here - never fall back to a Windows zip.
                    var chosen = PickLinuxAsset(assets);
                    if (chosen.ValueKind == JsonValueKind.Object)
                    {
                        info.DownloadUrl = chosen.GetProperty("browser_download_url").GetString();
                        info.ArchiveFileName = chosen.GetProperty("name").GetString();
                    }''')
rep(U, "        public static async Task RunUpdateProcess(string channel)\n", r'''        /// <summary>
        /// Picks the release asset for this machine. Self-contained builds are named
        /// *-linux-x64 / *-linux-arm64 (.tar.gz, .tgz or .zip); *-portable is framework-dependent
        /// (needs a dotnet runtime) and is the only kind that runs in Termux, where glibc
        /// builds can't.
        /// </summary>
        private static JsonElement PickLinuxAsset(JsonElement assets)
        {
            string[] archNames = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => new[] { "arm64", "aarch64" },
                Architecture.Arm => new[] { "arm32", "armhf", "armv7" },
                Architecture.X86 => new[] { "x86", "i686" },
                _ => new[] { "x64", "amd64", "x86_64" }
            };

            JsonElement best = default;
            int bestScore = 0;
            foreach (var asset in assets.EnumerateArray())
            {
                string name = ((asset.TryGetProperty("name", out var n) ? n.GetString() : null) ?? "").ToLowerInvariant();
                bool archive = name.EndsWith(".tar.gz") || name.EndsWith(".tgz") || name.EndsWith(".zip");
                if (!archive) continue;

                int score = 0;
                if (name.Contains("portable")) score = HostPlatform.IsTermux ? 200 : 50;
                else if (name.Contains("linux") && archNames.Any(a => name.Contains(a)) && !HostPlatform.IsTermux) score = 100;
                if (score == 0) continue;
                if (!name.EndsWith(".zip")) score++;

                if (score > bestScore) { bestScore = score; best = asset; }
            }
            return best;
        }

        public static async Task RunUpdateProcess(string channel)
''')
rep(U, '''            channel = channel == BetaChannel ? BetaChannel : StableChannel;
            SystemLogger.Log("PACMAN", $"Checking for updates on the {channel} channel");''', r'''            channel = channel == BetaChannel ? BetaChannel : StableChannel;

            // Installed with the styleos Python tool (or any git checkout): update means
            // git pull + rebuild, releases aren't involved at all.
            if (SourceUpdater.IsSourceInstall)
            {
                await SourceUpdater.Run();
                return;
            }

            SystemLogger.Log("PACMAN", $"Checking for updates on the {channel} channel");''')
rep(U, 'Io.Error("pacman", "this release has no downloadable artifact attached");',
    'Io.Error("pacman", "this release has no build for this machine attached (expected *-linux-<arch>.tar.gz or *-portable.tar.gz) - reinstall from source with \'styleos install\'");')
between(U, "        private static async Task Install(ReleaseInfo release)", "        /// <summary>\n        /// Semver-ish", r'''        private static async Task Install(ReleaseInfo release)
        {
            string work = Path.Combine(Path.GetTempPath(), $"styleos_update_{Environment.ProcessId}");
            string archivePath = Path.Combine(work, release.ArchiveFileName ?? "update.bin");
            string extractPath = Path.Combine(work, "extracted");

            try
            {
                using var client = CreateClient();

                Console.WriteLine($":: Downloading {release.Tag} ({release.ArchiveFileName})...");
                byte[] data = await client.GetByteArrayAsync(release.DownloadUrl);

                if (!string.IsNullOrEmpty(release.SumsUrl) && !string.IsNullOrEmpty(release.SignatureUrl))
                {
                    Console.WriteLine(":: Verifying release signature...");
                    byte[] sums = await client.GetByteArrayAsync(release.SumsUrl);
                    byte[] signature = await client.GetByteArrayAsync(release.SignatureUrl);

                    var status = ReleaseVerifier.Verify(data, release.ArchiveFileName, sums, signature);
                    if (status == SignatureStatus.Invalid)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(":: SIGNATURE CHECK FAILED.");
                        Console.WriteLine("   This download does not match its published signature - refusing to install it.");
                        Console.WriteLine("   Do not trust this file. If this keeps happening, please report it.");
                        Console.ResetColor();
                        SystemLogger.Log("PACMAN", $"Signature verification FAILED for {release.Tag}");
                        return;
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("   signature verified.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(":: This release isn't signed - proceeding without verification.");
                    Console.ResetColor();
                }

                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(extractPath);
                File.WriteAllBytes(archivePath, data);
                Console.WriteLine($":: Downloaded {PathUtil.HumanSize(data.Length)}, extracting...");

                ExtractArchive(archivePath, extractPath);

                // A release archive often wraps everything in one folder - unwrap it so the
                // files land next to the binary instead of in a nested directory.
                var entries = Directory.GetFileSystemEntries(extractPath);
                string source = entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : extractPath;

                int replaced = ApplyStagedFiles(source, Kernel.BaseDir);
                try { Directory.Delete(work, true); } catch { }

                SystemLogger.Log("PACMAN", $"Installed {release.Tag} ({replaced} files)");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($":: {release.Tag} installed ({replaced} files). Restarting...");
                Console.ResetColor();
                Thread.Sleep(800);
                Restart();
            }
            catch (UnauthorizedAccessException ex)
            {
                Io.Error("pacman", $"no write access to {Kernel.BaseDir} ({ex.Message}) - reinstall StyleOS into a folder you own");
            }
            catch (Exception ex)
            {
                Io.Error("pacman", $"update failed: {ex.Message}");
                SystemLogger.Log("PACMAN", "Update failed: " + ex.Message);
            }
        }

        private static void ExtractArchive(string archive, string destination)
        {
            string lower = archive.ToLowerInvariant();
            if (lower.EndsWith(".zip")) { ZipFile.ExtractToDirectory(archive, destination, true); return; }

            using var file = File.OpenRead(archive);
            using Stream stream = lower.EndsWith(".gz") || lower.EndsWith(".tgz")
                ? new GZipStream(file, CompressionMode.Decompress)
                : file;
            TarFile.ExtractToDirectory(stream, destination, true);
        }

        /// <summary>
        /// Copies every staged file over the install. Each file is written next to its target
        /// and then renamed onto it: the rename swaps the directory entry atomically, so the
        /// running StyleOS keeps its old (still open) copy and nothing is ever half-written.
        /// </summary>
        public static int ApplyStagedFiles(string sourceDir, string targetDir)
        {
            int count = 0;
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                string target = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));

                string temp = target + ".styleos-new";
                File.Copy(file, temp, true);
                if (!OperatingSystem.IsWindows())
                {
                    try { File.SetUnixFileMode(temp, File.GetUnixFileMode(file)); } catch { }
                }
                File.Move(temp, target, true);
                count++;
            }

            if (!OperatingSystem.IsWindows())
            {
                string apphost = Path.Combine(targetDir, "StyleOS");
                try
                {
                    if (File.Exists(apphost))
                        File.SetUnixFileMode(apphost, File.GetUnixFileMode(apphost)
                            | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                }
                catch { }
            }
            return count;
        }

        /// <summary>
        /// Starts the freshly installed StyleOS in this same terminal and exits with its code.
        /// (The Windows build did this with a .bat file and a second console window.)
        /// </summary>
        public static void Restart()
        {
            try
            {
                ConsoleHost.Reset();
                string processPath = Environment.ProcessPath ?? "";
                string apphost = Path.Combine(Kernel.BaseDir, "StyleOS");
                string dll = Path.Combine(Kernel.BaseDir, "StyleOS.dll");

                var psi = new ProcessStartInfo { UseShellExecute = false };
                bool viaDotnet = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                                 || !File.Exists(apphost);
                if (viaDotnet)
                {
                    psi.FileName = string.IsNullOrEmpty(processPath) ? "dotnet" : processPath;
                    psi.ArgumentList.Add(dll);
                }
                else psi.FileName = apphost;

                foreach (var arg in Environment.GetCommandLineArgs().Skip(1)) psi.ArgumentList.Add(arg);

                try { Console.TreatControlCAsInput = false; } catch { }
                using var child = Process.Start(psi);
                child.WaitForExit();
                Environment.Exit(child.ExitCode);
            }
            catch (Exception ex)
            {
                Io.Error("pacman", $"installed, but could not restart automatically ({ex.Message}) - start StyleOS again");
                Kernel.IsRunning = false;
                Kernel.CurrentUser = null;
            }
        }

''')

# ============================================================== normalize + write
for p in sorted(ROOT.rglob("*.cs")):
    rel = p.relative_to(ROOT).as_posix()
    if rel.startswith(("bin/", "obj/", "out/")):
        continue
    text(rel)
text("README.md")

for path, t in files.items():
    for bad in ("WmiValue", "System.Management", "System.Drawing", "new Bitmap(", "styleos_updater.bat", "xcopy "):
        if bad in t:
            errors.append(f"{path}: Windows leftover still present: {bad}")

if errors:
    print("PORT FAILED - nothing was written:")
    for e in errors:
        print("  - " + e)
    sys.exit(1)

for path, t in files.items():
    (ROOT / path).write_text(t, encoding="utf-8", newline="\n")

print(f"Linux port applied: {len(files)} files normalized/edited, 0 errors")
