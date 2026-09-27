using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StyleOS
{
    public static class PathUtil
    {
        public static string Resolve(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return Kernel.CurrentDirectory;

            target = target.Replace('/', Path.DirectorySeparatorChar);

            if (target == "~") return Kernel.Home;
            if (target.StartsWith("~" + Path.DirectorySeparatorChar))
                return Path.GetFullPath(Path.Combine(Kernel.Home, target.Substring(2)));

            if (target == "-") return ShellEnv.PreviousDirectory ?? Kernel.CurrentDirectory;

            try { return Path.GetFullPath(Path.Combine(Kernel.CurrentDirectory, target)); }
            catch { return target; }
        }

        /// <summary>Path as shown in the prompt: home collapses to ~, separators are unix style.</summary>
        public static string Display(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            string home = Kernel.Home;

            if (!string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                path = "~" + path.Substring(home.Length);

            return path.Replace('\\', '/');
        }

        public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        /// <summary>Expands simple * and ? patterns; returns the original word when nothing matches.</summary>
        public static List<string> Glob(string pattern)
        {
            var result = new List<string>();
            if (pattern.IndexOf('*') < 0 && pattern.IndexOf('?') < 0) { result.Add(pattern); return result; }

            try
            {
                string full = Resolve(pattern);
                string dir = Path.GetDirectoryName(full);
                string mask = Path.GetFileName(full);
                if (string.IsNullOrEmpty(dir)) dir = Kernel.CurrentDirectory;
                if (!Directory.Exists(dir)) { result.Add(pattern); return result; }

                foreach (var entry in Directory.GetFileSystemEntries(dir, mask).OrderBy(x => x))
                    result.Add(entry);
            }
            catch { }

            if (result.Count == 0) result.Add(pattern);
            return result;
        }

        public static string HumanSize(long bytes)
        {
            string[] units = { "B", "K", "M", "G", "T", "P" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return unit == 0 ? $"{bytes}{units[0]}" : $"{size:0.#}{units[unit]}";
        }
    }

    /// <summary>Helpers every text filter needs: read from files or from the pipe.</summary>
    public static class Io
    {
        public static void Error(string command, string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{command}: {message}");
            Console.ResetColor();
            ShellEnv.ExitCode = 1;
        }

        public static void Info(string message, ConsoleColor color = ConsoleColor.Cyan)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        /// <summary>
        /// Reads the input of a filter: every file argument, or the pipe when there are none.
        /// Returns null when there is nothing to read.
        /// </summary>
        /// <summary>
        /// Splits piped text into lines the way File.ReadAllLines splits a file: a single
        /// trailing newline does not create a phantom empty last line. Every command that
        /// reads ShellEnv.StdIn line-by-line goes through this instead of a raw Split('\n'),
        /// so piped input behaves the same as reading the equivalent file would.
        /// </summary>
        public static string[] SplitStdin(string text)
        {
            string normalized = text.Replace("\r\n", "\n");
            if (normalized.EndsWith("\n")) normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized.Length == 0 ? new string[0] : normalized.Split('\n');
        }

        public static string[] ReadInput(string command, List<string> fileArgs)
        {
            var files = fileArgs?.Where(a => !a.StartsWith("-")).ToList() ?? new List<string>();

            if (files.Count == 0)
            {
                if (ShellEnv.StdIn == null)
                {
                    Error(command, "missing file operand (or pipe something into it)");
                    return null;
                }
                return SplitStdin(ShellEnv.StdIn);
            }

            var lines = new List<string>();
            foreach (var arg in files)
            {
                foreach (var candidate in PathUtil.Glob(arg))
                {
                    string path = PathUtil.Resolve(candidate);
                    if (!File.Exists(path)) { Error(command, $"{candidate}: No such file or directory"); continue; }
                    try { lines.AddRange(File.ReadAllLines(path)); }
                    catch (Exception ex) { Error(command, ex.Message); }
                }
            }
            return lines.ToArray();
        }

        public static void WriteLines(IEnumerable<string> lines)
        {
            foreach (var line in lines) Console.WriteLine(line);
        }

        public static bool HasFlag(List<string> args, params string[] flags)
        {
            foreach (var a in args)
            {
                if (a.Length < 2 || a[0] != '-') continue;
                foreach (var f in flags)
                {
                    if (a == f) return true;
                    // short flags can be bundled: -la contains -l and -a
                    if (f.Length == 2 && f[0] == '-' && !a.StartsWith("--") && a.Contains(f[1])) return true;
                }
            }
            return false;
        }

        public static List<string> Operands(List<string> args) => args.Where(a => !a.StartsWith("-")).ToList();

        /// <summary>
        /// Removes each occurrence of a value-taking flag AND the token right after it
        /// (e.g. "-d" "," or "-O" "file.txt") before the caller computes positional operands.
        /// Without this, a flag's own value - which usually isn't dash-prefixed - gets
        /// mistaken for a file name or URL by Operands()/ReadInput().
        /// </summary>
        public static List<string> StripOptionValues(List<string> args, params string[] valueFlags)
        {
            var result = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                if (valueFlags.Contains(args[i]) && i + 1 < args.Count) { i++; continue; }
                result.Add(args[i]);
            }
            return result;
        }
    }
}
