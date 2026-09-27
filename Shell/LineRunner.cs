using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace StyleOS
{
    /// <summary>
    /// Runs a typed line one chain segment at a time ("a; b && c || d"), like bash.
    /// The parser expands $VARS, $? and aliases when it sees a line, so feeding it the whole
    /// line at once meant "export X=1; echo $X" printed nothing, "false; echo $?" printed 0
    /// and an alias only worked as the first word. Splitting first fixes all three.
    /// </summary>
    public static class LineRunner
    {
        public static async Task Execute(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            bool shouldRun = true;
            foreach (var (text, op) in Split(input))
            {
                if (shouldRun && !string.IsNullOrWhiteSpace(text)) await CommandRouter.ExecuteLine(text);

                shouldRun = op switch
                {
                    "&&" => ShellEnv.ExitCode == 0,
                    "||" => ShellEnv.ExitCode != 0,
                    _ => true
                };

                if (!Kernel.IsRunning || Kernel.RequestReboot || Kernel.CurrentUser == null) break;
            }
        }

        /// <summary>Splits on top-level ;, &amp;&amp; and || (quotes and backslash escapes respected; single | stays).</summary>
        public static List<(string Text, string Op)> Split(string input)
        {
            var result = new List<(string, string)>();
            var sb = new StringBuilder();
            char quote = '\0';

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (quote != '\0')
                {
                    sb.Append(c);
                    if (c == '\\' && quote == '"' && i + 1 < input.Length) { sb.Append(input[++i]); continue; }
                    if (c == quote) quote = '\0';
                    continue;
                }

                if (c == '\\' && i + 1 < input.Length) { sb.Append(c).Append(input[++i]); continue; }
                if (c == '\'' || c == '"') { quote = c; sb.Append(c); continue; }

                string op = null;
                if (c == ';') op = ";";
                else if (c == '&' && i + 1 < input.Length && input[i + 1] == '&') op = "&&";
                else if (c == '|' && i + 1 < input.Length && input[i + 1] == '|') op = "||";

                if (op == null) { sb.Append(c); continue; }

                result.Add((sb.ToString(), op));
                sb.Clear();
                i += op.Length - 1;
            }

            result.Add((sb.ToString(), ";"));
            return result;
        }
    }
}
