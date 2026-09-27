using System;
using System.Collections.Generic;
using System.IO;

namespace StyleOS
{
    public static class NanoEditor
    {
        private static readonly string[] Help =
        {
            "^S Save   ^X Exit   ^W Where Is   ^G Go To Line",
            "^K Cut    ^U Paste  Arrows Move   Tab Indent"
        };

        public static void Run(List<string> args)
        {
            var operands = Io.Operands(args);
            if (operands.Count == 0) { Io.Error("nano", "missing filename"); return; }

            string filePath = PathUtil.Resolve(operands[0]);
            var editor = new EditorCore();

            if (File.Exists(filePath))
            {
                try { editor.Load(File.ReadAllLines(filePath)); }
                catch (Exception ex) { Io.Error("nano", ex.Message); return; }
            }
            else if (Directory.Exists(filePath))
            {
                Io.Error("nano", $"\"{operands[0]}\" is a directory");
                return;
            }

            Console.Clear();

            while (true)
            {
                editor.Draw("GNU nano 9.0", $"File: {Path.GetFileName(filePath)}",
                    ConsoleColor.Gray, ConsoleColor.Black, Help);

                ConsoleKeyInfo key;
                try { key = Console.ReadKey(true); }
                catch { break; }

                if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.X:
                            if (!editor.Modified) return;
                            switch (ConfirmExit(editor, filePath))
                            {
                                case ExitChoice.Cancel: continue;
                                case ExitChoice.SaveAndExit: SaveFile(editor, filePath); return;
                                case ExitChoice.DiscardAndExit: return;
                            }
                            continue;

                        case ConsoleKey.S:
                            SaveFile(editor, filePath);
                            continue;

                        case ConsoleKey.K:
                            editor.CutLine();
                            continue;

                        case ConsoleKey.U:
                            editor.PasteLine();
                            continue;

                        case ConsoleKey.W:
                            {
                                string term = editor.Prompt("Search: ", ConsoleColor.Gray, ConsoleColor.Black);
                                if (term != null) editor.Search(term);
                                continue;
                            }

                        case ConsoleKey.G:
                            {
                                string raw = editor.Prompt("Go to line: ", ConsoleColor.Gray, ConsoleColor.Black);
                                if (raw != null && int.TryParse(raw.Trim(), out int line))
                                    editor.GoTo(line - 1);
                                else if (raw != null)
                                    editor.Message = "  [ Invalid line number ]";
                                continue;
                            }
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    if (!editor.Modified) return;
                    switch (ConfirmExit(editor, filePath))
                    {
                        case ExitChoice.Cancel: continue;
                        case ExitChoice.SaveAndExit: SaveFile(editor, filePath); return;
                        case ExitChoice.DiscardAndExit: return;
                    }
                    continue;
                }

                editor.ProcessKey(key);
            }

            Console.Clear();
        }

        private enum ExitChoice { Cancel, SaveAndExit, DiscardAndExit }

        private static ExitChoice ConfirmExit(EditorCore editor, string path)
        {
            Console.BackgroundColor = ConsoleColor.Gray;
            Console.ForegroundColor = ConsoleColor.Black;
            ConsoleHost.WriteAt(0, ConsoleHost.Height - 3,
                ConsoleHost.FullLine($"Save modified buffer to {Path.GetFileName(path)}? (Y)es / (N)o / (C)ancel"));
            Console.ResetColor();

            while (true)
            {
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Y) return ExitChoice.SaveAndExit;
                if (key == ConsoleKey.N) return ExitChoice.DiscardAndExit;
                if (key == ConsoleKey.C || key == ConsoleKey.Escape) return ExitChoice.Cancel;
            }
        }

        private static void SaveFile(EditorCore editor, string path)
        {
            try
            {
                File.WriteAllLines(path, editor.Lines);
                editor.Modified = false;
                editor.Message = $"  [ Wrote {editor.Lines.Count} lines ]";
            }
            catch (Exception ex)
            {
                editor.Message = $"  [ Error writing file: {ex.Message} ]";
            }
        }
    }
}
