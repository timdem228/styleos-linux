using System;
using System.Diagnostics;
using System.Threading;

namespace StyleOS
{
    public static class BugReportEditor
    {
        private static readonly string[] Help = { "^S Send    ^X Cancel    Arrows Move" };

        public static void Run()
        {
            var editor = new EditorCore();
            Console.Clear();

            while (true)
            {
                editor.Draw("StyleOS Bug Reporter", "Dest: admin@timd.site", ConsoleColor.DarkRed, ConsoleColor.White, Help);

                ConsoleKeyInfo key;
                try { key = Console.ReadKey(true); }
                catch { break; }

                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.X) break;
                if (key.Key == ConsoleKey.Escape) break;

                if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.S)
                {
                    Send(editor.Text);
                    break;
                }

                editor.ProcessKey(key);
            }

            Console.Clear();
        }

        private static void Send(string reportText)
        {
            Console.Clear();
            Console.WriteLine("Формирование отчёта об ошибке...");

            if (string.IsNullOrWhiteSpace(reportText))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Отчёт пуст, отправка отменена.");
                Console.ResetColor();
                Thread.Sleep(1200);
                return;
            }

            try
            {
                string logs = string.Join("\n", SystemLogger.Tail(40));
                string subject = Uri.EscapeDataString($"{Kernel.DistroName} Bug Report");
                string body = Uri.EscapeDataString(
                    $"Версия: {Kernel.Version}\n\nUser Description:\n{reportText}\n\nSystem Logs:\n{logs}");
                string url = $"https://mail.google.com/mail/?view=cm&fs=1&to=admin@timd.site&su={subject}&body={body}";

                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Браузер с подготовленным письмом открыт. Нажмите 'Отправить' в почте.");
                SystemLogger.Log("BUGREPORT", "Draft opened in browser");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Ошибка при открытии браузера: {ex.Message}");
                Console.WriteLine("Можно написать напрямую на admin@timd.site.");
            }

            Console.ResetColor();
            Thread.Sleep(2000);
        }
    }
}
