using System;
using System.Collections.Generic;
using System.Text;

namespace StyleOS
{
    /// <summary>
    /// Shared buffer and screen handling for the full screen editors.
    /// All cursor moves go through ConsoleHost, so a small window can no longer throw
    /// ArgumentOutOfRangeException in the middle of typing.
    /// </summary>
    public class EditorCore
    {
        public List<string> Lines = new List<string> { "" };
        public int Cx, Cy, Offset, ColOffset;
        public string Message = "";
        public bool Modified;
        public string Clipboard = "";

        public int ViewHeight => Math.Max(1, ConsoleHost.Height - 4);
        public int ViewWidth => Math.Max(10, ConsoleHost.Width - 1);
        public int CurrentIndex => Math.Min(Offset + Cy, Math.Max(0, Lines.Count - 1));
        public string CurrentLine => Lines[CurrentIndex];

        public void Load(IEnumerable<string> lines)
        {
            Lines = new List<string>(lines);
            if (Lines.Count == 0) Lines.Add("");
        }

        public string Text => string.Join("\n", Lines);

        public void Draw(string leftTitle, string rightTitle, ConsoleColor barBg, ConsoleColor barFg, string[] help)
        {
            ConsoleHost.SetCursorVisible(false);

            Console.BackgroundColor = barBg;
            Console.ForegroundColor = barFg;
            string header = "  " + leftTitle;
            header = header.PadRight(Math.Max(0, ConsoleHost.Width / 2 - header.Length / 2)) + rightTitle;
            ConsoleHost.WriteAt(0, 0, ConsoleHost.FullLine(header));
            Console.ResetColor();
            ConfigureTextColors();

            for (int i = 0; i < ViewHeight; i++)
            {
                int index = Offset + i;
                string line = "";

                if (index < Lines.Count)
                {
                    string raw = Lines[index];
                    line = ColOffset < raw.Length ? raw.Substring(ColOffset) : "";
                    if (line.Length > ViewWidth) line = line.Substring(0, ViewWidth);
                }

                ConsoleHost.WriteAt(0, i + 1, ConsoleHost.FullLine(line));
            }

            Console.BackgroundColor = barBg;
            Console.ForegroundColor = barFg;
            ConsoleHost.WriteAt(0, ConsoleHost.Height - 3, ConsoleHost.FullLine(
                string.IsNullOrEmpty(Message)
                    ? $"  line {CurrentIndex + 1}/{Lines.Count}  col {Cx + 1}{(Modified ? "   [ Modified ]" : "")}"
                    : Message));
            Message = "";

            for (int i = 0; i < help.Length && i < 2; i++)
                ConsoleHost.WriteAt(0, ConsoleHost.Height - 2 + i, ConsoleHost.FullLine(help[i]));

            Console.ResetColor();
            ConfigureTextColors();

            ConsoleHost.SetCursor(Math.Max(0, Cx - ColOffset), Math.Min(Cy + 1, ConsoleHost.Height - 4));
            ConsoleHost.SetCursorVisible(true);
        }

        private static void ConfigureTextColors()
        {
            try
            {
                Console.ForegroundColor = Kernel.Config.DefaultTextColor;
                Console.BackgroundColor = Kernel.Config.DefaultBgColor;
            }
            catch { }
        }

        public void ProcessKey(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (Cy > 0) Cy--;
                    else if (Offset > 0) Offset--;
                    ClampColumn();
                    break;

                case ConsoleKey.DownArrow:
                    if (Offset + Cy < Lines.Count - 1)
                    {
                        if (Cy < ViewHeight - 1) Cy++;
                        else Offset++;
                    }
                    ClampColumn();
                    break;

                case ConsoleKey.LeftArrow:
                    if (Cx > 0) Cx--;
                    else if (CurrentIndex > 0)
                    {
                        MoveUpOneLine();
                        Cx = CurrentLine.Length;
                    }
                    SyncColumnOffset();
                    break;

                case ConsoleKey.RightArrow:
                    if (Cx < CurrentLine.Length) Cx++;
                    else if (CurrentIndex < Lines.Count - 1)
                    {
                        MoveDownOneLine();
                        Cx = 0;
                    }
                    SyncColumnOffset();
                    break;

                case ConsoleKey.Home:
                    Cx = 0; ColOffset = 0; break;

                case ConsoleKey.End:
                    Cx = CurrentLine.Length; SyncColumnOffset(); break;

                case ConsoleKey.PageUp:
                    Offset = Math.Max(0, Offset - ViewHeight);
                    ClampColumn();
                    break;

                case ConsoleKey.PageDown:
                    Offset = Math.Min(Math.Max(0, Lines.Count - 1), Offset + ViewHeight);
                    if (Offset + Cy >= Lines.Count) Cy = Math.Max(0, Lines.Count - Offset - 1);
                    ClampColumn();
                    break;

                case ConsoleKey.Enter:
                    {
                        int index = CurrentIndex;
                        string line = Lines[index];
                        Cx = Math.Min(Cx, line.Length);

                        string rest = line.Substring(Cx);
                        Lines[index] = line.Substring(0, Cx);
                        Lines.Insert(index + 1, rest);

                        Cx = 0;
                        ColOffset = 0;
                        if (Cy < ViewHeight - 1) Cy++; else Offset++;
                        Modified = true;
                        break;
                    }

                case ConsoleKey.Backspace:
                    {
                        int index = CurrentIndex;
                        if (Cx > 0)
                        {
                            Lines[index] = Lines[index].Remove(Cx - 1, 1);
                            Cx--;
                        }
                        else if (index > 0)
                        {
                            int previousLength = Lines[index - 1].Length;
                            Lines[index - 1] += Lines[index];
                            Lines.RemoveAt(index);
                            MoveUpOneLine();
                            Cx = previousLength;
                        }
                        Modified = true;
                        SyncColumnOffset();
                        break;
                    }

                case ConsoleKey.Delete:
                    {
                        int index = CurrentIndex;
                        if (Cx < Lines[index].Length) Lines[index] = Lines[index].Remove(Cx, 1);
                        else if (index < Lines.Count - 1)
                        {
                            Lines[index] += Lines[index + 1];
                            Lines.RemoveAt(index + 1);
                        }
                        Modified = true;
                        break;
                    }

                case ConsoleKey.Tab:
                    InsertText("    ");
                    break;

                default:
                    if (!char.IsControl(key.KeyChar)) InsertText(key.KeyChar.ToString());
                    break;
            }
        }

