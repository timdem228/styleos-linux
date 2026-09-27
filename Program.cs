using System;
using System.Threading;
using System.Threading.Tasks;

namespace StyleOS
{
    /// <summary>
    /// Entry point only. Every subsystem lives in its own module now:
    ///   Core/      kernel state, console host, boot, logging, config, crash handling
    ///   Auth/      users and login
    ///   Shell/     line editor, parser, router, session
    ///   Commands/  the actual commands, grouped by area
    ///   Apps/      full screen programs (nano, bug reporter)
    ///   Update/    pacman and the update channels
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                ConsoleHost.EnableAnsi();
                ConsoleHost.GoFullscreen();

                Kernel.EnsureSystemDirs();
                ConfigManager.LoadConfig();
                AuthSystem.Init();

                BootManager.CheckDebugKey();

                bool needsBoot = true;

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
            catch (Exception ex)
            {
                CrashHandler.HandleCrash(ex);
            }
        }
    }
}
