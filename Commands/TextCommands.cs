using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StyleOS
{
    public static class TextCommands
    {
        public static void Cat(List<string> args)
        {
            var lines = Io.ReadInput("cat", args);
            if (lines == null) return;

            bool number = Io.HasFlag(args, "-n");
            for (int i = 0; i < lines.Length; i++)
                Console.WriteLine(number ? $"{i + 1,6}  {lines[i]}" : lines[i]);
        }

        public static void Head(List<string> args)
        {
            int count = ParseCount(args, 10);
            var lines = Io.ReadInput("head", StripCount(args));
            if (lines == null) return;
            foreach (var l in lines.Take(count)) Console.WriteLine(l);
        }

        public static void Tail(List<string> args)
        {
            int count = ParseCount(args, 10);
            var lines = Io.ReadInput("tail", StripCount(args));
            if (lines == null) return;
            foreach (var l in lines.Skip(Math.Max(0, lines.Length - count))) Console.WriteLine(l);
        }

        private static int ParseCount(List<string> args, int fallback)
        {
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "-n" && i + 1 < args.Count && int.TryParse(args[i + 1], out int n)) return n;
                if (args[i].StartsWith("-") && args[i].Length > 1 && int.TryParse(args[i].Substring(1), out int m)) return m;
            }
            return fallback;
        }

        private static List<string> StripCount(List<string> args)
        {
            var result = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "-n") { i++; continue; }
                if (args[i].StartsWith("-")) continue;
                result.Add(args[i]);
            }
            return result;
        }

        public static void Wc(List<string> args)
        {
            var lines = Io.ReadInput("wc", args);
            if (lines == null) return;

            int lineCount = lines.Length;
            int wordCount = lines.Sum(l => l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length);
            int charCount = lines.Sum(l => l.Length + 1);

            bool l = Io.HasFlag(args, "-l"), w = Io.HasFlag(args, "-w"), c = Io.HasFlag(args, "-c", "-m");

            if (l && !w && !c) { Console.WriteLine(lineCount); return; }
            if (w && !l && !c) { Console.WriteLine(wordCount); return; }
            if (c && !l && !w) { Console.WriteLine(charCount); return; }

            Console.WriteLine($"{lineCount,8}{wordCount,8}{charCount,8} {string.Join(" ", Io.Operands(args))}".TrimEnd());
        }

        public static void Grep(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("grep", "usage: grep [-invcr] PATTERN [FILE...]"); return; }

            string pattern = operands[0];
            var files = operands.Skip(1).ToList();

            bool ignoreCase = Io.HasFlag(args, "-i");
            bool invert = Io.HasFlag(args, "-v");
            bool showNumbers = Io.HasFlag(args, "-n");
            bool countOnly = Io.HasFlag(args, "-c");
            bool recursive = Io.HasFlag(args, "-r", "-R");

            Regex regex;
            try
            {
                regex = new Regex(pattern, ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            }
            catch
            {
                regex = new Regex(Regex.Escape(pattern), ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            }

            if (recursive)
            {
                string root = files.Count > 0 ? PathUtil.Resolve(files[0]) : Kernel.CurrentDirectory;
                try
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                    foreach (var file in Directory.EnumerateFiles(root, "*", options))
                        GrepFile(file, regex, invert, showNumbers, countOnly, true);
                }
                catch (Exception ex) { Io.Error("grep", ex.Message); }
                return;
            }

            if (files.Count == 0)
            {
                if (ShellEnv.StdIn == null) { Io.Error("grep", "missing file operand"); return; }
                GrepLines(Io.SplitStdin(ShellEnv.StdIn), regex, invert, showNumbers, countOnly, null);
                return;
            }

            foreach (var f in files)
                foreach (var candidate in PathUtil.Glob(f))
                    GrepFile(PathUtil.Resolve(candidate), regex, invert, showNumbers, countOnly, files.Count > 1);
        }

        private static void GrepFile(string path, Regex regex, bool invert, bool numbers, bool countOnly, bool withName)
        {
            if (!File.Exists(path)) { Io.Error("grep", $"{PathUtil.Display(path)}: No such file or directory"); return; }
            try
            {
                GrepLines(File.ReadAllLines(path), regex, invert, numbers, countOnly, withName ? PathUtil.Display(path) : null);
            }
            catch { }
        }

        private static void GrepLines(string[] lines, Regex regex, bool invert, bool numbers, bool countOnly, string name)
        {
            int hits = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                bool match = regex.IsMatch(lines[i]);
                if (invert) match = !match;
                if (!match) continue;
                hits++;
                if (countOnly) continue;

                if (name != null) { Console.ForegroundColor = ConsoleColor.Magenta; Console.Write(name + ":"); Console.ResetColor(); }
                if (numbers) { Console.ForegroundColor = ConsoleColor.Green; Console.Write((i + 1) + ":"); Console.ResetColor(); }

                if (invert) { Console.WriteLine(lines[i]); continue; }

                int last = 0;
                foreach (Match m in regex.Matches(lines[i]))
                {
                    Console.Write(lines[i].Substring(last, m.Index - last));
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write(m.Value);
                    Console.ResetColor();
                    last = m.Index + m.Length;
                    if (m.Length == 0) break;
                }
                Console.WriteLine(lines[i].Substring(last));
            }

            if (countOnly) Console.WriteLine(name != null ? $"{name}:{hits}" : hits.ToString());
            if (hits == 0) ShellEnv.ExitCode = 1;
        }

        public static void Sort(List<string> args)
        {
            var lines = Io.ReadInput("sort", args);
            if (lines == null) return;

            IEnumerable<string> result = lines;
            if (Io.HasFlag(args, "-n"))
                result = lines.OrderBy(l => double.TryParse(l.Trim(), out double d) ? d : double.MaxValue).ThenBy(l => l);
            else
                result = lines.OrderBy(l => l, StringComparer.OrdinalIgnoreCase);

            if (Io.HasFlag(args, "-r")) result = result.Reverse();
            if (Io.HasFlag(args, "-u")) result = result.Distinct();

            Io.WriteLines(result);
        }

        public static void Uniq(List<string> args)
        {
            var lines = Io.ReadInput("uniq", args);
            if (lines == null) return;

            bool count = Io.HasFlag(args, "-c");
            bool onlyDuplicates = Io.HasFlag(args, "-d");

            string previous = null;
            int run = 0;
            var output = new List<string>();

            foreach (var line in lines.Concat(new string[] { null }))
            {
                if (previous != null && line == previous) { run++; continue; }
                if (previous != null)
                {
                    if (!onlyDuplicates || run > 1)
                        output.Add(count ? $"{run,7} {previous}" : previous);
                }
                previous = line;
                run = 1;
            }
            Io.WriteLines(output);
        }

        public static void Cut(List<string> args)
        {
            string delimiter = "\t";
            string fields = null;
            string chars = null;

            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "-d" && i + 1 < args.Count) delimiter = args[i + 1];
                else if (args[i].StartsWith("-d") && args[i].Length > 2) delimiter = args[i].Substring(2);
                else if (args[i] == "-f" && i + 1 < args.Count) fields = args[i + 1];
                else if (args[i].StartsWith("-f") && args[i].Length > 2) fields = args[i].Substring(2);
                else if (args[i] == "-c" && i + 1 < args.Count) chars = args[i + 1];
                else if (args[i].StartsWith("-c") && args[i].Length > 2) chars = args[i].Substring(2);
            }

            // "-d ," and "-f 2" each take a following value that must not be mistaken
            // for the file name, so strip both tokens before finding the real file operand.
            var fileArgs = Io.StripOptionValues(args, "-d", "-f", "-c");
            var lines = Io.ReadInput("cut", fileArgs);
            if (lines == null) return;

            foreach (var line in lines)
            {
                if (chars != null)
                {
                    var picked = new StringBuilder();
                    foreach (int index in ParseRange(chars, line.Length))
                        if (index >= 1 && index <= line.Length) picked.Append(line[index - 1]);
                    Console.WriteLine(picked.ToString());
                }
                else if (fields != null)
                {
                    var parts = line.Split(new string[] { delimiter }, StringSplitOptions.None);
                    var picked = ParseRange(fields, parts.Length)
                        .Where(i => i >= 1 && i <= parts.Length)
                        .Select(i => parts[i - 1]);
                    Console.WriteLine(string.Join(delimiter, picked));
                }
                else Console.WriteLine(line);
            }
        }

        private static IEnumerable<int> ParseRange(string spec, int max)
        {
            foreach (var part in spec.Split(','))
            {
                if (part.Contains('-'))
                {
                    var bounds = part.Split('-');
                    int from = int.TryParse(bounds[0], out int f) ? f : 1;
                    int to = bounds.Length > 1 && int.TryParse(bounds[1], out int t) ? t : max;
                    for (int i = from; i <= to; i++) yield return i;
                }
                else if (int.TryParse(part, out int single)) yield return single;
            }
        }

        public static void Tr(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count < 1) { Io.Error("tr", "missing operand"); return; }
            if (ShellEnv.StdIn == null) { Io.Error("tr", "tr reads from a pipe: try 'cat file | tr a-z A-Z'"); return; }

            string text = ShellEnv.StdIn;
            bool delete = Io.HasFlag(args, "-d");

            string from = Expand(UnescapeSet(operands[0]));
            string to = operands.Count > 1 ? Expand(UnescapeSet(operands[1])) : "";

            var sb = new StringBuilder();
            foreach (char c in text)
            {
                int index = from.IndexOf(c);
                if (index < 0) { sb.Append(c); continue; }
                if (delete) continue;
                sb.Append(to.Length == 0 ? c : to[Math.Min(index, to.Length - 1)]);
            }
            Console.Write(sb.ToString());
        }

        /// <summary>
        /// tr's SET operands recognize backslash escapes (\n, \t, \\...) themselves - the
        /// shell already passed them through literally as backslash+letter (correctly, since
        /// they were single-quoted), so tr has to do this decoding on its own arguments.
        /// </summary>
        private static string UnescapeSet(string set)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < set.Length; i++)
            {
                if (set[i] == '\\' && i + 1 < set.Length)
                {
                    char next = set[i + 1];
                    char mapped = next switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '\\' => '\\',
                        'a' => '\a',
                        'b' => '\b',
                        'f' => '\f',
                        'v' => '\v',
                        '0' => '\0',
                        _ => '\0'
                    };

                    if (mapped != '\0' || next == '0')
                    {
                        sb.Append(mapped);
                        i++;
                        continue;
                    }
                }
                sb.Append(set[i]);
            }
            return sb.ToString();
        }

        private static string Expand(string set)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < set.Length; i++)
            {
                if (i + 2 < set.Length && set[i + 1] == '-')
                {
                    for (char c = set[i]; c <= set[i + 2]; c++) sb.Append(c);
                    i += 2;
                }
                else sb.Append(set[i]);
            }
            return sb.ToString();
        }

        public static void Rev(List<string> args)
        {
            var lines = Io.ReadInput("rev", args);
            if (lines == null) return;
            foreach (var line in lines) Console.WriteLine(new string(line.Reverse().ToArray()));
        }

        public static void Nl(List<string> args)
        {
            var lines = Io.ReadInput("nl", args);
            if (lines == null) return;
            int n = 1;
            foreach (var line in lines)
                Console.WriteLine(string.IsNullOrWhiteSpace(line) ? "       " : $"{n++,6}  {line}");
        }

        public static void Tee(List<string> args)
        {
            if (ShellEnv.StdIn == null) { Io.Error("tee", "nothing on stdin"); return; }
            bool append = Io.HasFlag(args, "-a");

            foreach (var target in Io.Operands(args))
            {
                string path = PathUtil.Resolve(target);
                try
                {
                    if (append) File.AppendAllText(path, ShellEnv.StdIn);
                    else File.WriteAllText(path, ShellEnv.StdIn);
                }
                catch (Exception ex) { Io.Error("tee", ex.Message); }
            }
            Console.Write(ShellEnv.StdIn);
        }

        /// <summary>Pager used by both less and more.</summary>
        public static void Pager(List<string> args)
        {
            var lines = Io.ReadInput("less", args);
            if (lines == null) return;

            int pageSize = Math.Max(1, ConsoleHost.Height - 2);
            int offset = 0;

            while (true)
            {
                Console.Clear();
                for (int i = 0; i < pageSize && offset + i < lines.Length; i++)
                    Console.WriteLine(lines[offset + i]);

                if (offset + pageSize >= lines.Length && offset == 0) return;

                Console.BackgroundColor = ConsoleColor.Gray;
                Console.ForegroundColor = ConsoleColor.Black;
                int percent = lines.Length == 0 ? 100 : Math.Min(100, (offset + pageSize) * 100 / lines.Length);
                Console.Write($":{percent}%  (space/PgDn next, b/PgUp back, q quit)");
                Console.ResetColor();

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape) break;
                if (key.Key == ConsoleKey.DownArrow || key.Key == ConsoleKey.Enter) offset = Math.Min(Math.Max(0, lines.Length - 1), offset + 1);
                else if (key.Key == ConsoleKey.UpArrow) offset = Math.Max(0, offset - 1);
                else if (key.Key == ConsoleKey.B || key.Key == ConsoleKey.PageUp) offset = Math.Max(0, offset - pageSize);
                else offset = Math.Min(Math.Max(0, lines.Length - 1), offset + pageSize);
            }
            Console.Clear();
        }

        public static void Sed(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("sed", "usage: sed 's/old/new/[g]' [file]"); return; }

            string script = operands[0];
            var lines = Io.ReadInput("sed", operands.Skip(1).ToList());
            if (lines == null) return;

            if (!script.StartsWith("s") || script.Length < 4) { Io.Error("sed", "only s/old/new/ is supported"); return; }

            char sep = script[1];
            var parts = script.Substring(2).Split(sep);
            if (parts.Length < 2) { Io.Error("sed", "malformed expression"); return; }

            string from = parts[0], to = parts[1];
            bool global = parts.Length > 2 && parts[2].Contains('g');

            foreach (var line in lines)
            {
                try
                {
                    var regex = new Regex(from);
                    Console.WriteLine(global ? regex.Replace(line, to) : regex.Replace(line, to, 1));
                }
                catch { Console.WriteLine(line.Replace(from, to)); }
            }
        }

        public static void Diff(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count < 2) { Io.Error("diff", "missing operand"); return; }

            string a = PathUtil.Resolve(operands[0]), b = PathUtil.Resolve(operands[1]);
            if (!File.Exists(a)) { Io.Error("diff", $"{operands[0]}: No such file"); return; }
            if (!File.Exists(b)) { Io.Error("diff", $"{operands[1]}: No such file"); return; }

            var left = File.ReadAllLines(a);
            var right = File.ReadAllLines(b);
            bool same = true;

            for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                string l = i < left.Length ? left[i] : null;
                string r = i < right.Length ? right[i] : null;
                if (l == r) continue;

                same = false;
                Console.WriteLine($"{i + 1}c{i + 1}");
                if (l != null) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("< " + l); }
                if (r != null) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("> " + r); }
                Console.ResetColor();
            }

            if (!same) ShellEnv.ExitCode = 1;
        }

        public static void Xxd(List<string> args)
        {
            var operands = Io.Operands(args);
            byte[] data;

            if (operands.Count == 0)
            {
                if (ShellEnv.StdIn == null) { Io.Error("xxd", "missing file operand"); return; }
                data = Encoding.UTF8.GetBytes(ShellEnv.StdIn);
            }
            else
            {
                string path = PathUtil.Resolve(operands[0]);
                if (!File.Exists(path)) { Io.Error("xxd", $"{operands[0]}: No such file"); return; }
                data = File.ReadAllBytes(path);
            }

            int limit = Math.Min(data.Length, 4096);
            for (int i = 0; i < limit; i += 16)
            {
                var hex = new StringBuilder();
                var ascii = new StringBuilder();
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < limit)
                    {
                        hex.Append(data[i + j].ToString("x2"));
                        ascii.Append(data[i + j] >= 32 && data[i + j] < 127 ? (char)data[i + j] : '.');
                    }
                    else hex.Append("  ");
                    if (j % 2 == 1) hex.Append(' ');
                }
                Console.WriteLine($"{i:x8}: {hex}  {ascii}");
            }
            if (data.Length > limit) Console.WriteLine($"... ({data.Length - limit} more bytes)");
        }

        public static void Strings(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("strings", "missing file operand"); return; }

            string path = PathUtil.Resolve(operands[0]);
            if (!File.Exists(path)) { Io.Error("strings", $"{operands[0]}: No such file"); return; }

            try
            {
                var data = File.ReadAllBytes(path);
                var current = new StringBuilder();
                foreach (byte b in data)
                {
                    if (b >= 32 && b < 127) current.Append((char)b);
                    else
                    {
                        if (current.Length >= 4) Console.WriteLine(current.ToString());
                        current.Clear();
                    }
                }
                if (current.Length >= 4) Console.WriteLine(current.ToString());
            }
            catch (Exception ex) { Io.Error("strings", ex.Message); }
        }
    }
}
