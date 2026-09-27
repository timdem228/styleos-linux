using System;
using System.Threading.Tasks;

namespace StyleOS
{
    public static class ShellSession
    {
        public static async Task Start()
        {
            LineEditor.LoadHistory();
            ShellEnv.InitDefaults();
            ShellEnv.Set("PWD", Kernel.CurrentDirectory);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\nWelcome to {Kernel.DistroName} {Kernel.Version} ({Kernel.KernelString})");
            Console.ResetColor();
            Console.WriteLine($"Logged in as {Kernel.CurrentUser.Username}. Type 'help' for the command list, 'man <cmd>' for details.");

            UpdateSystem.PrintBootNotification();

            while (Kernel.CurrentUser != null && Kernel.IsRunning && !Kernel.RequestReboot)
            {
                string input;
                try { input = LineEditor.ReadLine(DrawPrompt); }
                catch (Exception ex)
                {
                    SystemLogger.Log("SHELL", "Input error: " + ex.Message);
                    continue;
                }

                if (input == null) { Kernel.CurrentUser = null; break; }
                if (string.IsNullOrWhiteSpace(input)) continue;

                LineEditor.AddHistory(input);
                SystemLogger.Log("SHELL", $"{Kernel.CurrentUser?.Username}: {input}");

                try
                {
                    await CommandRouter.ExecuteLine(input);
                }
                catch (Exception ex)
                {
                    // One bad command must never take the whole session down.
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.ResetColor();
                    SystemLogger.Log("SHELL", "Command failed: " + ex);
                    ShellEnv.ExitCode = 1;
                }
            }
        }

        public static void DrawPrompt()
        {
            try
            {
                var user = Kernel.CurrentUser;
                if (user == null) return;

                Console.ForegroundColor = Kernel.Config.PromptUserColor;
                Console.Write($"{user.Username}@{ShellEnv.HostName}");

                Console.ForegroundColor = Kernel.Config.DefaultTextColor;
                Console.Write(":");

                Console.ForegroundColor = Kernel.Config.PromptDirColor;
                Console.Write(PathUtil.Display(Kernel.CurrentDirectory));

                Console.ForegroundColor = Kernel.Config.DefaultTextColor;
                Console.Write(user.IsRoot ? "# " : "$ ");
                Console.ResetColor();
                Console.ForegroundColor = Kernel.Config.DefaultTextColor;
            }
            catch { }
        }
    }
}
