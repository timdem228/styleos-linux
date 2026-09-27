using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace StyleOS
{
    /// <summary>
    /// Input line editor. Replaces the old CustomReadLine, which guessed the prompt width
    /// (off by one), left garbage behind when the line got shorter and threw
    /// ArgumentOutOfRangeException as soon as the text wrapped to a second row.
    /// </summary>
    public static class LineEditor
    {
        public static List<string> History = new List<string>();
        private static int _startLeft, _startTop, _lastLength;

        public static string ReadLine(Action drawPrompt)
        {
            var buffer = new StringBuilder();
            int cursor = 0;
            int historyIndex = History.Count;
            string stash = "";

            drawPrompt();
            try { _startLeft = Console.CursorLeft; _startTop = Console.CursorTop; }
            catch { _startLeft = 0; _startTop = 0; }
            _lastLength = 0;

            while (true)
            {
                ConsoleKeyInfo key;
                try { key = Console.ReadKey(true); }
                catch { return Console.ReadLine(); }

                bool ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);

                if (key.Key == ConsoleKey.Enter)
                {
                    MoveToEnd(buffer.Length);
                    Console.WriteLine();
                    return buffer.ToString();
                }

                if (ctrl && key.Key == ConsoleKey.C)
                {
                    MoveToEnd(buffer.Length);
                    Console.WriteLine("^C");
                    ShellEnv.ExitCode = 130;
                    return "";
                }

                if (ctrl && key.Key == ConsoleKey.D)
                {
                    if (buffer.Length == 0)
                    {
                        MoveToEnd(0);
                        Console.WriteLine("exit");
                        return "exit";
                    }
                    if (cursor < buffer.Length) { buffer.Remove(cursor, 1); Render(buffer.ToString(), cursor); }
                    continue;
                }

                if (ctrl && key.Key == ConsoleKey.L)
                {
                    Console.Clear();
                    drawPrompt();
                    try { _startLeft = Console.CursorLeft; _startTop = Console.CursorTop; } catch { }
                    _lastLength = 0;
                    Render(buffer.ToString(), cursor);
                    continue;
                }

                if (ctrl && key.Key == ConsoleKey.U)
                {
                    buffer.Remove(0, cursor);
                    cursor = 0;
                    Render(buffer.ToString(), cursor);
                    continue;
                }

                if (ctrl && key.Key == ConsoleKey.K)
                {
                    buffer.Remove(cursor, buffer.Length - cursor);
                    Render(buffer.ToString(), cursor);
                    continue;
                }

                if (ctrl && key.Key == ConsoleKey.W)
                {
                    int start = cursor;
                    while (start > 0 && char.IsWhiteSpace(buffer[start - 1])) start--;
                    while (start > 0 && !char.IsWhiteSpace(buffer[start - 1])) start--;
                    buffer.Remove(start, cursor - start);
                    cursor = start;
                    Render(buffer.ToString(), cursor);
                    continue;
                }

                switch (key.Key)
                {
                    case ConsoleKey.Backspace:
                        if (cursor > 0) { buffer.Remove(cursor - 1, 1); cursor--; Render(buffer.ToString(), cursor); }
                        continue;

                    case ConsoleKey.Delete:
                        if (cursor < buffer.Length) { buffer.Remove(cursor, 1); Render(buffer.ToString(), cursor); }
                        continue;

                    case ConsoleKey.LeftArrow:
                        if (cursor > 0) { cursor--; PlaceCursor(cursor); }
                        continue;

                    case ConsoleKey.RightArrow:
                        if (cursor < buffer.Length) { cursor++; PlaceCursor(cursor); }
                        continue;

                    case ConsoleKey.Home:
                        cursor = 0; PlaceCursor(cursor); continue;

                    case ConsoleKey.End:
                        cursor = buffer.Length; PlaceCursor(cursor); continue;

                    case ConsoleKey.UpArrow:
                        if (History.Count == 0) continue;
                        if (historyIndex == History.Count) stash = buffer.ToString();
                        if (historyIndex > 0)
                        {
                            historyIndex--;
                            buffer.Clear(); buffer.Append(History[historyIndex]);
                            cursor = buffer.Length;
                            Render(buffer.ToString(), cursor);
                        }
                        continue;

                    case ConsoleKey.DownArrow:
                        if (History.Count == 0) continue;
                        if (historyIndex < History.Count - 1)
                        {
                            historyIndex++;
                            buffer.Clear(); buffer.Append(History[historyIndex]);
                        }
                        else
                        {
                            historyIndex = History.Count;
                            buffer.Clear(); buffer.Append(stash);
                        }
                        cursor = buffer.Length;
                        Render(buffer.ToString(), cursor);
                        continue;

                    case ConsoleKey.Tab:
                        {
                            string completed = Complete(buffer.ToString(), cursor, out int newCursor);
                            if (completed != null)
                            {
                                buffer.Clear(); buffer.Append(completed);
                                cursor = newCursor;
                                Render(buffer.ToString(), cursor);
                            }
                            continue;
                        }
                }

                if (!char.IsControl(key.KeyChar))
                {
                    buffer.Insert(cursor, key.KeyChar);
                    cursor++;
                    Render(buffer.ToString(), cursor);
                }
            }
        }

        private static int Width => ConsoleHost.Width;

        private static void Render(string text, int cursor)
        {
            try
            {
                Console.SetCursorPosition(_startLeft, _startTop);
                Console.Write(text);

                if (_lastLength > text.Length)
                    Console.Write(new string(' ', Math.Min(_lastLength - text.Length, Width)));

                // Writing past the bottom row scrolls the screen, so the anchor moves up.
                int expectedRow = _startTop + (_startLeft + text.Length) / Width;
                int actualRow = Console.CursorTop;
                if (actualRow < expectedRow) _startTop -= (expectedRow - actualRow);

                _lastLength = text.Length;
                PlaceCursor(cursor);
            }
            catch { }
        }

        private static void PlaceCursor(int cursor)
        {
            try
            {
                int absolute = _startLeft + cursor;
                int row = _startTop + absolute / Width;
                int col = absolute % Width;
                if (row >= ConsoleHost.Height) row = ConsoleHost.Height - 1;
                Console.SetCursorPosition(col, Math.Max(0, row));
            }
            catch { }
        }

        private static void MoveToEnd(int length) => PlaceCursor(length);

        /// <summary>Tab completion: command names for the first word, paths for the rest.</summary>
        private static string Complete(string text, int cursor, out int newCursor)
        {
            newCursor = cursor;
            string head = text.Substring(0, cursor);
            int start = head.LastIndexOf(' ') + 1;
            string word = head.Substring(start);
            bool isCommand = start == 0;

            List<string> matches;
            if (isCommand)
            {
                matches = CommandRouter.CommandNames
                    .Where(c => c.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c).ToList();
            }
            else
            {
                matches = CompletePath(word);
            }

            if (matches.Count == 0) return null;

            string completion;
            if (matches.Count == 1)
            {
                completion = matches[0] + (isCommand || matches[0].EndsWith("/") ? (isCommand ? " " : "") : " ");
            }
            else
            {
                completion = LongestCommonPrefix(matches);
                if (completion.Length <= word.Length)
                {
                    Console.WriteLine();
                    Console.WriteLine(string.Join("  ", matches.Take(60)));
                    return null;
                }
            }

            string result = text.Substring(0, start) + completion + text.Substring(cursor);
            newCursor = start + completion.Length;
            return result;
        }

        private static List<string> CompletePath(string word)
        {
            var result = new List<string>();
            try
            {
                string expanded = ShellEnv.Expand(word);
                string dir, prefix;

                if (expanded.EndsWith("/") || expanded.EndsWith("\\"))
                {
                    dir = PathUtil.Resolve(expanded);
                    prefix = "";
                }
                else
                {
                    string parent = Path.GetDirectoryName(expanded);
                    dir = string.IsNullOrEmpty(parent) ? Kernel.CurrentDirectory : PathUtil.Resolve(parent);
                    prefix = Path.GetFileName(expanded);
                }

                if (!Directory.Exists(dir)) return result;

                string wordDir = word.Contains('/') || word.Contains('\\')
                    ? word.Substring(0, word.LastIndexOf(word.Contains('/') ? '/' : '\\') + 1)
                    : "";

                foreach (var d in new DirectoryInfo(dir).GetDirectories())
                    if (d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) result.Add(wordDir + d.Name + "/");

                foreach (var f in new DirectoryInfo(dir).GetFiles())
                    if (f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) result.Add(wordDir + f.Name);
            }
            catch { }
            return result;
        }

        private static string LongestCommonPrefix(List<string> items)
        {
            string first = items[0];
            int len = first.Length;
            foreach (var s in items)
            {
                int i = 0;
                while (i < len && i < s.Length && char.ToLowerInvariant(s[i]) == char.ToLowerInvariant(first[i])) i++;
                len = i;
            }
            return first.Substring(0, len);
        }

        public static void LoadHistory()
        {
            try
            {
                if (File.Exists(Kernel.HistoryFile))
                    History = File.ReadAllLines(Kernel.HistoryFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            }
            catch { History = new List<string>(); }

            if (History.Count > 1000) History = History.Skip(History.Count - 1000).ToList();
        }

        public static void AddHistory(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return;
            if (History.Count > 0 && History[History.Count - 1] == cmd) return;

            History.Add(cmd);
            if (History.Count > 1000) History.RemoveAt(0);

            try { File.AppendAllText(Kernel.HistoryFile, cmd + Environment.NewLine); } catch { }
        }

        public static void ClearHistory()
        {
            History.Clear();
            try { if (File.Exists(Kernel.HistoryFile)) File.WriteAllText(Kernel.HistoryFile, ""); } catch { }
        }
    }
}
