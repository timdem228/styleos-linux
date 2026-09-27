using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StyleOS
{
    /// <summary>
    /// Entry point only. Every subsystem lives in its own module:
    ///   Core/      kernel state, console host, host platform (Linux / Termux), boot, logging, config
    ///   Auth/      users and login
    ///   Shell/     line editor, parser, router, session
    ///   Commands/  the actual commands, grouped by area
    ///   Apps/      full screen programs (nano, bug reporter)
    ///   Update/    pacman, release + source updates, Python modules
    ///
    /// Linux build extras (all optional - a plain launch works exactly like before):
    ///   -c, --exec "cmd"   run commands without boot/login and exit with their status
    ///   -u, --user NAME    account used by --exec / script mode (default: root)
    ///   script.sos         run a file of StyleOS commands, one per line
    ///   --skip-boot        interactive, but skip the F6 window and the boot animation
    ///   --version, --help
    /// </summary>
    public class Program
    {
        private class CliOptions
        {
            public readonly List<string> Commands = new List<string>();
            public string ScriptFile;
            public string User;
            public bool SkipBoot;
            public bool ShowHelp;
            public bool ShowVersion;
            public string Error;
        }

        public static async Task<int> Main(string[] args)
        {
            var cli = ParseArgs(args);
            if (cli.Error != null)
            {
                Console.Error.WriteLine("StyleOS: " + cli.Error);
                PrintUsage();
                return 2;
            }
            if (cli.ShowHelp) { PrintUsage(); return 0; }
            if (cli.ShowVersion)
            {
                Console.WriteLine($"{Kernel.DistroName} {Kernel.Version} ({Kernel.KernelString}), Linux build");
                return 0;
            }

            try
            {
                if (cli.Commands.Count > 0 || cli.ScriptFile != null) return await RunBatch(cli);

                bool skipBoot = cli.SkipBoot || Environment.GetEnvironmentVariable("STYLEOS_SKIP_BOOT") == "1";
                await RunInteractive(skipBoot);
                return 0;
            }
            catch (Exception ex)
            {
                CrashHandler.HandleCrash(ex);
                return 1;
            }
        }

        private static async Task RunInteractive(bool skipBoot)
        {
            ConsoleHost.EnableAnsi();
            ConsoleHost.GoFullscreen();

            Kernel.EnsureSystemDirs();
            ConfigManager.LoadConfig();
            AuthSystem.Init();

            if (!skipBoot) BootManager.CheckDebugKey();

            bool needsBoot = !skipBoot;

            while (Kernel.IsRunning)
            {
                Kernel.RequestReboot = false;

                if (needsBoot)
                {
                    Kernel.BootTime = DateTime.Now;
                    SystemLogger.Log("SYSTEM", $"Boot sequence for {Kernel.DistroName} {Kernel.Version}");
                    await BootManager.BootSequence();
                    needsBoot = false;
                }
                else
                {
                    // Plain "exit" logs out but is not a reboot - it shouldn't replay the
                    // splash and boot messages every time, only an actual reboot does that.
                    Console.Clear();
                }

                while (Kernel.CurrentUser == null && Kernel.IsRunning && !Kernel.RequestReboot)
                    AuthSystem.LoginPrompt();

                if (Kernel.CurrentUser != null)
                {
                    SystemLogger.Log("AUTH", $"User '{Kernel.CurrentUser.Username}' logged in");
                    await ShellSession.Start();
                }

                if (Kernel.RequestReboot)
                {
                    Kernel.CurrentUser = null;
                    Console.Clear();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("Restarting system...");
                    Console.ResetColor();
                    Thread.Sleep(1200);
                    needsBoot = true;
                }
            }

            ConsoleHost.Reset();
            Console.Clear();
        }

        /// <summary>
        /// Non-interactive mode: no F6 window, no boot, no login prompt. Runs every command
        /// line through the normal router (pipes, redirects, chaining all work) as root or
        /// the --user account, then exits with the last command's status. Used by the smoke
        /// tests, by `styleos run -c ...` and handy for scripting.
        /// </summary>
        private static async Task<int> RunBatch(CliOptions cli)
        {
            ConsoleHost.EnableAnsi(interactive: false);
            Kernel.EnsureSystemDirs();
            ConfigManager.LoadConfig(applyTheme: false);
            AuthSystem.Init();

            string userName = cli.User ?? "root";
            var user = AuthSystem.Find(userName);
            if (user == null)
            {
                Console.Error.WriteLine($"StyleOS: user '{userName}' does not exist");
                return 2;
            }

            Kernel.CurrentUser = user;
            Kernel.CurrentDirectory = Directory.GetCurrentDirectory();
            ShellEnv.InitDefaults();
            ShellEnv.Set("PWD", Kernel.CurrentDirectory);

            var lines = new List<string>(cli.Commands);
            if (cli.ScriptFile != null)
            {
                string path = Path.GetFullPath(cli.ScriptFile);
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"StyleOS: {cli.ScriptFile}: No such file");
                    return 2;
                }
                lines.AddRange(File.ReadAllLines(path));
            }

            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                SystemLogger.Log("EXEC", $"{user.Username}: {line}");
                try
                {
                    await CommandRouter.ExecuteLine(line);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    SystemLogger.Log("EXEC", "Command failed: " + ex);
                    ShellEnv.ExitCode = 1;
                }

                if (!Kernel.IsRunning || Kernel.CurrentUser == null) break;
            }

            Console.Out.Flush();
            return ShellEnv.ExitCode;
        }

        private static CliOptions ParseArgs(string[] args)
        {
            var o = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "-c":
                    case "--exec":
                        if (i + 1 >= args.Length) { o.Error = $"{a} needs a command"; return o; }
                        o.Commands.AddRange(args[++i].Replace("\r\n", "\n").Split('\n'));
                        break;

                    case "-u":
                    case "--user":
                        if (i + 1 >= args.Length) { o.Error = $"{a} needs a user name"; return o; }
                        o.User = args[++i];
                        break;

                    case "--skip-boot": o.SkipBoot = true; break;
                    case "-h": case "--help": o.ShowHelp = true; break;
                    case "-v": case "--version": o.ShowVersion = true; break;

                    default:
                        if (a.StartsWith("-")) { o.Error = $"unknown option '{a}'"; return o; }
                        if (o.ScriptFile != null) { o.Error = "only one script file can be given"; return o; }
                        o.ScriptFile = a;
                        break;
                }
            }
            return o;
        }

        private static void PrintUsage()
        {
            Console.WriteLine($"{Kernel.DistroName} {Kernel.Version} - Linux build");
            Console.WriteLine();
            Console.WriteLine("usage: StyleOS                     boot StyleOS (F6 = debug menu)");
            Console.WriteLine("       StyleOS --skip-boot         straight to the login prompt");
            Console.WriteLine("       StyleOS -c \"cmd; cmd | cmd\"   run commands and exit (--exec)");
            Console.WriteLine("       StyleOS -u bob -c whoami    same, as another StyleOS user");
            Console.WriteLine("       StyleOS script.sos          run a file of commands");
            Console.WriteLine("       StyleOS --version | --help");
            Console.WriteLine();
            Console.WriteLine("Data lives in $STYLEOS_HOME (default: ~/.local/share/styleos).");
        }
    }
}
