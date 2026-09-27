using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StyleOS
{
    public static class FileCommands
    {
        public static void Ls(List<string> args)
        {
            bool longFormat = Io.HasFlag(args, "-l");
            bool all = Io.HasFlag(args, "-a");
            bool human = Io.HasFlag(args, "-h");
            bool recursive = Io.HasFlag(args, "-R");
            bool onePerLine = Io.HasFlag(args, "-1");
            bool byTime = Io.HasFlag(args, "-t");
            bool reverse = Io.HasFlag(args, "-r");

            var targets = Io.Operands(args);
            if (targets.Count == 0) targets.Add(".");

            foreach (var target in targets)
            {
                string path = PathUtil.Resolve(target);

                if (File.Exists(path)) { PrintFile(new FileInfo(path), longFormat, human); continue; }
                if (!Directory.Exists(path)) { Io.Error("ls", $"cannot access '{target}': No such file or directory"); continue; }

                if (targets.Count > 1 || recursive) Console.WriteLine($"\n{PathUtil.Display(path)}:");
                ListDirectory(path, longFormat, all, human, onePerLine, byTime, reverse);

                if (recursive)
                {
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(path))
                            ListRecursive(sub, longFormat, all, human, onePerLine, byTime, reverse);
                    }
                    catch (Exception ex) { Io.Error("ls", ex.Message); }
                }
            }
        }

        private static void ListRecursive(string path, bool l, bool a, bool h, bool one, bool t, bool r)
        {
            Console.WriteLine($"\n{PathUtil.Display(path)}:");
            ListDirectory(path, l, a, h, one, t, r);
            try
            {
                foreach (var sub in Directory.GetDirectories(path)) ListRecursive(sub, l, a, h, one, t, r);
            }
            catch { }
        }

        private static void ListDirectory(string path, bool longFormat, bool all, bool human, bool onePerLine, bool byTime, bool reverse)
        {
            try
            {
                var di = new DirectoryInfo(path);
                IEnumerable<FileSystemInfo> entries = di.GetFileSystemInfos();

                if (!all) entries = entries.Where(e => !e.Name.StartsWith(".") && !e.Attributes.HasFlag(FileAttributes.Hidden));

                entries = byTime
                    ? entries.OrderByDescending(e => e.LastWriteTime)
                    : entries.OrderBy(e => e is DirectoryInfo ? 0 : 1).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

                if (reverse) entries = entries.Reverse();

                var list = entries.ToList();

                if (!longFormat && !onePerLine)
                {
                    PrintColumns(list);
                    return;
                }

                foreach (var entry in list)
                {
                    if (entry is DirectoryInfo d) PrintDir(d, longFormat);
                    else PrintFile((FileInfo)entry, longFormat, human);
                }
            }
            catch (UnauthorizedAccessException) { Io.Error("ls", $"cannot open directory '{PathUtil.Display(path)}': Permission denied"); }
            catch (Exception ex) { Io.Error("ls", ex.Message); }
        }

        private static void PrintColumns(List<FileSystemInfo> entries)
        {
            if (entries.Count == 0) return;
            int width = ConsoleHost.Width - 1;
            int colWidth = Math.Min(40, entries.Max(e => e.Name.Length) + 2);
            int perRow = Math.Max(1, width / colWidth);

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                Console.ForegroundColor = ColorFor(e);
                Console.Write(e.Name.PadRight(colWidth));
                Console.ResetColor();
                if ((i + 1) % perRow == 0) Console.WriteLine();
            }
            if (entries.Count % perRow != 0) Console.WriteLine();
        }

        private static ConsoleColor ColorFor(FileSystemInfo e)
        {
            if (e.LinkTarget != null) return ConsoleColor.Cyan;
            if (e is DirectoryInfo) return ConsoleColor.Blue;
            string ext = e.Extension.ToLower();
            if (ext == ".exe" || ext == ".bat" || ext == ".sh" || ext == ".cmd" || ext == ".ps1") return ConsoleColor.Green;
            if (ext == ".zip" || ext == ".rar" || ext == ".7z" || ext == ".tar" || ext == ".gz") return ConsoleColor.Red;
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp") return ConsoleColor.Magenta;
            return Kernel.Config.DefaultTextColor;
        }

        private static void PrintDir(DirectoryInfo d, bool longFormat)
        {
            // A symlinked directory reports its OWN size as the length of the target path
            // string, not the actual size 0 a directory has, and isn't really "drwxr-xr-x"
            // either - show it as a link instead of pretending it's a plain directory.
            if (d.LinkTarget != null) { PrintLink(d, longFormat); return; }

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine(longFormat
                ? $"drwxr-xr-x  {"-",10}  {d.LastWriteTime:MMM dd HH:mm}  {d.Name}/"
                : d.Name + "/");
            Console.ResetColor();
        }

        private static void PrintFile(FileInfo f, bool longFormat, bool human)
        {
            if (f.LinkTarget != null) { PrintLink(f, longFormat); return; }

            Console.ForegroundColor = ColorFor(f);
            string size = human ? PathUtil.HumanSize(f.Length) : f.Length.ToString();
            Console.WriteLine(longFormat
                ? $"-rw-r--r--  {size,10}  {f.LastWriteTime:MMM dd HH:mm}  {f.Name}"
                : f.Name);
            Console.ResetColor();
        }

        /// <summary>Prints a symlink as "name -> target", the way a real ls -l does,
        /// instead of following it and reporting the target's own type/size under the
        /// link's name.</summary>
        private static void PrintLink(FileSystemInfo entry, bool longFormat)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(longFormat
                ? $"lrwxrwxrwx  {"-",10}  {entry.LastWriteTime:MMM dd HH:mm}  {entry.Name} -> {entry.LinkTarget}"
                : $"{entry.Name} -> {entry.LinkTarget}");
            Console.ResetColor();
        }

        public static void Cd(List<string> args)
        {
            var operands = Io.Operands(args);
            string target = operands.Count == 0 ? Kernel.Home : operands[0];

            if (target == "-")
            {
                if (string.IsNullOrEmpty(ShellEnv.PreviousDirectory)) { Io.Error("cd", "OLDPWD not set"); return; }
                target = ShellEnv.PreviousDirectory;
                Console.WriteLine(PathUtil.Display(target));
            }

            string path = PathUtil.Resolve(target);
            if (!Directory.Exists(path)) { Io.Error("cd", $"{target}: No such file or directory"); return; }

            try
            {
                // Fails early with a clear message instead of breaking every later command.
                Directory.GetFileSystemEntries(path);
            }
            catch (UnauthorizedAccessException) { Io.Error("cd", $"{target}: Permission denied"); return; }
            catch { }

            ShellEnv.PreviousDirectory = Kernel.CurrentDirectory;
            Kernel.CurrentDirectory = path;
            ShellEnv.Set("PWD", path);
        }

        public static void Pwd() => Console.WriteLine(PathUtil.Display(Kernel.CurrentDirectory));

        public static void Mkdir(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("mkdir", "missing operand"); return; }

            foreach (var op in operands)
            {
                string path = PathUtil.Resolve(op);
                try
                {
                    if (Directory.Exists(path)) { Io.Error("mkdir", $"cannot create directory '{op}': File exists"); continue; }
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex) { Io.Error("mkdir", ex.Message); }
            }
        }

        public static void Rmdir(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("rmdir", "missing operand"); return; }

            foreach (var op in operands)
            {
                string path = PathUtil.Resolve(op);
                try
                {
                    if (!Directory.Exists(path)) { Io.Error("rmdir", $"failed to remove '{op}': No such file or directory"); continue; }
                    if (Directory.GetFileSystemEntries(path).Length > 0)
                    {
                        Io.Error("rmdir", $"failed to remove '{op}': Directory not empty");
                        continue;
                    }
                    Directory.Delete(path);
                }
                catch (Exception ex) { Io.Error("rmdir", ex.Message); }
            }
        }

        public static void Touch(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("touch", "missing file operand"); return; }

            foreach (var op in operands)
            {
                string path = PathUtil.Resolve(op);
                try
                {
                    // The old version used File.Create, which wiped an existing file.
                    if (File.Exists(path)) File.SetLastWriteTime(path, DateTime.Now);
                    else if (Directory.Exists(path)) Directory.SetLastWriteTime(path, DateTime.Now);
                    else using (File.Create(path)) { }
                }
                catch (Exception ex) { Io.Error("touch", ex.Message); }
            }
        }

        public static void Rm(List<string> args)
        {
            bool recursive = Io.HasFlag(args, "-r", "-R", "--recursive");
            bool force = Io.HasFlag(args, "-f", "--force");
            var operands = Io.Operands(args);

            if (operands.Count == 0)
            {
                if (!force) Io.Error("rm", "missing operand");
                return;
            }

            foreach (var op in operands)
            {
                foreach (var candidate in PathUtil.Glob(op))
                {
                    string path = PathUtil.Resolve(candidate);

                    // Protect the system directory from "rm -rf *" inside the install folder.
                    if (string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), Kernel.SysDir.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        Io.Error("rm", "refusing to remove the StyleOS system directory");
                        continue;
                    }

                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                        else if (Directory.Exists(path))
                        {
                            if (!recursive) { Io.Error("rm", $"cannot remove '{candidate}': Is a directory"); continue; }
                            Directory.Delete(path, true);
                        }
                        else if (!force) Io.Error("rm", $"cannot remove '{candidate}': No such file or directory");
                    }
                    catch (Exception ex) { if (!force) Io.Error("rm", ex.Message); }
                }
            }
        }

        public static void Cp(List<string> args)
        {
            bool recursive = Io.HasFlag(args, "-r", "-R", "--recursive");
            var operands = Io.Operands(args);
            if (operands.Count < 2) { Io.Error("cp", "missing destination file operand"); return; }

            string destArg = operands[operands.Count - 1];
            string dest = PathUtil.Resolve(destArg);
            bool destIsDir = Directory.Exists(dest);

            for (int i = 0; i < operands.Count - 1; i++)
            {
                string src = PathUtil.Resolve(operands[i]);
                try
                {
                    if (Directory.Exists(src))
                    {
                        if (!recursive) { Io.Error("cp", $"-r not specified; omitting directory '{operands[i]}'"); continue; }
                        string targetDir = destIsDir ? Path.Combine(dest, new DirectoryInfo(src).Name) : dest;
                        CopyDirectory(src, targetDir);
                    }
                    else if (File.Exists(src))
                    {
                        // "cp file dir/" used to fail: the destination folder was ignored.
                        string targetFile = destIsDir ? Path.Combine(dest, Path.GetFileName(src)) : dest;
                        File.Copy(src, targetFile, true);
                    }
                    else Io.Error("cp", $"cannot stat '{operands[i]}': No such file or directory");
                }
                catch (Exception ex) { Io.Error("cp", ex.Message); }
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        public static void Mv(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count < 2) { Io.Error("mv", "missing destination file operand"); return; }

            string dest = PathUtil.Resolve(operands[operands.Count - 1]);
            bool destIsDir = Directory.Exists(dest);

            for (int i = 0; i < operands.Count - 1; i++)
            {
                string src = PathUtil.Resolve(operands[i]);
                try
                {
                    string target = destIsDir ? Path.Combine(dest, Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar))) : dest;

                    if (Directory.Exists(src)) Directory.Move(src, target);
                    else if (File.Exists(src))
                    {
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(src, target);
                    }
                    else Io.Error("mv", $"cannot stat '{operands[i]}': No such file or directory");
                }
                catch (Exception ex) { Io.Error("mv", ex.Message); }
            }
        }

        public static void Ln(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count < 2) { Io.Error("ln", "missing file operand"); return; }

            string src = PathUtil.Resolve(operands[0]);
            string dest = PathUtil.Resolve(operands[1]);
            try
            {
                if (Directory.Exists(src)) Directory.CreateSymbolicLink(dest, src);
                else File.CreateSymbolicLink(dest, src);
            }
            catch (Exception ex) { Io.Error("ln", ex.Message); }
        }

        public static void Tree(List<string> args)
        {
            string root = Io.Operands(args).FirstOrDefault() ?? ".";
            string path = PathUtil.Resolve(root);
            if (!Directory.Exists(path)) { Io.Error("tree", $"{root}: No such directory"); return; }

            Console.WriteLine(PathUtil.Display(path));
            int dirs = 0, files = 0;
            PrintTree(new DirectoryInfo(path), "", ref dirs, ref files, 0);
            Console.WriteLine($"\n{dirs} directories, {files} files");
        }

        private static void PrintTree(DirectoryInfo dir, string indent, ref int dirs, ref int files, int depth)
        {
            if (depth > 12) return;
            FileSystemInfo[] entries;
            try { entries = dir.GetFileSystemInfos().OrderBy(e => e is FileInfo).ThenBy(e => e.Name).ToArray(); }
            catch (UnauthorizedAccessException) { Console.WriteLine(indent + "└── [Access Denied]"); return; }

            for (int i = 0; i < entries.Length; i++)
            {
                bool last = i == entries.Length - 1;
                var e = entries[i];

                Console.ForegroundColor = ColorFor(e);
                Console.WriteLine(indent + (last ? "└── " : "├── ") + e.Name);
                Console.ResetColor();

                if (e is DirectoryInfo sub)
                {
                    dirs++;
                    PrintTree(sub, indent + (last ? "    " : "│   "), ref dirs, ref files, depth + 1);
                }
                else files++;
            }
        }

        public static void Find(List<string> args)
        {
            var operands = Io.Operands(Io.StripOptionValues(args, "-name", "-type"));
            string root = operands.Count > 0 ? operands[0] : ".";
            string pattern = "*";

            int nameIndex = args.IndexOf("-name");
            if (nameIndex >= 0 && nameIndex + 1 < args.Count) pattern = args[nameIndex + 1];
            else if (operands.Count > 1) pattern = operands[1];

            bool typeFile = args.Contains("-type") && args.IndexOf("-type") + 1 < args.Count && args[args.IndexOf("-type") + 1] == "f";
            bool typeDir = args.Contains("-type") && args.IndexOf("-type") + 1 < args.Count && args[args.IndexOf("-type") + 1] == "d";

            string path = PathUtil.Resolve(root);
            if (!Directory.Exists(path)) { Io.Error("find", $"'{root}': No such file or directory"); return; }

            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                if (!typeFile)
                    foreach (var d in Directory.EnumerateDirectories(path, pattern, options)) Console.WriteLine(PathUtil.Display(d));
                if (!typeDir)
                    foreach (var f in Directory.EnumerateFiles(path, pattern, options)) Console.WriteLine(PathUtil.Display(f));
            }
            catch (Exception ex) { Io.Error("find", ex.Message); }
        }

        public static void Du(List<string> args)
        {
            bool human = Io.HasFlag(args, "-h");
            string target = Io.Operands(args).FirstOrDefault() ?? ".";
            string path = PathUtil.Resolve(target);

            try
            {
                long total = DirSize(new DirectoryInfo(path));
                Console.WriteLine($"{(human ? PathUtil.HumanSize(total) : (total / 1024).ToString()),-10}{PathUtil.Display(path)}");
            }
            catch (Exception ex) { Io.Error("du", ex.Message); }
        }

        private static long DirSize(DirectoryInfo dir)
        {
            long size = 0;
            try
            {
                foreach (var f in dir.GetFiles()) size += f.Length;
                foreach (var d in dir.GetDirectories()) size += DirSize(d);
            }
            catch { }
            return size;
        }

        public static void Stat(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("stat", "missing operand"); return; }

            foreach (var op in operands)
            {
                string path = PathUtil.Resolve(op);
                try
                {
                    if (File.Exists(path))
                    {
                        var f = new FileInfo(path);
                        Console.WriteLine($"  File: {f.Name}");
                        Console.WriteLine($"  Size: {f.Length,-12} Blocks: {(f.Length + 511) / 512,-8} regular file");
                        Console.WriteLine($"Access: {f.LastAccessTime:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine($"Modify: {f.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine($"Create: {f.CreationTime:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine($"Attrib: {f.Attributes}");
                    }
                    else if (Directory.Exists(path))
                    {
                        var d = new DirectoryInfo(path);
                        Console.WriteLine($"  File: {d.Name}");
                        Console.WriteLine($"  Size: 4096         directory");
                        Console.WriteLine($"Modify: {d.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                        Console.WriteLine($"Create: {d.CreationTime:yyyy-MM-dd HH:mm:ss}");
                    }
                    else Io.Error("stat", $"cannot stat '{op}': No such file or directory");
                }
                catch (Exception ex) { Io.Error("stat", ex.Message); }
            }
        }

        public static void FileType(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("file", "missing operand"); return; }

            foreach (var op in operands)
            {
                string path = PathUtil.Resolve(op);
                if (Directory.Exists(path)) { Console.WriteLine($"{op}: directory"); continue; }
                if (!File.Exists(path)) { Io.Error("file", $"cannot open '{op}': No such file or directory"); continue; }

                try
                {
                    byte[] head = new byte[16];
                    int read;
                    using (var fs = File.OpenRead(path)) read = fs.Read(head, 0, head.Length);

                    string type = "data";
                    if (read >= 2 && head[0] == 'M' && head[1] == 'Z') type = "PE32+ executable (Windows)";
                    else if (read >= 4 && head[0] == 0x7F && head[1] == 'E' && head[2] == 'L' && head[3] == 'F') type = "ELF executable";
                    else if (read >= 4 && head[0] == 0x50 && head[1] == 0x4B) type = "Zip archive data";
                    else if (read >= 8 && head[0] == 0x89 && head[1] == 'P') type = "PNG image data";
                    else if (read >= 3 && head[0] == 0xFF && head[1] == 0xD8) type = "JPEG image data";
                    else if (read >= 2 && head[0] == 0x1F && head[1] == 0x8B) type = "gzip compressed data";
                    else if (IsText(head, read)) type = "ASCII text";

                    Console.WriteLine($"{op}: {type}");
                }
                catch (Exception ex) { Io.Error("file", ex.Message); }
            }
        }

        private static bool IsText(byte[] data, int length)
        {
            for (int i = 0; i < length; i++)
                if (data[i] == 0) return false;
            return true;
        }

        public static void Basename(List<string> args)
        {
            var o = Io.Operands(args);
            if (o.Count == 0) { Io.Error("basename", "missing operand"); return; }
            Console.WriteLine(Path.GetFileName(o[0].TrimEnd('/', '\\')));
        }

        public static void Dirname(List<string> args)
        {
            var o = Io.Operands(args);
            if (o.Count == 0) { Io.Error("dirname", "missing operand"); return; }
            string dir = Path.GetDirectoryName(o[0].TrimEnd('/', '\\'));
            Console.WriteLine(string.IsNullOrEmpty(dir) ? "." : dir.Replace('\\', '/'));
        }

        public static void Realpath(List<string> args)
        {
            var o = Io.Operands(args);
            if (o.Count == 0) { Io.Error("realpath", "missing operand"); return; }
            Console.WriteLine(PathUtil.Resolve(o[0]));
        }

        /// <summary>Windows has no POSIX mode bits, so these report instead of pretending.</summary>
        public static void Chmod(List<string> args)
        {
            // The mode itself ("-w", "+x", "444"...) commonly starts with '-' or '+', so this
            // reads positional args directly instead of Io.Operands(), which would otherwise
            // mistake the mode for a flag and filter it out.
            if (args.Count < 2) { Io.Error("chmod", "missing operand"); return; }

            string mode = args[0];
            string target = args[1];
            string path = PathUtil.Resolve(target);
            if (!PathUtil.Exists(path)) { Io.Error("chmod", $"cannot access '{target}': No such file or directory"); return; }

            try
            {
                bool readOnly = mode.Contains("-w") || mode == "444" || mode == "555";
                var attrs = File.GetAttributes(path);
                File.SetAttributes(path, readOnly ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly);
                Console.WriteLine($"mode of '{target}' changed to {mode}");
            }
            catch (Exception ex) { Io.Error("chmod", ex.Message); }
        }

        public static void Chown(List<string> args)
        {
            if (args.Count < 2) { Io.Error("chown", "missing operand"); return; }
            Console.WriteLine($"chown: ownership of '{args[1]}' retained as {args[0]} (StyleOS maps every file to the current user)");
        }
    }
}
