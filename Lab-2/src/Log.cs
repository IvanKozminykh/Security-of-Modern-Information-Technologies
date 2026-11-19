using System;
using System.IO;
using System.Text;

namespace BackupService
{
    // Потокобезопасный журнал работы службы. Пишется в файл рядом с exe,
    // в консольном режиме (команда run) дублируется на экран.
    internal static class Log
    {
        private static readonly object Sync = new object();

        public static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Program.ExeBaseName + ".log");

        public static bool EchoToConsole;

        public static void Info(string format, params object[] args)  { Write("INFO ", format, args); }
        public static void Warn(string format, params object[] args)  { Write("WARN ", format, args); }
        public static void Error(string format, params object[] args) { Write("ERROR", format, args); }

        private static void Write(string level, string format, object[] args)
        {
            string message = args.Length > 0 ? string.Format(format, args) : format;
            string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}", DateTime.Now, level, message);
            lock (Sync)
            {
                try
                {
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception)
                {
                    // Журнал недоступен — работу службы из-за этого не прерываем
                }
                if (EchoToConsole)
                    Console.WriteLine(line);
            }
        }
    }
}
