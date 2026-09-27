using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StyleOS
{
    public static class ArchiveCommands
    {
        public static void Zip(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count < 2) { Io.Error("zip", "usage: zip <archive.zip> <file-or-dir>"); return; }

            string archive = PathUtil.Resolve(operands[0]);
            if (!archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) archive += ".zip";

            try
            {
                if (File.Exists(archive)) File.Delete(archive);

                using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
                foreach (var operand in operands.Skip(1))
                {
                    foreach (var candidate in PathUtil.Glob(operand))
                    {
                        string path = PathUtil.Resolve(candidate);
                        if (File.Exists(path))
                        {
                            zip.CreateEntryFromFile(path, Path.GetFileName(path));
                            Console.WriteLine($"  adding: {Path.GetFileName(path)}");
                        }
                        else if (Directory.Exists(path))
                        {
                            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                            {
                                string entry = Path.GetRelativePath(Path.GetDirectoryName(path), file).Replace('\\', '/');
                                zip.CreateEntryFromFile(file, entry);
                                Console.WriteLine($"  adding: {entry}");
                            }
                        }
                        else Io.Error("zip", $"{candidate}: No such file or directory");
                    }
                }
            }
            catch (Exception ex) { Io.Error("zip", ex.Message); }
        }

        public static void Unzip(List<string> args)
        {
            var operands = Io.Operands(Io.StripOptionValues(args, "-d"));
            if (operands.Count == 0) { Io.Error("unzip", "usage: unzip <archive.zip> [-d dir]"); return; }

            string archive = PathUtil.Resolve(operands[0]);
            if (!File.Exists(archive)) { Io.Error("unzip", $"cannot find or open {operands[0]}"); return; }

            string target = Kernel.CurrentDirectory;
            int dIndex = args.IndexOf("-d");
            if (dIndex >= 0 && dIndex + 1 < args.Count) target = PathUtil.Resolve(args[dIndex + 1]);

            try
            {
                Console.WriteLine($"Archive:  {operands[0]}");
                using var zip = ZipFile.OpenRead(archive);
                foreach (var entry in zip.Entries)
                {
                    string destination = Path.Combine(target, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));

                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    entry.ExtractToFile(destination, true);
                    Console.WriteLine($"  inflating: {entry.FullName}");
                }
            }
            catch (Exception ex) { Io.Error("unzip", ex.Message); }
        }

        /// <summary>tar mapped onto zip, so -czf / -xzf behave the way muscle memory expects.</summary>
        public static void Tar(List<string> args)
        {
            bool create = args.Any(a => a.StartsWith("-") && a.Contains('c'));
            bool extract = args.Any(a => a.StartsWith("-") && a.Contains('x'));
            bool list = args.Any(a => a.StartsWith("-") && a.Contains('t'));
            var operands = Io.Operands(args);

            if (operands.Count == 0) { Io.Error("tar", "usage: tar -czf out.tar.gz <files> | tar -xzf archive"); return; }

            if (create) { Zip(operands); return; }

            if (extract) { Unzip(operands); return; }

            if (list)
            {
                string archive = PathUtil.Resolve(operands[0]);
                if (!File.Exists(archive)) { Io.Error("tar", $"{operands[0]}: Cannot open"); return; }
                try
                {
                    using var zip = ZipFile.OpenRead(archive);
                    foreach (var entry in zip.Entries) Console.WriteLine(entry.FullName);
                }
                catch (Exception ex) { Io.Error("tar", ex.Message); }
                return;
            }

            Io.Error("tar", "you must specify one of -c, -x or -t");
        }

        public static void Gzip(List<string> args, bool decompress)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error(decompress ? "gunzip" : "gzip", "missing operand"); return; }

            string path = PathUtil.Resolve(operands[0]);
            if (!File.Exists(path)) { Io.Error(decompress ? "gunzip" : "gzip", $"{operands[0]}: No such file"); return; }

            try
            {
                if (decompress)
                {
                    string target = path.EndsWith(".gz") ? path.Substring(0, path.Length - 3) : path + ".out";
                    using var input = File.OpenRead(path);
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    using var output = File.Create(target);
                    gzip.CopyTo(output);
                }
                else
                {
                    using (var input = File.OpenRead(path))
                    using (var output = File.Create(path + ".gz"))
                    using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                        input.CopyTo(gzip);

                    File.Delete(path);
                }
            }
            catch (Exception ex) { Io.Error("gzip", ex.Message); }
        }
    }

    public static class HashCommands
    {
        public static void Hash(string algorithm, List<string> args)
        {
            var operands = Io.Operands(args);

            using HashAlgorithm hasher = algorithm switch
            {
                "md5" => MD5.Create(),
                "sha1" => SHA1.Create(),
                "sha512" => SHA512.Create(),
                _ => SHA256.Create()
            };

            if (operands.Count == 0)
            {
                if (ShellEnv.StdIn == null) { Io.Error(algorithm + "sum", "missing operand"); return; }
                Console.WriteLine($"{ToHex(hasher.ComputeHash(Encoding.UTF8.GetBytes(ShellEnv.StdIn)))}  -");
                return;
            }

            foreach (var operand in operands)
            {
                string path = PathUtil.Resolve(operand);
                if (!File.Exists(path)) { Io.Error(algorithm + "sum", $"{operand}: No such file or directory"); continue; }
                try
                {
                    using var stream = File.OpenRead(path);
                    Console.WriteLine($"{ToHex(hasher.ComputeHash(stream))}  {operand}");
                }
                catch (Exception ex) { Io.Error(algorithm + "sum", ex.Message); }
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static void Base64(List<string> args)
        {
            bool decode = Io.HasFlag(args, "-d", "--decode");
            var operands = Io.Operands(args);

            string input;
            if (operands.Count > 0)
            {
                string path = PathUtil.Resolve(operands[0]);
                if (!File.Exists(path)) { Io.Error("base64", $"{operands[0]}: No such file"); return; }
                input = File.ReadAllText(path);
            }
            else if (ShellEnv.StdIn != null) input = ShellEnv.StdIn;
            else { Io.Error("base64", "missing operand"); return; }

            try
            {
                Console.WriteLine(decode
                    ? Encoding.UTF8.GetString(Convert.FromBase64String(input.Trim()))
                    : Convert.ToBase64String(Encoding.UTF8.GetBytes(input)));
            }
            catch (Exception ex) { Io.Error("base64", "invalid input - " + ex.Message); }
        }
    }

    public static class MiscCommands
    {
        public static void Echo(List<string> args)
        {
            bool noNewline = args.Count > 0 && args[0] == "-n";
            bool escapes = args.Count > 0 && args[0] == "-e";
            var words = args.Skip(noNewline || escapes ? 1 : 0);

            string text = string.Join(" ", words);
            if (escapes) text = text.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");

            if (noNewline) Console.Write(text);
            else Console.WriteLine(text);
        }

        public static void Printf(List<string> args)
        {
            if (args.Count == 0) { Io.Error("printf", "usage: printf FORMAT [ARGS]"); return; }

            string format = args[0].Replace("\\n", "\n").Replace("\\t", "\t");
            var values = args.Skip(1).Cast<object>().ToArray();

            int index = 0;
            var sb = new StringBuilder();
            for (int i = 0; i < format.Length; i++)
            {
                if (format[i] == '%' && i + 1 < format.Length)
                {
                    char spec = format[i + 1];
                    if (spec == '%') { sb.Append('%'); i++; continue; }
                    if (spec == 's' || spec == 'd' || spec == 'f')
                    {
                        sb.Append(index < values.Length ? values[index++] : "");
                        i++;
                        continue;
                    }
                }
                sb.Append(format[i]);
            }
            Console.Write(sb.ToString());
        }

        public static void Seq(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("seq", "missing operand"); return; }

            double start = 1, step = 1, end;
            if (operands.Count == 1) double.TryParse(operands[0], out end);
            else if (operands.Count == 2) { double.TryParse(operands[0], out start); double.TryParse(operands[1], out end); }
            else { double.TryParse(operands[0], out start); double.TryParse(operands[1], out step); double.TryParse(operands[2], out end); }

            if (step == 0) { Io.Error("seq", "step cannot be zero"); return; }

            int guard = 0;
            for (double v = start; step > 0 ? v <= end : v >= end; v += step)
            {
                Console.WriteLine(v % 1 == 0 ? ((long)v).ToString() : v.ToString());
                if (++guard > 100000) break;
            }
        }

        public static void Yes(List<string> args)
        {
            string text = Io.Operands(args).Count > 0 ? string.Join(" ", Io.Operands(args)) : "y";
            Console.WriteLine($"yes: would print '{text}' forever - press Ctrl+C in a real terminal.");
            for (int i = 0; i < 10; i++) Console.WriteLine(text);
        }

        public static void Calc(List<string> args)
        {
            var operands = Io.Operands(args);
            string expression = operands.Count > 0 ? string.Join("", operands) : ShellEnv.StdIn?.Trim();

            if (string.IsNullOrWhiteSpace(expression)) { Io.Error("calc", "usage: calc <expression>"); return; }

            try
            {
                var result = new DataTable().Compute(expression, "");
                Console.WriteLine(result);
            }
            catch { Io.Error("calc", $"invalid expression: {expression}"); }
        }

        public static void Expr(List<string> args) => Calc(args);

        public static void Theme(List<string> args)
        {
            var operands = Io.Operands(args);

            if (operands.Count == 0)
            {
                Console.WriteLine("Usage: theme <prompt|dir|text|bg|reset> <color>");
                Console.WriteLine("Colors: " + string.Join(", ", Enum.GetNames(typeof(ConsoleColor))));
                Console.WriteLine($"\nCurrent: prompt={Kernel.Config.PromptUserColor}, dir={Kernel.Config.PromptDirColor}, " +
                                  $"text={Kernel.Config.DefaultTextColor}, bg={Kernel.Config.DefaultBgColor}");
                return;
            }

            if (operands[0].ToLower() == "reset")
            {
                Kernel.Config.PromptUserColor = ConsoleColor.Green;
                Kernel.Config.PromptDirColor = ConsoleColor.Blue;
                Kernel.Config.DefaultTextColor = ConsoleColor.White;
                Kernel.Config.DefaultBgColor = ConsoleColor.Black;
                ConfigManager.SaveConfig();
                ConfigManager.ApplyTheme();
                Console.WriteLine("Theme reset.");
                return;
            }

            if (operands.Count < 2) { Io.Error("theme", "missing color"); return; }

            if (!Enum.TryParse(operands[1], true, out ConsoleColor color))
            {
                Io.Error("theme", $"invalid color '{operands[1]}'");
                return;
            }

            switch (operands[0].ToLower())
            {
                case "prompt": case "user": Kernel.Config.PromptUserColor = color; break;
                case "dir": case "path": Kernel.Config.PromptDirColor = color; break;
                case "text": case "fg": Kernel.Config.DefaultTextColor = color; break;
                case "bg": case "background": Kernel.Config.DefaultBgColor = color; break;
                default: Io.Error("theme", $"unknown target '{operands[0]}'"); return;
            }

            ConfigManager.SaveConfig();
            ConfigManager.ApplyTheme();
            Console.WriteLine("Theme updated.");
        }

        public static void History(List<string> args)
        {
            if (Io.HasFlag(args, "-c"))
            {
                LineEditor.ClearHistory();
                Console.WriteLine("History cleared.");
                return;
            }

            var history = LineEditor.History;
            int limit = history.Count;
            var operands = Io.Operands(args);
            if (operands.Count > 0 && int.TryParse(operands[0], out int n)) limit = Math.Min(n, history.Count);

            for (int i = history.Count - limit; i < history.Count; i++)
                Console.WriteLine($"{i + 1,5}  {history[i]}");
        }

        public static void Alias(List<string> args)
        {
            var operands = Io.Operands(args);

            if (operands.Count == 0)
            {
                foreach (var alias in ShellEnv.Aliases.OrderBy(a => a.Key))
                    Console.WriteLine($"alias {alias.Key}='{alias.Value}'");
                return;
            }

            string joined = string.Join(" ", operands);
            int equals = joined.IndexOf('=');
            if (equals < 0)
            {
                if (ShellEnv.Aliases.TryGetValue(joined, out string value)) Console.WriteLine($"alias {joined}='{value}'");
                else Io.Error("alias", $"{joined}: not found");
                return;
            }

            string name = joined.Substring(0, equals).Trim();
            string body = joined.Substring(equals + 1).Trim().Trim('\'', '"');
            ShellEnv.Aliases[name] = body;
        }

        public static void Unalias(List<string> args)
        {
            foreach (var name in Io.Operands(args))
                if (!ShellEnv.Aliases.Remove(name)) Io.Error("unalias", $"{name}: not found");
        }

        public static void Export(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { SysInfoCommands.Env(); return; }

            foreach (var operand in operands)
            {
                int equals = operand.IndexOf('=');
                if (equals < 0) continue;
                ShellEnv.Set(operand.Substring(0, equals), operand.Substring(equals + 1));
            }
        }

        public static void Which(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("which", "missing argument"); return; }

            foreach (var name in operands)
            {
                if (ShellEnv.Aliases.ContainsKey(name)) { Console.WriteLine($"{name}: aliased to {ShellEnv.Aliases[name]}"); continue; }
                if (CommandRouter.CommandNames.Contains(name)) Console.WriteLine($"/usr/bin/{name}");
                else { Io.Error("which", $"no {name} in ({ShellEnv.Get("PATH")})"); }
            }
        }

        public static void Cowsay(List<string> args)
        {
            string text = Io.Operands(args).Count > 0 ? string.Join(" ", Io.Operands(args)) : (ShellEnv.StdIn?.Trim() ?? "Moo!");
            if (text.Length > 40) text = text.Substring(0, 40);

            string border = new string('_', text.Length + 2);
            Console.WriteLine($" {border}");
            Console.WriteLine($"< {text} >");
            Console.WriteLine($" {new string('-', text.Length + 2)}");
            Console.WriteLine(@"        \   ^__^");
            Console.WriteLine(@"         \  (oo)\_______");
            Console.WriteLine(@"            (__)\       )\/\");
            Console.WriteLine(@"                ||----w |");
            Console.WriteLine(@"                ||     ||");
        }

        private static readonly string[] Fortunes =
        {
            "Любой код, который ты не понимаешь - легаси.",
            "Единственный баг, который нельзя исправить, - тот, который нельзя воспроизвести.",
            "Сначала заставь работать, потом заставь красиво.",
            "Кто не логирует, тот отлаживает вслепую.",
            "StyleOS: потому что своя ОС лучше чужой."
        };

        public static void Fortune() => Console.WriteLine(Fortunes[new Random().Next(Fortunes.Length)]);

        public static void Banner(List<string> args)
        {
            string text = Io.Operands(args).Count > 0 ? string.Join(" ", Io.Operands(args)).ToUpper() : "STYLEOS";
            Console.ForegroundColor = ConsoleColor.Cyan;
            foreach (char c in text) Console.Write($"{c} ");
            Console.WriteLine();
            Console.WriteLine(new string('=', text.Length * 2));
            Console.ResetColor();
        }

        public static void Open(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("open", "missing operand"); return; }

            string target = operands[0];
            string path = PathUtil.Resolve(target);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = PathUtil.Exists(path) ? path : target,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Io.Error("open", ex.Message); }
        }

        public static void Authors()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"{Kernel.DistroName} {Kernel.Version}");
            Console.ResetColor();
            Console.WriteLine("  Tim (timdem228)  -  https://timd.site");
            Console.WriteLine("  admin@timd.site");
        }
    }
}
