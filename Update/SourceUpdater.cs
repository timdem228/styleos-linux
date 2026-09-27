using System;
using System.IO;
using System.Threading.Tasks;

namespace StyleOS
{
    /// <summary>
    /// "pacman update" for source installs - what `styleos install` sets up, or a manual
    /// git clone + dotnet build: fetch, show the new commits, git pull, rebuild into a
    /// staging folder, swap the files in, restart in the same terminal.
    /// </summary>
    public static class SourceUpdater
    {
        public static string SourceDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("STYLEOS_SOURCE_DIR");
                if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(Path.Combine(env, ".git"))) return env;

                // bin/Release/net8.0 inside a checkout: walk up looking for the repo root.
                var dir = new DirectoryInfo(Kernel.BaseDir);
                for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
                        File.Exists(Path.Combine(dir.FullName, "styleos.csproj")))
                        return dir.FullName;
                }
                return null;
            }
        }

        public static bool IsSourceInstall => SourceDir != null && HostPlatform.FindExecutable("git") != null;

        public static async Task Run()
        {
            string src = SourceDir;
            Console.WriteLine($":: Source install detected ({PathUtil.Display(src)})");
            Console.WriteLine(":: Fetching from GitHub...");

            if (Git(src, "fetch", "--quiet", "origin") == null)
            {
                Io.Error("pacman", "git fetch failed - check the network connection");
                return;
            }

            string branch = Git(src, "rev-parse", "--abbrev-ref", "HEAD")?.Trim();
            if (string.IsNullOrEmpty(branch) || branch == "HEAD") branch = "main";

            int.TryParse(Git(src, "rev-list", "--count", $"HEAD..origin/{branch}")?.Trim(), out int behind);
            if (behind == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($" there is nothing to do - {Kernel.DistroName} {Kernel.Version} is up to date (branch {branch})");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n:: {behind} new commit(s) on {branch}:");
            Console.ResetColor();
            string log = Git(src, "log", "--oneline", "-n", "12", $"HEAD..origin/{branch}");
            if (!string.IsNullOrWhiteSpace(log))
                foreach (var line in log.TrimEnd().Split('\n')) Console.WriteLine("  " + line);

            Console.Write("\n:: Proceed with installation? [y/N]: ");
            string answer = Console.ReadLine()?.Trim().ToLower();
            if (answer != "y" && answer != "yes") { Console.WriteLine("Update aborted."); return; }
            if (!AuthSystem.RequirePassword()) { Io.Error("pacman", "authentication failed, update aborted"); return; }

            if (Git(src, "pull", "--ff-only", "--quiet", "origin", branch) == null)
            {
                Io.Error("pacman", $"git pull failed (local changes?) - sort it out in {src}");
                return;
            }

            if (HostPlatform.FindExecutable("dotnet") == null)
            {
                Io.Error("pacman", "dotnet SDK not found - run 'styleos install' on the host to set it up");
                return;
            }

            string staging = Path.Combine(Path.GetTempPath(), $"styleos_build_{Environment.ProcessId}");
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }

            Console.WriteLine(":: Building (dotnet publish)...");
            int code = HostPlatform.RunStreaming("dotnet", "publish", Path.Combine(src, "styleos.csproj"),
                "-c", "Release", "-o", staging, "-p:UseAppHost=false", "--nologo", "-v", "quiet");
            if (code != 0)
            {
                Io.Error("pacman", $"build failed (exit {code}) - nothing was changed");
                return;
            }

            string installDir = Environment.GetEnvironmentVariable("STYLEOS_INSTALL_DIR");
            if (string.IsNullOrWhiteSpace(installDir)) installDir = Kernel.BaseDir;

            int files = UpdateSystem.ApplyStagedFiles(staging, installDir);
            try { Directory.Delete(staging, true); } catch { }
            SystemLogger.Log("PACMAN", $"Source update: {behind} commit(s), {files} files replaced");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($":: Updated ({files} files). Restarting...");
            Console.ResetColor();
            await Task.Delay(600);
            UpdateSystem.Restart();
        }

        private static string Git(string dir, params string[] args)
        {
            var all = new string[args.Length + 2];
            all[0] = "-C";
            all[1] = dir;
            Array.Copy(args, 0, all, 2, args.Length);
            return HostPlatform.Run("git", 120000, all);
        }
    }
}
