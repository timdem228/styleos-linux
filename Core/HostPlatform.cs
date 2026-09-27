using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace StyleOS
{
    /// <summary>
    /// Everything StyleOS asks the host Linux (or Termux / Android) system: /proc and /sys
    /// readers, host program lookup and execution, opening URLs. This replaces the WMI and
    /// Win32 calls the Windows build used.
    /// </summary>
    public static class HostPlatform
    {
        public static bool IsLinux => OperatingSystem.IsLinux();

        /// <summary>Termux sets TERMUX_VERSION; older versions only give away the prefix path.</summary>
        public static bool IsTermux =>
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERMUX_VERSION")) ||
            (Environment.GetEnvironmentVariable("PREFIX") ?? "").Contains("com.termux");

        // ---- files -----------------------------------------------------------------

        public static string ReadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }

        public static string ReadFirstLine(string path)
        {
            string text = ReadText(path);
            if (text == null) return null;
            int nl = text.IndexOf('\n');
            return (nl >= 0 ? text.Substring(0, nl) : text).Trim().Trim('\0');
        }

        // ---- host programs ---------------------------------------------------------

        /// <summary>Full path of an executable on the host PATH, or null.</summary>
        public static string FindExecutable(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (name.Contains('/')) return IsExecutable(name) ? name : null;

            string path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir, name);
                    if (IsExecutable(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }

        public static bool IsExecutable(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                if (OperatingSystem.IsWindows()) return true;
                var mode = File.GetUnixFileMode(path);
                return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch { return false; }
        }

        /// <summary>Runs a host program, returns its stdout - or null when it is missing, fails or times out.</summary>
        public static string Run(string exe, params string[] args) => Run(exe, 8000, args);

        public static string Run(string exe, int timeoutMs, params string[] args)
        {
            string full = FindExecutable(exe);
            if (full == null) return null;
            try
            {
                var psi = new ProcessStartInfo(full)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                using var p = Process.Start(psi);
                p.StandardInput.Close();
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();

                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(true); } catch { }
                    return null;
                }
                p.WaitForExit();
                return p.ExitCode == 0 ? stdout.Result : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Runs a host program and echoes its output line by line through Console, so it
        /// still works inside StyleOS pipes and redirections. Ctrl+C stops it.
        /// Returns the exit code, or -1 when it could not be started.
        /// </summary>
        public static int RunStreaming(string exe, params string[] args)
        {
            string full = FindExecutable(exe);
            if (full == null) return -1;
            try
            {
                var psi = new ProcessStartInfo(full)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                using var p = new Process { StartInfo = psi };
                var sync = new object();
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sync) Console.WriteLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sync) Console.WriteLine(e.Data); };

                Kernel.CancelRequested = false;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                while (!p.WaitForExit(100))
                {
                    bool cancel = Kernel.CancelRequested;
                    if (!cancel && ConsoleHost.KeyReady())
                    {
                        var key = Console.ReadKey(true);
                        cancel = key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control);
                    }
                    if (cancel)
                    {
                        try { p.Kill(true); } catch { }
                        Kernel.CancelRequested = false;
                        lock (sync) Console.WriteLine("^C");
                        p.WaitForExit();
                        return 130;
                    }
                }

                p.WaitForExit();
                return p.ExitCode;
            }
            catch { return -1; }
        }

        /// <summary>Runs a host program attached to the real terminal (ssh and friends).</summary>
        public static int RunInteractive(string exe, params string[] args)
        {
            string full = FindExecutable(exe);
            if (full == null) return 127;

            bool treat = false;
            try { treat = Console.TreatControlCAsInput; Console.TreatControlCAsInput = false; } catch { }
            try
            {
                var psi = new ProcessStartInfo(full) { UseShellExecute = false };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                p.WaitForExit();
                return p.ExitCode;
            }
            catch (Exception ex)
            {
                Io.Error(exe, ex.Message);
                return 126;
            }
            finally
            {
                try { Console.TreatControlCAsInput = treat; } catch { }
                Kernel.CancelRequested = false;
            }
        }

        /// <summary>
        /// Opens a URL or file with whatever the host has: termux-open(-url) in Termux,
        /// xdg-open / gio / sensible-browser on desktop Linux, wslview under WSL.
        /// Returns false when nothing could open it (headless boxes, bare containers).
        /// </summary>
        public static bool OpenExternal(string target)
        {
            bool isUrl = target.StartsWith("http://") || target.StartsWith("https://") || target.StartsWith("mailto:");
            var openers = new List<string>();
            if (IsTermux) openers.Add(isUrl ? "termux-open-url" : "termux-open");
            openers.AddRange(new[] { "xdg-open", "gio", "wslview", "sensible-browser" });

            foreach (var opener in openers)
            {
                string full = FindExecutable(opener);
                if (full == null) continue;
                try
                {
                    var psi = new ProcessStartInfo(full)
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    if (opener == "gio") psi.ArgumentList.Add("open");
                    psi.ArgumentList.Add(target);

                    using var p = Process.Start(psi);
                    _ = p.StandardOutput.ReadToEndAsync();
                    _ = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(5000) || p.ExitCode == 0) return true;
                }
                catch { }
            }
            return false;
        }

        // ---- system information ----------------------------------------------------

        public static string GetProp(string key) => Run("getprop", 3000, key)?.Trim();

        /// <summary>"Ubuntu 24.04 LTS", "Arch Linux", "Android 14 (Termux)"...</summary>
        public static string OsPrettyName()
        {
            if (IsTermux)
            {
                string release = GetProp("ro.build.version.release");
                return string.IsNullOrEmpty(release) ? "Android (Termux)" : $"Android {release} (Termux)";
            }

            string osRelease = ReadText("/etc/os-release") ?? ReadText("/usr/lib/os-release");
            if (osRelease != null)
            {
                foreach (var line in osRelease.Split('\n'))
                    if (line.StartsWith("PRETTY_NAME=")) return line.Substring(12).Trim().Trim('"');
            }
            return System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        }

        public static string KernelRelease() =>
            ReadFirstLine("/proc/sys/kernel/osrelease") ?? Environment.OSVersion.Version.ToString();

        public static string TerminalName()
        {
            string Env(string n) => Environment.GetEnvironmentVariable(n);

            if (IsTermux) return "Termux";
            if (!string.IsNullOrEmpty(Env("WT_SESSION"))) return "Windows Terminal (WSL)";
            if (!string.IsNullOrEmpty(Env("TERM_PROGRAM"))) return Env("TERM_PROGRAM");
            if (!string.IsNullOrEmpty(Env("KONSOLE_VERSION"))) return "Konsole";
            if (!string.IsNullOrEmpty(Env("KITTY_WINDOW_ID"))) return "kitty";
            if (!string.IsNullOrEmpty(Env("ALACRITTY_WINDOW_ID")) || !string.IsNullOrEmpty(Env("ALACRITTY_LOG"))) return "Alacritty";
            if (!string.IsNullOrEmpty(Env("GNOME_TERMINAL_SCREEN")) || !string.IsNullOrEmpty(Env("VTE_VERSION"))) return "GNOME Terminal (VTE)";
            if (!string.IsNullOrEmpty(Env("TMUX"))) return "tmux";
            if (!string.IsNullOrEmpty(Env("SSH_TTY"))) return $"ssh ({Env("TERM") ?? "tty"})";
            return string.IsNullOrEmpty(Env("TERM")) ? "console" : Env("TERM");
        }

        public static string CpuModel()
        {
            string info = ReadText("/proc/cpuinfo");
            if (info != null)
            {
                var lines = info.Split('\n');
                foreach (var key in new[] { "model name", "Hardware", "cpu model", "Processor", "uarch" })
                {
                    foreach (var line in lines)
                    {
                        int colon = line.IndexOf(':');
                        if (colon <= 0) continue;
                        if (!line.Substring(0, colon).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                        string value = line.Substring(colon + 1).Trim();
                        if (value.Length > 0) return Regex.Replace(value, @"\s+", " ");
                    }
                }
            }

            if (IsTermux)
            {
                string soc = GetProp("ro.soc.model");
                if (string.IsNullOrEmpty(soc)) soc = GetProp("ro.board.platform");
                if (!string.IsNullOrEmpty(soc)) return soc;
            }

            string lscpu = Run("lscpu", 3000);
            if (lscpu != null)
            {
                foreach (var line in lscpu.Split('\n'))
                    if (line.StartsWith("Model name:")) return line.Substring(11).Trim();
            }
            return null;
        }

        public static string Virtualization()
        {
            string info = ReadText("/proc/cpuinfo") ?? "";
            if (Regex.IsMatch(info, @"\bvmx\b")) return "VT-x";
            if (Regex.IsMatch(info, @"\bsvm\b")) return "AMD-V";
            return "none";
        }

        public static string GpuName()
        {
            string lspci = Run("lspci", 4000);
            if (!string.IsNullOrEmpty(lspci))
            {
                foreach (var line in lspci.Split('\n'))
                {
                    int idx = line.IndexOf("VGA compatible controller: ", StringComparison.Ordinal);
                    int len = "VGA compatible controller: ".Length;
                    if (idx < 0) { idx = line.IndexOf("3D controller: ", StringComparison.Ordinal); len = "3D controller: ".Length; }
                    if (idx < 0) { idx = line.IndexOf("Display controller: ", StringComparison.Ordinal); len = "Display controller: ".Length; }
                    if (idx >= 0) return line.Substring(idx + len).Trim();
                }
            }

            if (IsTermux)
            {
                string egl = GetProp("ro.hardware.egl");
                if (string.IsNullOrEmpty(egl)) egl = GetProp("ro.hardware.vulkan");
                if (!string.IsNullOrEmpty(egl)) return egl;
            }

            try
            {
                if (Directory.Exists("/sys/class/drm"))
                {
                    foreach (var card in Directory.GetDirectories("/sys/class/drm", "card?").OrderBy(c => c))
                    {
                        string vendor = ReadFirstLine(Path.Combine(card, "device", "vendor"));
                        if (string.IsNullOrEmpty(vendor)) continue;

                        string driver = null;
                        try { driver = Path.GetFileName(new DirectoryInfo(Path.Combine(card, "device", "driver")).LinkTarget ?? ""); }
                        catch { }

                        string vendorName = vendor switch
                        {
                            "0x10de" => "NVIDIA",
                            "0x1002" => "AMD",
                            "0x8086" => "Intel",
                            "0x1af4" => "VirtIO",
                            "0x15ad" => "VMware",
                            "0x1234" => "QEMU",
                            _ => vendor
                        };
                        return string.IsNullOrEmpty(driver) ? $"{vendorName} GPU" : $"{vendorName} GPU ({driver})";
                    }
                }
            }
            catch { }
            return null;
        }

        public static string HostModel()
        {
            if (IsTermux)
            {
                string both = $"{GetProp("ro.product.manufacturer")} {GetProp("ro.product.model")}".Trim();
                if (both.Length > 0) return both;
            }

            string product = ReadFirstLine("/sys/devices/virtual/dmi/id/product_name");
            string vendor = ReadFirstLine("/sys/devices/virtual/dmi/id/sys_vendor");
            if (!string.IsNullOrWhiteSpace(product) && !product.Contains("O.E.M"))
            {
                return string.IsNullOrWhiteSpace(vendor) || product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)
                    ? product
                    : $"{vendor} {product}";
            }

            string dt = ReadFirstLine("/sys/firmware/devicetree/base/model") ?? ReadFirstLine("/proc/device-tree/model");
            return string.IsNullOrWhiteSpace(dt) ? null : dt;
        }

        /// <summary>(total, available) memory in MiB from /proc/meminfo, zeros when unreadable.</summary>
        public static (long TotalMb, long AvailableMb) MemInfo()
        {
            var values = ReadMemInfo();
            if (!values.TryGetValue("MemTotal", out long total)) return (0, 0);

            if (!values.TryGetValue("MemAvailable", out long available))
            {
                values.TryGetValue("MemFree", out long free);
                values.TryGetValue("Buffers", out long buffers);
                values.TryGetValue("Cached", out long cached);
                available = free + buffers + cached;
            }
            return (total / 1024, available / 1024);
        }

        public static (long TotalMb, long FreeMb) SwapInfo()
        {
            var values = ReadMemInfo();
            values.TryGetValue("SwapTotal", out long total);
            values.TryGetValue("SwapFree", out long free);
            return (total / 1024, free / 1024);
        }

        private static Dictionary<string, long> ReadMemInfo()
        {
            var result = new Dictionary<string, long>();
            string text = ReadText("/proc/meminfo");
            if (text == null) return result;

            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split(new[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out long kb)) result[parts[0]] = kb;
            }
            return result;
        }

        public static string LoadAverage()
        {
            string line = ReadFirstLine("/proc/loadavg");
            if (string.IsNullOrEmpty(line)) return "0.00, 0.00, 0.00";
            var p = line.Split(' ');
            return p.Length >= 3 ? $"{p[0]}, {p[1]}, {p[2]}" : "0.00, 0.00, 0.00";
        }
    }
}
