using System;
using System.Collections.Generic;
using System.IO;

namespace BackupService
{
    internal sealed class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
    }

    // Настройки службы из конфигурационного файла (INI):
    //
    //   [Backup]
    //   SourceDir    = \\server\share\source     директория, из которой выполняется резервное копирование
    //   ArchiveDir   = \\server\share\backup     директория, в которой находится архив
    //   ArchiveName  = backup.zip                имя файла-архива
    //   ScanInterval = 10                        период полной проверки директории, секунд
    //
    //   [Masks]
    //   *.docx                                   по одной маске или имени файла в строке
    //
    // Строки, начинающиеся с ';' или '#', — комментарии.
    internal sealed class Config
    {
        public string SourceDir;
        public string ArchiveDir;
        public string ArchiveName;
        public int ScanIntervalSec = 30;
        public readonly List<string> Masks = new List<string>();

        public string ArchivePath
        {
            get { return Path.Combine(ArchiveDir, ArchiveName); }
        }

        public static string DefaultPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Program.ExeBaseName + ".ini"); }
        }

        public static Config Load(string path)
        {
            if (!File.Exists(path))
                throw new ConfigException("не найден конфигурационный файл " + path);

            var cfg = new Config();
            string section = null;
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                    continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    if (section != "backup" && section != "masks")
                        throw Error(i, "неизвестная секция [" + section + "]");
                    continue;
                }

                if (section == "masks")
                {
                    cfg.Masks.Add(Unquote(line));
                    continue;
                }
                if (section != "backup")
                    throw Error(i, "параметр вне секции [Backup]");

                int eq = line.IndexOf('=');
                if (eq < 0)
                    throw Error(i, "ожидается строка вида Параметр = Значение");
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = Unquote(line.Substring(eq + 1).Trim());

                switch (key)
                {
                    case "sourcedir":   cfg.SourceDir = value; break;
                    case "archivedir":  cfg.ArchiveDir = value; break;
                    case "archivename": cfg.ArchiveName = value; break;
                    case "scaninterval":
                        if (!int.TryParse(value, out cfg.ScanIntervalSec))
                            throw Error(i, "ScanInterval должен быть целым числом секунд");
                        break;
                    default:
                        throw Error(i, "неизвестный параметр " + line.Substring(0, eq).Trim());
                }
            }

            cfg.Validate();
            return cfg;
        }

        private void Validate()
        {
            SourceDir = CheckUncDir("SourceDir", SourceDir);
            ArchiveDir = CheckUncDir("ArchiveDir", ArchiveDir);

            if (string.IsNullOrEmpty(ArchiveName))
                throw new ConfigException("не задан параметр ArchiveName");
            if (ArchiveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ConfigException("ArchiveName должен быть именем файла без пути: " + ArchiveName);
            if (!ArchiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new ConfigException("ArchiveName должен иметь расширение .zip: " + ArchiveName);

            if (Masks.Count == 0)
                throw new ConfigException("в секции [Masks] не задано ни одной маски");
            foreach (string mask in Masks)
            {
                if (mask.IndexOfAny(new[] { '"', '<', '>', '|', ':' }) >= 0)
                    throw new ConfigException("недопустимые символы в маске: " + mask);
            }

            if (ScanIntervalSec < 1 || ScanIntervalSec > 86400)
                throw new ConfigException("ScanInterval должен быть от 1 до 86400 секунд");
        }

        // Путь должен быть задан в формате UNC: \\сервер\общий_ресурс[\путь]
        private static string CheckUncDir(string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ConfigException("не задан параметр " + name);
            if (!IsUncPath(value))
                throw new ConfigException(name + " должен быть задан в формате UNC (\\\\сервер\\ресурс\\путь): " + value);
            if (value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new ConfigException(name + " содержит недопустимые символы: " + value);
            return value.TrimEnd('\\', '/');
        }

        public static bool IsUncPath(string path)
        {
            if (!path.StartsWith(@"\\") || path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\"))
                return false;
            string[] parts = path.Substring(2).Split('\\');
            return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0;
        }

        private static string Unquote(string s)
        {
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2);
            return s;
        }

        private static ConfigException Error(int lineIndex, string message)
        {
            return new ConfigException(string.Format("строка {0}: {1}", lineIndex + 1, message));
        }
    }
}
