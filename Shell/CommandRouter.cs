using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StyleOS
{
    public class CommandInfo
    {
        public string Name;
        public string Category;
        public string Usage;
        public string Description;
        public Func<List<string>, Task> Run;
        public bool Interactive;   // must not be captured by a pipe
    }

    /// <summary>
    /// The single place that knows which command does what. Everything else (help, man,
    /// which, tab completion) reads this table, so nothing can drift out of sync.
    /// </summary>
    public static class CommandRouter
    {
        private static readonly Dictionary<string, CommandInfo> Commands =
            new Dictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<string> CommandNames => Commands.Keys;

        static CommandRouter() { Register(); }

        private static void Add(string names, string category, string usage, string description,
                                Action<List<string>> run, bool interactive = false)
            => AddAsync(names, category, usage, description, args => { run(args); return Task.CompletedTask; }, interactive);

        private static void AddAsync(string names, string category, string usage, string description,
                                Func<List<string>, Task> run, bool interactive = false)
        {
            var parts = names.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
            var info = new CommandInfo
            {
                Name = parts[0],
                Category = category,
                Usage = usage,
                Description = description,
                Run = run,
                Interactive = interactive
            };
            foreach (var name in parts) Commands[name] = info;
        }

        private static void Register()
        {
            const string files = "Files";
            const string text = "Text";
            const string system = "System";
            const string proc = "Processes";
            const string net = "Network";
            const string disk = "Disks";
            const string users = "Users";
            const string pkg = "Packages";
            const string shell = "Shell";
            const string apps = "Apps";

            // ---- files -------------------------------------------------------
            Add("ls,dir", files, "ls [-l -a -h -R -t -r -1] [path]", "List directory contents", FileCommands.Ls);
            Add("cd", files, "cd [dir|-]", "Change the working directory", FileCommands.Cd);
            Add("pwd", files, "pwd", "Print the working directory", a => FileCommands.Pwd());
            Add("mkdir", files, "mkdir <dir...>", "Create directories", FileCommands.Mkdir);
            Add("rmdir", files, "rmdir <dir...>", "Remove empty directories", FileCommands.Rmdir);
            Add("touch", files, "touch <file...>", "Create a file or update its timestamp", FileCommands.Touch);
            Add("rm,del", files, "rm [-r -f] <path...>", "Remove files and directories", FileCommands.Rm);
            Add("cp,copy", files, "cp [-r] <src...> <dest>", "Copy files and directories", FileCommands.Cp);
            Add("mv,move", files, "mv <src...> <dest>", "Move or rename files", FileCommands.Mv);
            Add("ln", files, "ln [-s] <target> <link>", "Create a symbolic link", FileCommands.Ln);
            Add("tree", files, "tree [path]", "Show the directory tree", FileCommands.Tree);
            Add("find", files, "find <path> [-name pattern] [-type f|d]", "Search for files", FileCommands.Find);
            Add("du", files, "du [-h] [path]", "Disk usage of a directory", FileCommands.Du);
            Add("stat", files, "stat <path...>", "Show file metadata", FileCommands.Stat);
            Add("file", files, "file <path...>", "Guess the type of a file", FileCommands.FileType);
            Add("basename", files, "basename <path>", "Strip the directory from a path", FileCommands.Basename);
            Add("dirname", files, "dirname <path>", "Strip the file name from a path", FileCommands.Dirname);
            Add("realpath", files, "realpath <path>", "Print the absolute path", FileCommands.Realpath);
            Add("chmod", files, "chmod <mode> <path>", "Change the read-only attribute", FileCommands.Chmod);
            Add("chown", files, "chown <user> <path>", "Change the owner (informational)", FileCommands.Chown);

            // ---- text --------------------------------------------------------
            Add("cat", text, "cat [-n] <file...>", "Print files", TextCommands.Cat);
            Add("head", text, "head [-n N] <file>", "First lines of a file", TextCommands.Head);
            Add("tail", text, "tail [-n N] <file>", "Last lines of a file", TextCommands.Tail);
            Add("wc", text, "wc [-l -w -c] <file>", "Count lines, words and bytes", TextCommands.Wc);
            Add("grep", text, "grep [-i -n -v -c -r] <pattern> [file...]", "Search text with a regex", TextCommands.Grep);
            Add("sort", text, "sort [-n -r -u] <file>", "Sort lines", TextCommands.Sort);
            Add("uniq", text, "uniq [-c -d] <file>", "Collapse repeated lines", TextCommands.Uniq);
            Add("cut", text, "cut -d DELIM -f LIST <file>", "Cut fields out of each line", TextCommands.Cut);
            Add("tr", text, "... | tr <set1> <set2>", "Translate or delete characters", TextCommands.Tr);
            Add("rev", text, "rev <file>", "Reverse each line", TextCommands.Rev);
            Add("nl", text, "nl <file>", "Number the lines of a file", TextCommands.Nl);
            Add("tee", text, "... | tee [-a] <file>", "Write stdin to a file and to the screen", TextCommands.Tee);
            Add("sed", text, "sed 's/old/new/g' <file>", "Stream editor (substitution)", TextCommands.Sed);
            Add("diff", text, "diff <file1> <file2>", "Compare two files line by line", TextCommands.Diff);
            Add("less,more", text, "less <file>", "Page through a file", TextCommands.Pager, true);
            Add("xxd,hexdump", text, "xxd <file>", "Hex dump of a file", TextCommands.Xxd);
            Add("strings", text, "strings <file>", "Printable strings inside a binary", TextCommands.Strings);

            // ---- system ------------------------------------------------------
            Add("fastfetch", system, "fastfetch", "System summary with the StyleOS Core kernel line", a => SysInfoCommands.Fetch("fastfetch"));
            Add("neofetch,sysinfo,screenfetch", system, "neofetch", "System summary (same output as fastfetch)", a => SysInfoCommands.Fetch("neofetch"));
            Add("uname", system, "uname [-a -s -n -r -v -m -o]", "Print kernel and system information", SysInfoCommands.Uname);
            Add("uptime", system, "uptime", "How long the session has been running", a => SysInfoCommands.Uptime());
            Add("free", system, "free [-h -g]", "Memory usage", SysInfoCommands.Free);
            Add("lscpu", system, "lscpu", "CPU information", a => SysInfoCommands.Lscpu());
            Add("dmesg", system, "dmesg", "Kernel ring buffer (the StyleOS log)", a => SysInfoCommands.Dmesg());
            AddAsync("modules,lsmod", system, "modules | modules install modules [version]", "List loaded kernel modules, or bootstrap the Python module bridge", HandleModulesCommand);
            Add("date", system, "date [+FORMAT]", "Show the current date", SysInfoCommands.Date);
            Add("time", system, "time", "Show the current time", a => Console.WriteLine(DateTime.Now.ToString("HH:mm:ss")));
            Add("cal", system, "cal", "Calendar for the current month", a => SysInfoCommands.Cal());
            Add("env,printenv", system, "env", "List environment variables", a => SysInfoCommands.Env());
            Add("systemctl,service", system, "systemctl <status|start|stop|list-units> [unit]", "Control StyleOS services", ServiceCommands.Systemctl);
            Add("journalctl", system, "journalctl [-n N]", "Show the system journal", ServiceCommands.Journalctl);
            Add("authors", system, "authors", "Who made this", a => MiscCommands.Authors());
            Add("version", system, "version", "Print the StyleOS version", a => Console.WriteLine($"{Kernel.DistroName} {Kernel.Version} ({Kernel.KernelString})"));
            AddAsync("whatsnew", system, "whatsnew [version]", "Show the whatsnew.txt from a GitHub release (defaults to the installed version)",
                a => UpdateSystem.WhatsNew(Io.Operands(a).FirstOrDefault()));

            // ---- processes ---------------------------------------------------
            Add("ps", proc, "ps [-e]", "List processes", SysInfoCommands.Ps);
            Add("top,htop", proc, "top", "Live process viewer", a => SysInfoCommands.Top(), true);
            Add("kill", proc, "kill [-9] <pid>", "Terminate a process", ProcessCommands.Kill);
            Add("killall,pkill", proc, "killall <name>", "Terminate processes by name", ProcessCommands.Killall);
            Add("pgrep", proc, "pgrep [-l] <pattern>", "Find process ids by name", ProcessCommands.Pgrep);
            Add("jobs", proc, "jobs", "List shell jobs", a => ProcessCommands.Jobs());
            Add("sleep", proc, "sleep <seconds>", "Wait for a while", ProcessCommands.Sleep);
            Add("watch", proc, "watch <command>", "Re-run a command every two seconds", ProcessCommands.Watch, true);

            // ---- network -----------------------------------------------------
            AddAsync("ping", net, "ping [-c N] <host>", "Send ICMP echo requests", NetworkCommands.Ping);
            AddAsync("curl", net, "curl [-I -i] <url>", "Fetch a URL", NetworkCommands.Curl);
            AddAsync("wget,tc", net, "wget [-O file] <url>", "Download a file", NetworkCommands.Wget);
            Add("ifconfig,ipconfig,ip", net, "ifconfig", "Show network interfaces", a => NetworkCommands.IfConfig());
            Add("netstat,ss", net, "netstat [-l]", "Show network connections", NetworkCommands.Netstat);
            AddAsync("nslookup,dig,host", net, "nslookup <host>", "Resolve a host name", NetworkCommands.Nslookup);
            AddAsync("traceroute,tracert", net, "traceroute <host>", "Trace the route to a host", NetworkCommands.Traceroute);
            Add("hostname", net, "hostname [-I] [name]", "Show or set the host name", NetworkCommands.Hostname);
            Add("ssh", net, "ssh user@host", "Remote shell (not implemented yet)", NetworkCommands.Ssh);

            // ---- disks -------------------------------------------------------
            Add("df", disk, "df [-h]", "Free space per filesystem", DiskCommands.Df);
            Add("mount", disk, "mount", "Show mounted filesystems", a => DiskCommands.Mount());
            Add("lsblk,drives", disk, "lsblk", "List block devices", a => DiskCommands.Lsblk());

            // ---- users -------------------------------------------------------
            Add("whoami", users, "whoami", "Print the current user name", a => Console.WriteLine(Kernel.CurrentUser?.Username ?? "root"));
            Add("id", users, "id", "Print user and group ids", a => SysInfoCommands.Id());
            Add("groups", users, "groups", "Print the groups of the current user", a => UserCommands.Groups());
            Add("users", users, "users", "List every account on the system", a => UserCommands.Users());
            Add("passwd", users, "passwd [user]", "Change a password", UserCommands.Passwd);
            Add("useradd,adduser", users, "useradd <name>", "Create a user", UserCommands.UserAdd);
            Add("userdel,deluser", users, "userdel <name>", "Delete a user", UserCommands.UserDel);
            Add("su", users, "su [user]", "Switch user", UserCommands.Su);
            AddAsync("sudo", users, "sudo <command>", "Run a command as root", UserCommands.Sudo);

            // ---- packages and updates ---------------------------------------
            AddAsync("pacman", pkg, "pacman update [beta|stable] | -S | -R | -Q | -Ss | channel", "StyleOS package manager and updater", PackageManager.Pacman);
            AddAsync("apt,pkg,apt-get", pkg, "apt <install|remove|search|list>", "Compatibility wrapper around pacman", PackageManager.Apt);
            AddAsync("module", pkg, "module install <path> | module list | module run <name> [args] | module remove <name>",
                "Install and run StyleOS Python modules (run 'modules install modules' first)", HandleModuleCommand);
            Add("zip", pkg, "zip <archive.zip> <files...>", "Create a zip archive", ArchiveCommands.Zip);
            Add("unzip", pkg, "unzip <archive.zip> [-d dir]", "Extract a zip archive", ArchiveCommands.Unzip);
            Add("tar", pkg, "tar -czf <archive> <files> | tar -xzf <archive>", "Archive files", ArchiveCommands.Tar);
            Add("gzip", pkg, "gzip <file>", "Compress a file", a => ArchiveCommands.Gzip(a, false));
            Add("gunzip", pkg, "gunzip <file.gz>", "Decompress a file", a => ArchiveCommands.Gzip(a, true));
            Add("md5sum", pkg, "md5sum <file>", "MD5 checksum", a => HashCommands.Hash("md5", a));
            Add("sha1sum", pkg, "sha1sum <file>", "SHA1 checksum", a => HashCommands.Hash("sha1", a));
            Add("sha256sum", pkg, "sha256sum <file>", "SHA256 checksum", a => HashCommands.Hash("sha256", a));
            Add("sha512sum", pkg, "sha512sum <file>", "SHA512 checksum", a => HashCommands.Hash("sha512", a));
            Add("base64", pkg, "base64 [-d] <file>", "Encode or decode base64", HashCommands.Base64);

            // ---- shell -------------------------------------------------------
            Add("echo", shell, "echo [-n -e] <text>", "Print text", MiscCommands.Echo);
            Add("printf", shell, "printf <format> [args]", "Formatted print", MiscCommands.Printf);
            Add("seq", shell, "seq [first [step]] last", "Print a sequence of numbers", MiscCommands.Seq);
            Add("yes", shell, "yes [text]", "Repeat text", MiscCommands.Yes);
            Add("clear,cls", shell, "clear", "Clear the screen", a => Console.Clear());
            Add("history", shell, "history [-c] [n]", "Command history", MiscCommands.History);
            Add("alias", shell, "alias [name=value]", "Show or define aliases", MiscCommands.Alias);
            Add("unalias", shell, "unalias <name>", "Remove an alias", MiscCommands.Unalias);
            Add("export,set", shell, "export NAME=value", "Set an environment variable", MiscCommands.Export);
            Add("unset", shell, "unset NAME", "Remove an environment variable", a => { foreach (var n in Io.Operands(a)) ShellEnv.Unset(n); });
            Add("which,whereis,type", shell, "which <command>", "Locate a command", MiscCommands.Which);
            Add("calc,bc", shell, "calc <expression>", "Evaluate an expression", MiscCommands.Calc);
            Add("expr", shell, "expr <expression>", "Evaluate an expression", MiscCommands.Expr);
            Add("true", shell, "true", "Do nothing, successfully", a => ShellEnv.ExitCode = 0);
            Add("false", shell, "false", "Do nothing, unsuccessfully", a => ShellEnv.ExitCode = 1);
            Add("theme,color", shell, "theme <prompt|dir|text|bg|reset> <color>", "Change the colour scheme", MiscCommands.Theme);
            Add("help", shell, "help [category]", "List available commands", Help);
            Add("man,info", shell, "man <command>", "Show the manual entry of a command", Man);
            Add("cowsay", shell, "cowsay <text>", "A cow says something", MiscCommands.Cowsay);
            Add("fortune", shell, "fortune", "Print a random saying", a => MiscCommands.Fortune());
            Add("banner,figlet", shell, "banner <text>", "Print large text", MiscCommands.Banner);

            // ---- apps and session -------------------------------------------
            Add("nano,edit", apps, "nano <file>", "Edit a text file", NanoEditor.Run, true);
            Add("bugreport", apps, "bugreport", "Send a bug report to the developer", a => BugReportEditor.Run(), true);
            Add("open,xdg-open,start", apps, "open <file|url>", "Open something with the host system", MiscCommands.Open);

            Add("exit,logout", shell, "exit", "Log out of the session", a =>
            {
                Kernel.CurrentUser = null;
                Console.WriteLine("logout");
            });

            Add("reboot", system, "reboot", "Restart StyleOS", a =>
            {
                if (Kernel.CurrentUser?.IsRoot != true && !AuthSystem.RequirePassword()) return;
                SystemLogger.Log("SYSTEM", "Reboot requested");
                Console.WriteLine("Rebooting system...");
                Kernel.RequestReboot = true;
            });

            Add("shutdown,poweroff,halt", system, "shutdown", "Power off StyleOS", a =>
            {
                if (Kernel.CurrentUser?.IsRoot != true && !AuthSystem.RequirePassword()) return;
                SystemLogger.Log("SYSTEM", "Shutdown requested");
                Console.WriteLine("Powering off...");
                Kernel.IsRunning = false;
                Kernel.CurrentUser = null;
            });
        }

        private static async Task HandleModulesCommand(List<string> args)
        {
            var operands = Io.Operands(args);

            if (operands.Count >= 2 && operands[0].Equals("install", StringComparison.OrdinalIgnoreCase)
                                     && operands[1].Equals("modules", StringComparison.OrdinalIgnoreCase))
            {
                string version = operands.Count >= 3 ? operands[2] : null;
                await PythonModules.Bootstrap(version);
                return;
            }

            if (operands.Count >= 1 && operands[0].Equals("install", StringComparison.OrdinalIgnoreCase))
            {
                Io.Error("modules", "did you mean: modules install modules   (sets up the Python bridge)");
                return;
            }

            ModuleSystem.PrintLsmod();
        }

        private static async Task HandleModuleCommand(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0)
            {
                Io.Error("module", "usage: module install <path> | module list | module run <name> [args] | module remove <name>");
                return;
            }

            string action = operands[0].ToLower();
            var rest = operands.Skip(1).ToList();

            switch (action)
            {
                case "install":
                    if (rest.Count == 0) { Io.Error("module", "usage: module install <path to module folder>"); return; }
                    await PythonModules.Install(rest[0]);
                    return;

                case "list":
                    PythonModules.List();
                    return;

                case "remove":
                case "uninstall":
                    if (rest.Count == 0) { Io.Error("module", "usage: module remove <name>"); return; }
                    PythonModules.Remove(rest[0]);
                    return;

                case "run":
                    if (rest.Count == 0) { Io.Error("module", "usage: module run <name> [args...]"); return; }
                    await PythonModules.Run(rest[0], rest.Skip(1).ToList());
                    return;

                default:
                    Io.Error("module", $"unknown action '{action}' (install, list, remove, run)");
                    return;
            }
        }

        /// <summary>Runs a whole line: aliases, ;, &amp;&amp;, ||, pipes and redirections.</summary>
        public static async Task ExecuteLine(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            input = ShellEnv.ApplyAlias(input, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            var pipelines = CommandLine.Parse(input);
            bool shouldRun = true;

            foreach (var pipeline in pipelines)
            {
                if (shouldRun) await RunPipeline(pipeline);

                shouldRun = pipeline.NextOp switch
                {
                    ChainOp.And => ShellEnv.ExitCode == 0,
                    ChainOp.Or => ShellEnv.ExitCode != 0,
                    _ => true
                };

                if (!Kernel.IsRunning || Kernel.RequestReboot || Kernel.CurrentUser == null) break;
            }
        }

        private static async Task RunPipeline(Pipeline pipeline)
        {
            string pipeBuffer = null;
            var originalOut = Console.Out;

            try
            {
                for (int i = 0; i < pipeline.Stages.Count; i++)
                {
                    var stage = pipeline.Stages[i];
                    bool isLast = i == pipeline.Stages.Count - 1;

                    ShellEnv.StdIn = pipeBuffer;

                    if (stage.RedirectIn != null)
                    {
                        string path = PathUtil.Resolve(stage.RedirectIn);
                        if (!File.Exists(path)) { Io.Error("shell", $"{stage.RedirectIn}: No such file or directory"); return; }
                        ShellEnv.StdIn = File.ReadAllText(path);
                    }

                    bool toFile = stage.RedirectOut != null || stage.RedirectAppend != null;
                    bool capture = !isLast || toFile;

                    if (capture && IsInteractive(stage.Name))
                    {
                        Io.Error(stage.Name, "this command cannot be used in a pipe or redirection");
                        return;
                    }

                    StringWriter writer = null;
                    if (capture)
                    {
                        writer = new StringWriter();
                        Console.SetOut(writer);
                    }

                    try { await Dispatch(stage); }
                    finally
                    {
                        if (capture) Console.SetOut(originalOut);
                    }

                    if (!capture) continue;

                    string output = writer.ToString();

                    if (toFile)
                    {
                        string target = PathUtil.Resolve(stage.RedirectOut ?? stage.RedirectAppend);
                        try
                        {
                            if (stage.RedirectAppend != null) File.AppendAllText(target, output);
                            else File.WriteAllText(target, output);
                        }
                        catch (Exception ex) { Io.Error("shell", ex.Message); }

                        pipeBuffer = null;
                    }
                    else pipeBuffer = output;
                }
            }
            finally
            {
                Console.SetOut(originalOut);
                ShellEnv.StdIn = null;
            }
        }

        private static bool IsInteractive(string name) =>
            Commands.TryGetValue(name, out var info) && info.Interactive;

        private static async Task Dispatch(CommandStage stage)
        {
            string name = stage.Name;
            var args = stage.Args;

            // VAR=value with no command is an assignment
            if (!Commands.ContainsKey(name) && name.Contains('=') && !name.StartsWith("="))
            {
                int equals = name.IndexOf('=');
                ShellEnv.Set(name.Substring(0, equals), name.Substring(equals + 1));
                ShellEnv.ExitCode = 0;
                return;
            }

            if (!Commands.TryGetValue(name, out var command))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{name}: command not found");
                Console.ResetColor();

                var suggestion = Commands.Keys
                    .Where(k => k.StartsWith(name.Substring(0, Math.Min(2, name.Length)), StringComparison.OrdinalIgnoreCase))
                    .OrderBy(k => k.Length)
                    .Take(3).ToList();

                if (suggestion.Count > 0) Console.WriteLine($"Did you mean: {string.Join(", ", suggestion)}?");

                ShellEnv.ExitCode = 127;
                return;
            }

            ShellEnv.ExitCode = 0;
            await command.Run(args);
        }

        private static void Help(List<string> args)
        {
            var distinct = Commands.Values.Distinct().ToList();
            var operands = Io.Operands(args);

            if (operands.Count > 0)
            {
                string wanted = operands[0];
                var matches = distinct.Where(c => string.Equals(c.Category, wanted, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matches.Count == 0) { Man(args); return; }

                Console.WriteLine($"{wanted} commands:\n");
                foreach (var command in matches.OrderBy(c => c.Name))
                    Console.WriteLine($"  {command.Name,-14}{command.Description}");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n{Kernel.DistroName} {Kernel.Version} - {distinct.Count} commands available");
            Console.ResetColor();
            Console.WriteLine("Type 'man <command>' for details, or 'help <category>' for one group.\n");

            foreach (var group in distinct.GroupBy(c => c.Category).OrderBy(g => g.Key))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"  {group.Key,-11}");
                Console.ResetColor();

                string line = string.Join(", ", group.Select(c => c.Name).OrderBy(n => n));
                int indent = 13;
                int width = Math.Max(30, ConsoleHost.Width - indent - 2);

                foreach (var chunk in Wrap(line, width).Select((t, index) => new { t, index }))
                    Console.WriteLine(chunk.index == 0 ? chunk.t : new string(' ', indent) + chunk.t);
            }

            Console.WriteLine("\n  Pipes and redirection work too:  ls -l | grep .cs > list.txt");
            Console.WriteLine();
        }

        private static IEnumerable<string> Wrap(string text, int width)
        {
            var words = text.Split(' ');
            var line = "";
            foreach (var word in words)
            {
                if (line.Length + word.Length + 1 > width) { yield return line.TrimEnd(); line = ""; }
                line += word + " ";
            }
            if (line.Length > 0) yield return line.TrimEnd();
        }

        private static void Man(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("man", "what manual page do you want?"); return; }

            if (!Commands.TryGetValue(operands[0], out var command))
            {
                Io.Error("man", $"no manual entry for {operands[0]}");
                return;
            }

            var aliases = Commands.Where(kv => kv.Value == command).Select(kv => kv.Key).OrderBy(k => k).ToList();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n{command.Name.ToUpper()}(1)                 {Kernel.DistroName} Manual");
            Console.ResetColor();
            Console.WriteLine($"\nNAME\n    {command.Name} - {command.Description}");
            Console.WriteLine($"\nSYNOPSIS\n    {command.Usage}");
            Console.WriteLine($"\nCATEGORY\n    {command.Category}");
            if (aliases.Count > 1) Console.WriteLine($"\nALIASES\n    {string.Join(", ", aliases)}");
            if (command.Interactive) Console.WriteLine("\nNOTES\n    Interactive command: it cannot be piped or redirected.");
            Console.WriteLine();
        }
    }
}
