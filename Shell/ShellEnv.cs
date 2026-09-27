using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StyleOS
{
    /// <summary>
    /// Runtime state of the shell: variables, aliases, the piped stdin of the command that is
    /// running right now, and the exit code of the last one ($?).
    /// </summary>
    public static class ShellEnv
    {
        public static readonly Dictionary<string, string> Vars = new Dictionary<string, string>();
        public static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>();

        /// <summary>Text piped into the current command, null when there is no pipe.</summary>
        public static string StdIn { get; set; }

        public static int ExitCode { get; set; }

        public static readonly List<string> DirStack = new List<string>();
        public static string PreviousDirectory { get; set; }

        public static void InitDefaults()
        {
            Vars["HOME"] = Kernel.Home;
            Vars["USER"] = Kernel.CurrentUser?.Username ?? "root";
            Vars["SHELL"] = "/bin/sh";
            Vars["PATH"] = "/usr/local/bin:/usr/bin:/bin";
            Vars["TERM"] = "styleos-256color";
            Vars["OS"] = Kernel.DistroName;
            Vars["OSVERSION"] = Kernel.Version;
            Vars["KERNEL"] = Kernel.KernelString;
            Vars["HOSTNAME"] = HostName;
            Vars["PS1"] = @"\u@\h:\w\$ ";

            if (Aliases.Count == 0)
            {
                Aliases["ll"] = "ls -la";
                Aliases["la"] = "ls -a";
                Aliases["l"] = "ls";
                Aliases[".."] = "cd ..";
                Aliases["vi"] = "nano";
                Aliases["vim"] = "nano";
                Aliases["ff"] = "fastfetch";
            }
        }

        public static string HostName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Kernel.Config.Hostname)) return Kernel.Config.Hostname;
                try { return Environment.MachineName.ToLower(); } catch { return "styleos"; }
            }
        }

        public static string Get(string name)
        {
            if (name == "?") return ExitCode.ToString();
            if (name == "$") return Environment.ProcessId.ToString();
            if (Vars.TryGetValue(name, out string v)) return v;
            try { return Environment.GetEnvironmentVariable(name) ?? ""; } catch { return ""; }
        }

        public static void Set(string name, string value) => Vars[name] = value ?? "";

        public static void Unset(string name)
        {
            if (Vars.ContainsKey(name)) Vars.Remove(name);
        }

        /// <summary>Expands $VAR, ${VAR} and ~ inside a token.</summary>
        public static string Expand(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;

            if (token == "~") token = Kernel.Home;
            else if (token.StartsWith("~/") || token.StartsWith("~\\"))
                token = Path.Combine(Kernel.Home, token.Substring(2));

            if (token.IndexOf('$') < 0) return token;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < token.Length; i++)
            {
                if (token[i] != '$' || i == token.Length - 1) { sb.Append(token[i]); continue; }

                int j = i + 1;
                if (token[j] == '{')
                {
                    int end = token.IndexOf('}', j);
                    if (end > 0)
                    {
                        sb.Append(Get(token.Substring(j + 1, end - j - 1)));
                        i = end;
                        continue;
                    }
                }

                if (token[j] == '?' || token[j] == '$') { sb.Append(Get(token[j].ToString())); i = j; continue; }

                int k = j;
                while (k < token.Length && (char.IsLetterOrDigit(token[k]) || token[k] == '_')) k++;
                if (k == j) { sb.Append('$'); continue; }

                sb.Append(Get(token.Substring(j, k - j)));
                i = k - 1;
            }
            return sb.ToString();
        }

        /// <summary>Applies an alias to the first word of a command line, once.</summary>
        public static string ApplyAlias(string line, HashSet<string> seen)
        {
            string trimmed = line.TrimStart();
            int sp = trimmed.IndexOf(' ');
            string head = sp < 0 ? trimmed : trimmed.Substring(0, sp);
            string tail = sp < 0 ? "" : trimmed.Substring(sp);

            if (head.Length == 0 || seen.Contains(head)) return line;
            if (!Aliases.TryGetValue(head, out string replacement)) return line;

            seen.Add(head);
            return replacement + tail;
        }
    }
}
