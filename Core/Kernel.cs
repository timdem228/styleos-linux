using System;
using System.IO;
using System.Runtime.InteropServices;

namespace StyleOS
{
    /// <summary>
    /// Global kernel state: version, paths and the current session.
    /// Everything that used to live as a static field on Program lives here now.
    /// </summary>
    public static class Kernel
    {
        public const string Version = "1.1.7";
        public const string CoreName = "StyleOS Core";
        public const string Repo = "timdem228/styleos-";
        public const string DistroName = "Style OS";

        public static string CurrentDirectory { get; set; } = Directory.GetCurrentDirectory();
        public static User CurrentUser { get; set; }
        public static bool IsRunning { get; set; } = true;
        public static bool RequestReboot { get; set; }
        public static bool DebugMode { get; set; }
        public static DateTime BootTime { get; set; } = DateTime.Now;
        public static SystemConfig Config { get; set; } = new SystemConfig();

        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string SysDir = Path.Combine(BaseDir, ".styleos");
        public static readonly string ImageDir = Path.Combine(SysDir, "img");
        public static readonly string LogoFile = Path.Combine(ImageDir, "styleos.png");
        public static readonly string UsersFile = Path.Combine(SysDir, "users.json");
        public static readonly string ConfigFile = Path.Combine(SysDir, "config.json");
        public static readonly string HistoryFile = Path.Combine(SysDir, "history.log");
        public static readonly string PkgFile = Path.Combine(SysDir, "packages.json");
        public static readonly string LogFile = Path.Combine(SysDir, "system.log");
        public static readonly string CronFile = Path.Combine(SysDir, "crontab");

        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static string Home
        {
            get
            {
                string h = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return string.IsNullOrEmpty(h) ? BaseDir : h;
            }
        }

        /// <summary>Root of the filesystem as StyleOS sees it.</summary>
        public static string RootPath
        {
            get
            {
                try { return Path.GetPathRoot(BaseDir) ?? "/"; }
                catch { return "/"; }
            }
        }

        /// <summary>What uname / fastfetch report as the kernel.</summary>
        public static string KernelString => $"{CoreName} {Version}-styleos";

        public static string Arch => RuntimeInformation.OSArchitecture.ToString().ToLower();

        public static TimeSpan Uptime => DateTime.Now - BootTime;

        public static void EnsureSystemDirs()
        {
            try
            {
                if (!Directory.Exists(SysDir)) Directory.CreateDirectory(SysDir);
                if (!Directory.Exists(ImageDir)) Directory.CreateDirectory(ImageDir);
                if (!File.Exists(LogFile)) File.Create(LogFile).Close();
            }
            catch { }
        }
    }
}
