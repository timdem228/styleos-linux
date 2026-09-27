using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StyleOS
{
    public class User
    {
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public bool IsRoot { get; set; }
        public int Uid { get; set; }
        public string Shell { get; set; } = "/bin/sh";
        public string HomeDir { get; set; } = "";
    }

    public static class AuthSystem
    {
        private static List<User> Users = new List<User>();
        private static bool _initialized;

        public static IReadOnlyList<User> All => Users;

        /// <summary>
        /// Loads the user database. Safe to call several times: the old version appended a
        /// second "root" on every call, so a reboot loop slowly filled users.json with clones.
        /// </summary>
        public static void Init(bool force = false)
        {
            if (_initialized && !force) return;
            _initialized = true;

            Users = new List<User>();

            if (File.Exists(Kernel.UsersFile))
            {
                try
                {
                    string json = File.ReadAllText(Kernel.UsersFile);
                    Users = JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
                }
                catch (Exception ex)
                {
                    SystemLogger.Log("AUTH", "users.json is broken: " + ex.Message);
                    Users = new List<User>();
                }
            }

            Users = Users.Where(u => u != null && !string.IsNullOrEmpty(u.Username))
                         .GroupBy(u => u.Username)
                         .Select(g => g.First())
                         .ToList();

            if (!Users.Any(u => u.IsRoot))
            {
                Users.Insert(0, new User { Username = "root", PasswordHash = HashPassword(""), IsRoot = true, Uid = 0 });
                SaveUsers();
            }
        }

        public static bool VerifyRootForDebug(string pwd)
        {
            Init();
            var root = Users.FirstOrDefault(u => u.IsRoot);
            return root != null && root.PasswordHash == HashPassword(pwd ?? "");
        }

        public static void SaveUsers()
        {
            try
            {
                File.WriteAllText(Kernel.UsersFile,
                    JsonSerializer.Serialize(Users, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { SystemLogger.Log("AUTH", "Cannot save users: " + ex.Message); }
        }

        public static void LoginPrompt()
        {
            Init();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"{Kernel.DistroName} login: ");
            string username = Console.ReadLine();

            if (username == null) { Kernel.IsRunning = false; return; }
            username = username.Trim();
            if (username.Length == 0) return;

            Console.Write("Password: ");
            string password = ReadPassword();
            Console.WriteLine();

            var user = Users.FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase) &&
                u.PasswordHash == HashPassword(password));

            if (user != null)
            {
                Kernel.CurrentUser = user;
                Kernel.CurrentDirectory = ResolveHome(user);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Login incorrect");
                Console.ResetColor();
                SystemLogger.Log("AUTH", $"Failed login attempt for user '{username}'");
            }
        }

        public static string ResolveHome(User user)
        {
            try
            {
                if (!string.IsNullOrEmpty(user.HomeDir) && Directory.Exists(user.HomeDir)) return user.HomeDir;
                string home = Kernel.Home;
                return Directory.Exists(home) ? home : Kernel.BaseDir;
            }
            catch { return Kernel.BaseDir; }
        }

        public static string ReadPassword()
        {
            var pass = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key;
                try { key = Console.ReadKey(true); }
                catch { return Console.ReadLine() ?? ""; }

                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Escape) return "";
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (pass.Length > 0) pass.Remove(pass.Length - 1, 1);
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    pass.Append(key.KeyChar);
                }
            }
            return pass.ToString();
        }

        public static bool RequirePassword()
        {
            if (Kernel.CurrentUser == null) return false;
            Console.Write($"[sudo] password for {Kernel.CurrentUser.Username}: ");
            string input = ReadPassword();
            Console.WriteLine();

            if (HashPassword(input) == Kernel.CurrentUser.PasswordHash) return true;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Sorry, try again.");
            Console.ResetColor();
            return false;
        }

        public static string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password ?? ""));
                var builder = new StringBuilder();
                foreach (byte b in bytes) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        public static void AddUser(string username, string password)
        {
            Init();
            if (Users.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)))
                throw new Exception($"useradd: user '{username}' already exists");

            int uid = Users.Count == 0 ? 1000 : Math.Max(1000, Users.Max(u => u.Uid) + 1);
            Users.Add(new User { Username = username, PasswordHash = HashPassword(password), IsRoot = false, Uid = uid });
            SaveUsers();
        }

        public static void DeleteUser(string username)
        {
            Init();
            var u = Users.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            if (u == null) throw new Exception($"userdel: user '{username}' does not exist");
            if (u.IsRoot) throw new Exception("userdel: cannot remove the root account");
            if (Kernel.CurrentUser != null && u.Username == Kernel.CurrentUser.Username)
                throw new Exception("userdel: user is currently logged in");

            Users.Remove(u);
            SaveUsers();
        }

        public static void ChangePassword(string username, string newPassword)
        {
            Init();
            var u = Users.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            if (u == null) throw new Exception($"passwd: user '{username}' does not exist");
            u.PasswordHash = HashPassword(newPassword);
            SaveUsers();
        }

        public static User Find(string username) =>
            Users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
    }
}
