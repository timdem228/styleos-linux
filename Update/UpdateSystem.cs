using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StyleOS
{
    public class ReleaseInfo
    {
        public string Tag;
        public string Name;
        public string Body;
        public bool IsPrerelease;
        public string DownloadUrl;
        public string ArchiveFileName;
        public string SumsUrl;
        public string SignatureUrl;
        public DateTime Published;
    }

    public static class UpdateSystem
    {
        public const string StableChannel = "stable";
        public const string BetaChannel = "beta";

        public static string LatestVersionAvailable { get; private set; }
        public static string LatestChannel { get; private set; }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("User-Agent", $"StyleOS-Client/{Kernel.Version}");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return client;
        }

        public static async Task CheckOnBootAsync()
        {
            if (!Kernel.Config.CheckUpdatesOnBoot) return;

            try
            {
                string channel = Kernel.Config.UpdateChannel ?? StableChannel;
                var release = await FetchLatest(channel);

                if (release != null && IsNewerVersion(Kernel.Version, release.Tag))
                {
                    LatestVersionAvailable = release.Tag;
                    LatestChannel = channel;
                }
            }
            catch (Exception ex) { SystemLogger.Log("UPDATE", "Boot check failed: " + ex.Message); }
        }

        public static void PrintBootNotification()
        {
            if (string.IsNullOrEmpty(LatestVersionAvailable)) return;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n[INFO] Available new release: {Kernel.DistroName} {LatestVersionAvailable} ({LatestChannel} channel)");
            Console.WriteLine($"       Run 'pacman update{(LatestChannel == BetaChannel ? " beta" : "")}' to install it.\n");
            Console.ResetColor();
        }

        /// <summary>
        /// stable = the GitHub "latest release", beta = the newest release of any kind,
        /// prereleases included.
        /// </summary>
        public static async Task<ReleaseInfo> FetchLatest(string channel)
        {
            using var client = CreateClient();

            if (channel == BetaChannel)
            {
                string json = await client.GetStringAsync($"https://api.github.com/repos/{Kernel.Repo}/releases?per_page=30");
                using var document = JsonDocument.Parse(json);

                var releases = document.RootElement.EnumerateArray()
                    .Select(Parse)
                    .Where(r => r != null)
                    .OrderByDescending(r => r.Published)
                    .ToList();

                // Prefer an actual prerelease, but never offer something older than stable.
                var newest = releases.FirstOrDefault();
                var prerelease = releases.FirstOrDefault(r => r.IsPrerelease);

                if (prerelease != null && newest != null && IsNewerVersion(prerelease.Tag, newest.Tag)) return newest;
                return prerelease ?? newest;
            }

            string latestJson = await client.GetStringAsync($"https://api.github.com/repos/{Kernel.Repo}/releases/latest");
            using var latest = JsonDocument.Parse(latestJson);
            return Parse(latest.RootElement);
        }

        /// <summary>
        /// "whatsnew [version]" - looks at that release's asset list on GitHub for a
        /// whatsnew.txt file and prints it. No version given means the version currently
        /// running. Missing tag, missing file, or any network hiccup all end up with the
        /// same plain "no such feature" message rather than a wall of error text.
        /// </summary>
        public static async Task WhatsNew(string version)
        {
            string tag = string.IsNullOrWhiteSpace(version) ? Kernel.Version : version.Trim().TrimStart('v', 'V');

            try
            {
                using var client = CreateClient();
                string json = await client.GetStringAsync(
                    $"https://api.github.com/repos/{Kernel.Repo}/releases/tags/{Uri.EscapeDataString(tag)}");

                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("assets", out var assets))
                {
                    Console.WriteLine("sorry, there is no such feature in this version.");
                    return;
                }

                string downloadUrl = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.Equals(name, "whatsnew.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Console.WriteLine("sorry, there is no such feature in this version.");
                    return;
                }

                string text = await client.GetStringAsync(downloadUrl);
                Console.WriteLine();
                Console.WriteLine(text.TrimEnd());
            }
            catch
            {
                // Unknown tag (404), network error, malformed response - a user asking
                // "what's new in X" doesn't need to see any of that, just the same answer
                // as "there's nothing here".
                Console.WriteLine("sorry, there is no such feature in this version.");
            }
        }

        private static ReleaseInfo Parse(JsonElement element)
        {
            try
            {
                var info = new ReleaseInfo
                {
                    Tag = element.GetProperty("tag_name").GetString(),
                    Name = element.TryGetProperty("name", out var n) ? n.GetString() : "",
                    Body = element.TryGetProperty("body", out var b) ? b.GetString() : "",
                    IsPrerelease = element.TryGetProperty("prerelease", out var p) && p.GetBoolean(),
                    Published = element.TryGetProperty("published_at", out var d) && d.TryGetDateTime(out var dt) ? dt : DateTime.MinValue
                };

                if (element.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                {
                    var zip = assets.EnumerateArray()
                        .FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                    var chosen = zip.ValueKind == JsonValueKind.Object ? zip : assets[0];
                    info.DownloadUrl = chosen.GetProperty("browser_download_url").GetString();
                    info.ArchiveFileName = chosen.GetProperty("name").GetString();

                    foreach (var asset in assets.EnumerateArray())
                    {
                        string assetName = asset.TryGetProperty("name", out var an) ? an.GetString() : "";
                        if (string.Equals(assetName, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                            info.SumsUrl = asset.GetProperty("browser_download_url").GetString();
                        else if (string.Equals(assetName, "SHA256SUMS.txt.sig", StringComparison.OrdinalIgnoreCase))
                            info.SignatureUrl = asset.GetProperty("browser_download_url").GetString();
                    }
                }

                return info;
            }
            catch { return null; }
        }

        public static async Task RunUpdateProcess(string channel)
        {
            channel = channel == BetaChannel ? BetaChannel : StableChannel;
            SystemLogger.Log("PACMAN", $"Checking for updates on the {channel} channel");

            Console.WriteLine($":: Synchronising package databases ({channel} channel)...");

            ReleaseInfo release;
            try { release = await FetchLatest(channel); }
            catch (Exception ex) { Io.Error("pacman", $"failed to reach the repository: {ex.Message}"); return; }

            if (release == null || string.IsNullOrEmpty(release.Tag))
            {
                Io.Error("pacman", $"no releases published on the {channel} channel yet");
                return;
            }

            if (!IsNewerVersion(Kernel.Version, release.Tag))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($" there is nothing to do - {Kernel.DistroName} {Kernel.Version} is up to date ({channel}: {release.Tag})");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n:: {release.Tag}{(release.IsPrerelease ? "  [prerelease]" : "")} is available (you have {Kernel.Version})");
            Console.ResetColor();

            if (!string.IsNullOrWhiteSpace(release.Body))
            {
                Console.WriteLine("\nChangelog:");
                foreach (var line in release.Body.Split('\n').Take(12)) Console.WriteLine("  " + line.TrimEnd());
                Console.WriteLine();
            }

            if (release.IsPrerelease)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Warning: this is a beta build and may be unstable.");
                Console.ResetColor();
            }

            Console.Write(":: Proceed with installation? [y/N]: ");
            string answer = Console.ReadLine()?.Trim().ToLower();
            if (answer != "y" && answer != "yes") { Console.WriteLine("Update aborted."); return; }

            if (!AuthSystem.RequirePassword()) { Io.Error("pacman", "authentication failed, update aborted"); return; }

            if (string.IsNullOrEmpty(release.DownloadUrl))
            {
                Io.Error("pacman", "this release has no downloadable artifact attached");
                return;
            }

            await Install(release);
        }

        private static async Task Install(ReleaseInfo release)
        {
            string tempZip = Path.Combine(Path.GetTempPath(), "styleos_update.zip");
            string extractPath = Path.Combine(Path.GetTempPath(), "styleos_update_extracted");

            try
            {
                using var client = CreateClient();

                Console.WriteLine($":: Downloading {release.Tag}...");
                byte[] data = await client.GetByteArrayAsync(release.DownloadUrl);

                if (!string.IsNullOrEmpty(release.SumsUrl) && !string.IsNullOrEmpty(release.SignatureUrl))
                {
                    Console.WriteLine(":: Verifying release signature...");
                    byte[] sums = await client.GetByteArrayAsync(release.SumsUrl);
                    byte[] signature = await client.GetByteArrayAsync(release.SignatureUrl);

                    var status = ReleaseVerifier.Verify(data, release.ArchiveFileName, sums, signature);
                    if (status == SignatureStatus.Invalid)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(":: SIGNATURE CHECK FAILED.");
                        Console.WriteLine("   This download does not match its published signature - refusing to install it.");
                        Console.WriteLine("   Do not trust this file. If this keeps happening, please report it.");
                        Console.ResetColor();
                        SystemLogger.Log("PACMAN", $"Signature verification FAILED for {release.Tag}");
                        return;
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("   signature verified.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(":: This release isn't signed - proceeding without verification.");
                    Console.ResetColor();
                }

                if (File.Exists(tempZip)) File.Delete(tempZip);
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);

                File.WriteAllBytes(tempZip, data);
                Console.WriteLine($":: Downloaded {PathUtil.HumanSize(data.Length)}, extracting...");

                ZipFile.ExtractToDirectory(tempZip, extractPath);

                // A release zip often wraps everything in one folder - unwrap it so the
                // files land next to the exe instead of in a nested directory.
                var entries = Directory.GetFileSystemEntries(extractPath);
                if (entries.Length == 1 && Directory.Exists(entries[0])) extractPath = entries[0];

                string currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExe)) { Io.Error("pacman", "cannot locate the running executable"); return; }

                string currentDir = Path.GetDirectoryName(currentExe);
                string updater = Path.Combine(Path.GetTempPath(), "styleos_updater.bat");

                string batch = $@"@echo off
title {Kernel.DistroName} Updater
echo Applying update {release.Tag}, please wait...
ping 127.0.0.1 -n 3 > nul
xcopy /Y /E /I ""{extractPath}\*"" ""{currentDir}""
echo Cleaning up...
del ""{tempZip}"" 2> nul
rmdir /S /Q ""{extractPath}"" 2> nul
echo Restarting {Kernel.DistroName}...
start """" ""{currentExe}""
del ""%~f0""
";
                File.WriteAllText(updater, batch);

                Process.Start(new ProcessStartInfo
                {
                    FileName = updater,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                SystemLogger.Log("PACMAN", $"Installing {release.Tag}");
                Console.WriteLine(":: Update staged. The system will restart to apply it...");
                Thread.Sleep(1200);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Io.Error("pacman", $"update failed: {ex.Message}");
                SystemLogger.Log("PACMAN", "Update failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Semver-ish comparison that understands prereleases:
        /// 1.1.6-beta2 &lt; 1.1.6, and 1.1.6-beta10 &gt; 1.1.6-beta2.
        /// </summary>
        public static bool IsNewerVersion(string current, string candidate)
        {
            try
            {
                var c = Split(current);
                var n = Split(candidate);

                for (int i = 0; i < 4; i++)
                {
                    if (n.Numbers[i] > c.Numbers[i]) return true;
                    if (n.Numbers[i] < c.Numbers[i]) return false;
                }

                // Same numbers: a release without a suffix beats any prerelease.
                if (string.IsNullOrEmpty(n.Suffix) && !string.IsNullOrEmpty(c.Suffix)) return true;
                if (!string.IsNullOrEmpty(n.Suffix) && string.IsNullOrEmpty(c.Suffix)) return false;
                if (string.IsNullOrEmpty(n.Suffix) && string.IsNullOrEmpty(c.Suffix)) return false;

                return CompareSuffix(n.Suffix, c.Suffix) > 0;
            }
            catch { return false; }
        }

        private static (int[] Numbers, string Suffix) Split(string version)
        {
            string clean = (version ?? "").Trim().TrimStart('v', 'V').Trim();

            int dash = clean.IndexOfAny(new[] { '-', '+' });
            string suffix = dash >= 0 ? clean.Substring(dash + 1) : "";
            string numbers = dash >= 0 ? clean.Substring(0, dash) : clean;

            var parts = numbers.Split('.');
            var result = new int[4];
            for (int i = 0; i < 4 && i < parts.Length; i++) int.TryParse(parts[i], out result[i]);

            return (result, suffix.ToLowerInvariant());
        }

        private static int CompareSuffix(string a, string b)
        {
            var left = SplitSuffix(a);
            var right = SplitSuffix(b);

            for (int i = 0; i < Math.Max(left.Count, right.Count); i++)
            {
                string l = i < left.Count ? left[i] : "";
                string r = i < right.Count ? right[i] : "";

                bool lNum = int.TryParse(l, out int li);
                bool rNum = int.TryParse(r, out int ri);

                if (lNum && rNum) { if (li != ri) return li.CompareTo(ri); continue; }

                int cmp = string.Compare(l, r, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            return 0;
        }

        private static List<string> SplitSuffix(string suffix)
        {
            var parts = new List<string>();
            var buffer = new System.Text.StringBuilder();
            bool digits = false;

            foreach (char c in suffix)
            {
                if (c == '.' || c == '-') { if (buffer.Length > 0) { parts.Add(buffer.ToString()); buffer.Clear(); } continue; }

                bool isDigit = char.IsDigit(c);
                if (buffer.Length > 0 && isDigit != digits) { parts.Add(buffer.ToString()); buffer.Clear(); }

                digits = isDigit;
                buffer.Append(c);
            }

            if (buffer.Length > 0) parts.Add(buffer.ToString());
            return parts;
        }
    }
}