        private void InsertText(string text)
        {
            int index = CurrentIndex;
            Cx = Math.Min(Cx, Lines[index].Length);
            Lines[index] = Lines[index].Insert(Cx, text);
            Cx += text.Length;
            Modified = true;
            SyncColumnOffset();
        }

        private void MoveUpOneLine()
        {
            if (Cy > 0) Cy--;
            else if (Offset > 0) Offset--;
        }

        private void MoveDownOneLine()
        {
            if (Cy < ViewHeight - 1) Cy++;
            else Offset++;
        }

        private void ClampColumn()
        {
            Cx = Math.Min(Cx, CurrentLine.Length);
            SyncColumnOffset();
        }

        private void SyncColumnOffset()
        {
            if (Cx < ColOffset) ColOffset = Cx;
            else if (Cx >= ColOffset + ViewWidth) ColOffset = Cx - ViewWidth + 1;
            if (ColOffset < 0) ColOffset = 0;
        }

        public void CutLine()
        {
            int index = CurrentIndex;
            Clipboard = Lines[index];
            Lines.RemoveAt(index);
            if (Lines.Count == 0) Lines.Add("");
            if (Offset + Cy >= Lines.Count)
            {
                if (Cy > 0) Cy--;
                else if (Offset > 0) Offset--;
            }
            Cx = 0;
            ColOffset = 0;
            Modified = true;
        }

        public void PasteLine()
        {
            Lines.Insert(CurrentIndex, Clipboard ?? "");
            MoveDownOneLine();
            Modified = true;
        }

        /// <summary>Bottom-line prompt. Returns null when the user presses Escape.</summary>
        public string Prompt(string label, ConsoleColor barBg, ConsoleColor barFg)
        {
            Console.BackgroundColor = barBg;
            Console.ForegroundColor = barFg;
            ConsoleHost.WriteAt(0, ConsoleHost.Height - 3, ConsoleHost.FullLine(label));

            var input = new StringBuilder();
            while (true)
            {
                ConsoleHost.SetCursor(Math.Min(label.Length + input.Length, ConsoleHost.Width - 2), ConsoleHost.Height - 3);
                ConsoleHost.SetCursorVisible(true);

                var key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Escape) { Console.ResetColor(); return null; }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (input.Length > 0)
                    {
                        input.Remove(input.Length - 1, 1);
                        ConsoleHost.WriteAt(0, ConsoleHost.Height - 3, ConsoleHost.FullLine(label + input));
                    }
                    continue;
                }

                if (char.IsControl(key.KeyChar)) continue;

                input.Append(key.KeyChar);
                ConsoleHost.WriteAt(0, ConsoleHost.Height - 3, ConsoleHost.FullLine(label + input));
            }

            Console.ResetColor();
            return input.ToString();
        }

        public void Search(string term)
        {
            if (string.IsNullOrEmpty(term)) { Message = "  [ Empty search ]"; return; }

            for (int i = CurrentIndex + 1; i < Lines.Count + CurrentIndex + 1; i++)
            {
                int index = i % Lines.Count;
                if (Lines[index].IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0) continue;

                GoTo(index);
                Cx = Lines[index].IndexOf(term, StringComparison.OrdinalIgnoreCase);
                SyncColumnOffset();
                Message = $"  [ Found on line {index + 1} ]";
                return;
            }
            Message = $"  [ '{term}' not found ]";
        }

        public void GoTo(int lineIndex)
        {
            lineIndex = Math.Max(0, Math.Min(lineIndex, Lines.Count - 1));

            if (lineIndex < Offset || lineIndex >= Offset + ViewHeight)
            {
                Offset = Math.Max(0, lineIndex - ViewHeight / 2);
                Cy = lineIndex - Offset;
            }
            else Cy = lineIndex - Offset;

            Cx = 0;
            ColOffset = 0;
        }
    }
}
