using System;
using System.IO;
using System.Reflection;

namespace BackupService
{
    internal static class Program
    {
        public const string ServiceName = "Lab2Backup";
        public const string DisplayName = "Lab-2 Backup Service";
        public const string Description =
            "Резервное копирование файлов из директории в ZIP-архив (лабораторная работа 2). " +
            "Настройки: BackupService.ini рядом с исполняемым файлом.";

        // Имя exe без расширения: рядом с ним лежат BackupService.ini и BackupService.log
        public static readonly string ExeBaseName =
            Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);

        private static int Main(string[] args)
        {
            // Без аргументов процесс запускает SCM — переходим в режим службы
            if (args.Length == 0)
            {
                if (ServiceHost.RunAsService())
                    return 0;
                PrintUsage();
                return 1;
            }

            switch (args[0].TrimStart('-', '/').ToLowerInvariant())
            {
                case "install":   return ServiceControl.Install();
                case "remove":
                case "uninstall": return ServiceControl.Remove();
                case "start":     return ServiceControl.Start();
                case "stop":      return ServiceControl.Stop();
                case "restart":   return ServiceControl.Restart();
                case "status":    return ServiceControl.Status();
                case "check":     return CheckConfig();
                case "run":       return RunInConsole();
                case "help":
                case "?":
                case "h":
                    PrintUsage();
                    return 0;
                default:
                    Console.WriteLine("Неизвестная команда: {0}", args[0]);
                    Console.WriteLine();
                    PrintUsage();
                    return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Служба резервного копирования в ZIP-архив (лабораторная работа 2)");
            Console.WriteLine();
            Console.WriteLine("Использование: {0}.exe <команда>", ExeBaseName);
            Console.WriteLine();
            Console.WriteLine("  install   установить службу {0}", ServiceName);
            Console.WriteLine("  remove    удалить службу (работающая служба сначала останавливается)");
            Console.WriteLine("  start     запустить службу");
            Console.WriteLine("  stop      остановить службу");
            Console.WriteLine("  restart   перезапустить службу");
            Console.WriteLine("  status    показать состояние службы");
            Console.WriteLine("  check     проверить конфигурацию и показать, какие файлы попадут в архив");
            Console.WriteLine("  run       выполнять резервное копирование в консоли, без службы (Ctrl+C - выход)");
            Console.WriteLine();
            Console.WriteLine("install, remove, start, stop, restart требуют прав администратора.");
            Console.WriteLine();
            Console.WriteLine("Конфигурация: {0}", Config.DefaultPath);
            Console.WriteLine("Журнал:       {0}", Log.FilePath);
        }

        // Расшифровка кода завершения службы (dwServiceSpecificExitCode)
        public static string ExitCodeText(int code)
        {
            switch (code)
            {
                case 1:  return "ошибка в конфигурационном файле";
                case 2:  return "непредвиденная ошибка";
                default: return "неизвестный код";
            }
        }

        // Проверка конфигурации: настройки и список файлов, которые попадут / не попадут в архив
        private static int CheckConfig()
        {
            Config cfg = LoadConfig();
            if (cfg == null)
                return 1;

            Console.WriteLine("Конфигурация: {0}", Config.DefaultPath);
            Console.WriteLine("  Исходная директория: {0}", cfg.SourceDir);
            Console.WriteLine("  Архив:               {0}", cfg.ArchivePath);
            Console.WriteLine("  Период проверки:     {0} с", cfg.ScanIntervalSec);
            Console.WriteLine("  Маски:               {0}", string.Join("; ", cfg.Masks));
            Console.WriteLine();

            if (!Directory.Exists(cfg.SourceDir))
            {
                Console.WriteLine("Исходная директория недоступна: {0}", cfg.SourceDir);
                return 1;
            }

            var masks = new FileMasks(cfg.Masks);
            int included = 0, excluded = 0;
            Console.WriteLine("Файлы исходной директории ([+] попадает в архив, [-] не попадает):");
            BackupEngine.Walk(cfg.SourceDir.TrimEnd('\\'), (info, rel) =>
            {
                bool match = masks.IsMatch(rel.Replace('\\', '/'));
                Console.WriteLine("  [{0}] {1}", match ? "+" : "-", rel);
                if (match)
                    included++;
                else
                    excluded++;
            });
            Console.WriteLine();
            Console.WriteLine("Попадают в архив: {0}, не попадают: {1}", included, excluded);
            return 0;
        }

        // Отладочный режим: та же логика резервного копирования, но в консоли текущего пользователя
        private static int RunInConsole()
        {
            Log.EchoToConsole = true;
            Config cfg = LoadConfig();
            if (cfg == null)
                return 1;

            var engine = new BackupEngine(cfg);
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Log.Info("Ctrl+C: остановка");
                engine.RequestStop();
            };
            Log.Info("===== Консольный режим, Ctrl+C - выход =====");
            engine.Run();
            engine.Dispose();
            Log.Info("===== Консольный режим завершён =====");
            return 0;
        }

        private static Config LoadConfig()
        {
            try
            {
                return Config.Load(Config.DefaultPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка конфигурации: {0}", ex.Message);
                return null;
            }
        }
    }
}
