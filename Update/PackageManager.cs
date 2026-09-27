using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace StyleOS
{
    public static class PackageManager
    {
        private const string RepoUrl = "http://api.timd.site/sos/r/";

        public static List<string> Installed()
        {
            try
            {
                if (!File.Exists(Kernel.PkgFile)) return new List<string>();
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Kernel.PkgFile)) ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        public static int InstalledCount() => Installed().Count;

        private static void Save(List<string> packages)
        {
            try { File.WriteAllText(Kernel.PkgFile, JsonSerializer.Serialize(packages)); }
            catch (Exception ex) { SystemLogger.Log("PKG", "Cannot save package list: " + ex.Message); }
        }

        public static async Task Pacman(List<string> args)
        {
            if (args.Count == 0)
            {
                PrintUsage();
                return;
            }

            string action = args[0].ToLower();
            var rest = args.Skip(1).ToList();

            switch (action)
            {
                case "update":
                case "-syu":
                case "upgrade":
                    {
                        // "pacman update beta" and "pacman update --beta" both pick the beta channel.
                        string channel = rest.Any(a => a.Trim('-').ToLower() is "beta" or "prerelease" or "dev")
                            ? UpdateSystem.BetaChannel
                            : rest.Any(a => a.Trim('-').ToLower() == "stable")
                                ? UpdateSystem.StableChannel
                                : (Kernel.Config.UpdateChannel ?? UpdateSystem.StableChannel);

                        await UpdateSystem.RunUpdateProcess(channel);
                        return;
                    }

                case "channel":
                    {
                        if (rest.Count == 0)
                        {
                            Console.WriteLine($"Current update channel: {Kernel.Config.UpdateChannel}");
                            Console.WriteLine("Use 'pacman channel beta' or 'pacman channel stable' to switch.");
                            return;
                        }

                        string wanted = rest[0].ToLower();
                        if (wanted != UpdateSystem.BetaChannel && wanted != UpdateSystem.StableChannel)
                        {
                            Io.Error("pacman", $"unknown channel '{rest[0]}' (use stable or beta)");
                            return;
                        }

                        Kernel.Config.UpdateChannel = wanted;
                        ConfigManager.SaveConfig();
                        Console.WriteLine($":: Update channel set to {wanted}.");
                        return;
                    }

                case "check":
                    {
                        string channel = rest.Count > 0 && rest[0].ToLower() == UpdateSystem.BetaChannel
                            ? UpdateSystem.BetaChannel
                            : (Kernel.Config.UpdateChannel ?? UpdateSystem.StableChannel);

                        var release = await UpdateSystem.FetchLatest(channel);
                        if (release == null) { Io.Error("pacman", "no releases found"); return; }

                        Console.WriteLine($"Installed: {Kernel.Version}");
                        Console.WriteLine($"{channel,-9}: {release.Tag}{(release.IsPrerelease ? " [prerelease]" : "")}");
                        Console.WriteLine(UpdateSystem.IsNewerVersion(Kernel.Version, release.Tag)
                            ? ":: An update is available."
                            : ":: System is up to date.");
                        return;
                    }

                case "-s":
                case "install":
                    await Install(rest);
                    return;

                case "-r":
                case "remove":
                    Remove(rest);
                    return;

                case "-q":
                case "list":
                    {
                        var installed = Installed();
                        if (installed.Count == 0) { Console.WriteLine("No packages installed."); return; }
                        foreach (var package in installed) Console.WriteLine($"{package} (local)");
                        return;
                    }

                case "-ss":
                case "search":
                    await Search(rest);
                    return;

                case "help":
                case "-h":
                case "--help":
                    PrintUsage();
                    return;

                default:
                    Io.Error("pacman", $"invalid operation '{action}' (try 'pacman help')");
                    return;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine($"pacman - {Kernel.DistroName} package manager");
            Console.WriteLine("  pacman update            update the system on the current channel");
            Console.WriteLine("  pacman update beta       update to the newest beta / prerelease build");
            Console.WriteLine("  pacman update stable     update to the newest stable release");
            Console.WriteLine("  pacman channel <name>    remember stable or beta as the default channel");
            Console.WriteLine("  pacman check [beta]      only look, do not install");
            Console.WriteLine("  pacman -S <package>      install a package from the StyleOS repo");
            Console.WriteLine("  pacman -R <package>      remove an installed package");
            Console.WriteLine("  pacman -Q                list installed packages");
            Console.WriteLine("  pacman -Ss <name>        search the repository");
        }

        private static async Task Install(List<string> args)
        {
            var names = Io.Operands(args);
            if (names.Count == 0) { Io.Error("pacman", "no targets specified"); return; }

            var installed = Installed();

            foreach (var raw in names)
            {
                string package = raw.ToLower();

                if (installed.Contains(package))
                {
                    Console.WriteLine($" {package} is up to date -- reinstalling");
                }

                Console.WriteLine($":: Resolving {package}...");

                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                    client.DefaultRequestHeaders.Add("User-Agent", $"StyleOS/{Kernel.Version}");

                    var meta = await client.GetAsync(RepoUrl + package + ".txt");
                    if (!meta.IsSuccessStatusCode)
                    {
                        Io.Error("pacman", $"target not found: {package}");
                        continue;
                    }

                    string downloadUrl = (await meta.Content.ReadAsStringAsync()).Trim();
                    if (!downloadUrl.StartsWith("http://") && !downloadUrl.StartsWith("https://"))
                    {
                        Io.Error("pacman", "invalid repository metadata");
                        continue;
                    }

                    Console.WriteLine($":: Downloading {package}...");
                    byte[] payload = await client.GetByteArrayAsync(downloadUrl);

                    string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                    if (string.IsNullOrEmpty(fileName)) fileName = package + ".bin";

                    string destination = Path.Combine(Kernel.CurrentDirectory, fileName);
                    File.WriteAllBytes(destination, payload);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($":: Installed {fileName} ({PathUtil.HumanSize(payload.Length)})");
                    Console.ResetColor();

                    if (!installed.Contains(package)) installed.Add(package);
                    Save(installed);
                    SystemLogger.Log("PKG", $"Installed {package}");
                }
                catch (Exception ex) { Io.Error("pacman", ex.Message); }
            }
        }

        private static void Remove(List<string> args)
        {
            var names = Io.Operands(args);
            if (names.Count == 0) { Io.Error("pacman", "no targets specified"); return; }

            var installed = Installed();
            foreach (var raw in names)
            {
                string package = raw.ToLower();
                if (!installed.Contains(package))
                {
                    Io.Error("pacman", $"target not found: {package}");
                    continue;
                }

                installed.Remove(package);
                Console.WriteLine($":: Removed {package}");
                SystemLogger.Log("PKG", $"Removed {package}");
            }
            Save(installed);
        }

        private static async Task Search(List<string> args)
        {
            var names = Io.Operands(args);
            if (names.Count == 0) { Io.Error("pacman", "no search terms given"); return; }

            // The old code read args[2] unconditionally here and crashed with
            // ArgumentOutOfRangeException on "pkg search something".
            string term = names[0].ToLower();
            Console.WriteLine($":: Searching the repository for '{term}'...");

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                client.DefaultRequestHeaders.Add("User-Agent", $"StyleOS/{Kernel.Version}");

                var response = await client.GetAsync(RepoUrl + term + ".txt");
                if (response.IsSuccessStatusCode)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"styleos/{term} ");
                    Console.ResetColor();
                    Console.WriteLine($"\n    available in the StyleOS repository ({(await response.Content.ReadAsStringAsync()).Trim()})");
                }
                else Console.WriteLine($"No package named '{term}' in the repository.");
            }
            catch (Exception ex) { Io.Error("pacman", ex.Message); }
        }

        /// <summary>apt / pkg kept as a thin wrapper so old muscle memory still works.</summary>
        public static async Task Apt(List<string> args)
        {
            if (args.Count == 0) { Console.WriteLine("Usage: apt <install|remove|search|list|update> <package>"); return; }

            string action = args[0].ToLower();
            var rest = args.Skip(1).ToList();

            switch (action)
            {
                case "install": await Install(rest); return;
                case "remove": case "purge": Remove(rest); return;
                case "search": await Search(rest); return;
                case "list": await Pacman(new List<string> { "-Q" }); return;
                case "update": case "upgrade": await Pacman(new List<string> { "update" }.Concat(rest).ToList()); return;
                default: Io.Error("apt", $"invalid operation {action}"); return;
            }
        }
    }
}
