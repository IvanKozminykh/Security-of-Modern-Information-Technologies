using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace BackupService
{
    // Файл исходной директории, который должен попасть в резервную копию.
    internal sealed class SourceFile
    {
        public string FullPath;
        public string EntryName;        // путь внутри архива: папка1/папка2/файл.txt
        public DateTime LastWriteUtc;
        public long Length;
    }

    // Версия файла: время последнего изменения и размер.
    internal struct Stamp
    {
        public DateTime TimeUtc;
        public long Length;

        public Stamp(DateTime timeUtc, long length)
        {
            TimeUtc = timeUtc;
            Length = length;
        }
    }

    // Резервное копирование: следит за исходной директорией и поддерживает ZIP-архив в актуальном
    // состоянии — новые файлы добавляет, изменённые заменяет. Архив создаётся и обновляется
    // классом System.IO.Compression.ZipArchive (.NET Framework, входит в состав Windows).
    //
    // Изменения обнаруживаются двумя способами:
    //   - FileSystemWatcher (ReadDirectoryChangesW) сообщает об изменениях сразу;
    //   - раз в ScanInterval секунд выполняется полная проверка (на случай пропущенных уведомлений,
    //     например при переполнении буфера или обрыве сетевого соединения).
    // Каждая проверка сравнивает файлы, подходящие под маски, с содержимым архива (время изменения
    // и размер), поэтому состояние не теряется при перезапуске службы.
    internal sealed class BackupEngine : IDisposable
    {
        private const int DebounceMs = 1500;   // пауза после последнего уведомления перед проверкой
        private const int RetryMs = 3000;      // повторная проверка, если файл или архив были заняты

        private readonly Config cfg;
        private readonly FileMasks masks;
        private readonly string sourceRoot;
        private readonly string archivePath;

        private readonly ManualResetEvent stopEvent = new ManualResetEvent(false);
        private readonly AutoResetEvent changeEvent = new AutoResetEvent(false);
        private readonly object passLock = new object();   // проверка и пауза не выполняются одновременно

        private volatile bool paused;
        private volatile bool watcherFailed;
        private bool retrySoon;
        private bool sourceMissingReported;
        private FileSystemWatcher watcher;

        // Точные отметки файлов, записанных в архив этим процессом. В ZIP время хранится
        // с точностью 2 секунды, по этой таблице повторное изменение замечается и внутри этих 2 секунд.
        private readonly Dictionary<string, Stamp> written = new Dictionary<string, Stamp>(StringComparer.OrdinalIgnoreCase);

        public BackupEngine(Config cfg)
        {
            this.cfg = cfg;
            masks = new FileMasks(cfg.Masks);
            sourceRoot = Path.GetFullPath(cfg.SourceDir).TrimEnd('\\');
            archivePath = Path.GetFullPath(cfg.ArchivePath);
        }

        // Основной цикл. Возвращает управление после RequestStop().
        public void Run()
        {
            Log.Info("Исходная директория: {0}", sourceRoot);
            Log.Info("Архив: {0}", archivePath);
            Log.Info("Маски: {0}", string.Join("; ", cfg.Masks));
            Log.Info("Период полной проверки: {0} с", cfg.ScanIntervalSec);

            RunPass();
            var handles = new WaitHandle[] { stopEvent, changeEvent };
            while (true)
            {
                int timeout = retrySoon ? RetryMs : cfg.ScanIntervalSec * 1000;
                int signaled = WaitHandle.WaitAny(handles, timeout);
                if (signaled == 0)
                    break;
                if (signaled == 1)
                {
                    // Пришло уведомление об изменении: ждём, пока файлы допишутся и поток уведомлений утихнет
                    while ((signaled = WaitHandle.WaitAny(handles, DebounceMs)) == 1) { }
                    if (signaled == 0)
                        break;
                }
                RunPass();
            }

            lock (passLock)
                StopWatcher();
        }

        public void RequestStop()
        {
            stopEvent.Set();
        }

        // Приостановка: дожидается окончания текущей проверки и отключает наблюдение.
        public void Pause()
        {
            paused = true;
            lock (passLock)
            {
                if (watcher != null)
                    watcher.EnableRaisingEvents = false;
            }
            Log.Info("Резервное копирование приостановлено");
        }

        public void Resume()
        {
            paused = false;
            Log.Info("Резервное копирование возобновлено");
            changeEvent.Set();   // сразу проверить изменения, накопившиеся за время паузы
        }

        // События не закрываем: уведомление FileSystemWatcher может прийти уже после остановки
        public void Dispose()
        {
            lock (passLock)
                StopWatcher();
        }

        // ---- Одна проверка: найти новые и изменённые файлы и обновить архив ----

        private void RunPass()
        {
            lock (passLock)
            {
                if (paused)
                    return;
                retrySoon = false;
                try
                {
                    if (!Directory.Exists(sourceRoot))
                    {
                        if (!sourceMissingReported)
                            Log.Warn("Исходная директория недоступна: {0}. Жду её появления", sourceRoot);
                        sourceMissingReported = true;
                        StopWatcher();
                        return;
                    }
                    if (sourceMissingReported)
                        Log.Info("Исходная директория доступна");
                    sourceMissingReported = false;

                    EnsureWatcher();

                    Dictionary<string, Stamp> archived = ReadArchiveIndex();
                    if (archived == null)
                        return;

                    List<SourceFile> changed = ScanSource().Where(f => IsChanged(f, archived)).ToList();
                    if (changed.Count > 0)
                        UpdateArchive(changed);
                }
                catch (Exception ex)
                {
                    Log.Error("Ошибка резервного копирования: {0}", ex.Message);
                    retrySoon = true;
                }
            }
        }

        // Файлы исходной директории, подходящие под маски
        private List<SourceFile> ScanSource()
        {
            var files = new List<SourceFile>();
            Walk(sourceRoot, (info, rel) =>
            {
                if (string.Equals(info.FullName, archivePath, StringComparison.OrdinalIgnoreCase))
                    return;   // архив лежит внутри исходной директории — сам себя не копируем
                if (info.Name.StartsWith("~$"))
                    return;   // служебные файлы-блокировки Microsoft Office

                string entryName = rel.Replace('\\', '/');
                if (!masks.IsMatch(entryName))
                    return;

                files.Add(new SourceFile
                {
                    FullPath = info.FullName,
                    EntryName = entryName,
                    LastWriteUtc = info.LastWriteTimeUtc,
                    Length = info.Length
                });
            });
            return files;
        }

        // Обход дерева каталогов. onFile получает файл и его путь относительно root.
        public static void Walk(string root, Action<FileInfo, string> onFile)
        {
            var dirs = new Stack<string>();
            dirs.Push(root);
            while (dirs.Count > 0)
            {
                string dir = dirs.Pop();
                FileSystemInfo[] items;
                try
                {
                    items = new DirectoryInfo(dir).GetFileSystemInfos();
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log.Warn("Нет доступа к каталогу {0}: {1}", dir, ex.Message);
                    continue;
                }
                catch (IOException ex)
                {
                    Log.Warn("Не удалось прочитать каталог {0}: {1}", dir, ex.Message);
                    continue;
                }

                foreach (FileSystemInfo item in items)
                {
                    if ((item.Attributes & FileAttributes.Directory) != 0)
                    {
                        // Символические ссылки и точки соединения не обходим — защита от зацикливания
                        if ((item.Attributes & FileAttributes.ReparsePoint) == 0)
                            dirs.Push(item.FullName);
                    }
                    else
                    {
                        onFile((FileInfo)item, item.FullName.Substring(root.Length + 1));
                    }
                }
            }
        }

        // Содержимое архива: имя элемента -> время изменения и размер.
        // null — архив сейчас недоступен (занят другим процессом), проверка будет повторена.
        private Dictionary<string, Stamp> ReadArchiveIndex()
        {
            var index = new Dictionary<string, Stamp>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(archivePath) || new FileInfo(archivePath).Length == 0)
                return index;

            try
            {
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in zip.Entries)
                        index[entry.FullName] = new Stamp(entry.LastWriteTime.UtcDateTime, entry.Length);
                }
                return index;
            }
            catch (InvalidDataException ex)
            {
                // Архив повреждён: откладываем его в сторону и создаём новый
                string broken = archivePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Log.Error("Архив повреждён ({0}). Он переименован в {1}, будет создан новый", ex.Message, broken);
                File.Move(archivePath, broken);
                written.Clear();
                return new Dictionary<string, Stamp>(StringComparer.OrdinalIgnoreCase);
            }
            catch (IOException ex)
            {
                Log.Warn("Архив занят другим процессом, повторю позже: {0}", ex.Message);
                retrySoon = true;
                return null;
            }
        }

        private bool IsChanged(SourceFile file, Dictionary<string, Stamp> archived)
        {
            Stamp inZip;
            if (!archived.TryGetValue(file.EntryName, out inZip))
                return true;                                    // новый файл
            if (inZip.Length != file.Length)
                return true;                                    // изменился размер

            Stamp mine;
            if (written.TryGetValue(file.EntryName, out mine) && mine.Length == inZip.Length
                && Math.Abs((mine.TimeUtc - inZip.TimeUtc).TotalSeconds) < 2)
                return mine.TimeUtc != file.LastWriteUtc;       // элемент записан нами — сравниваем точно

            return Math.Abs((file.LastWriteUtc - inZip.TimeUtc).TotalSeconds) >= 2;
        }

        // Добавление новых и замена изменённых файлов в существующем архиве (или создание архива).
        private void UpdateArchive(List<SourceFile> changed)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath));

            FileStream fs;
            try
            {
                // Архив открыт только на время обновления, в остальное время его можно свободно читать
                fs = new FileStream(archivePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                Log.Warn("Архив занят другим процессом, повторю позже: {0}", ex.Message);
                retrySoon = true;
                return;
            }

            bool isNew = fs.Length == 0;
            int added = 0, replaced = 0, skipped = 0;
            ZipArchive zip;
            try
            {
                zip = new ZipArchive(fs, isNew ? ZipArchiveMode.Create : ZipArchiveMode.Update);
            }
            catch
            {
                fs.Dispose();
                throw;
            }

            using (zip)
            {
                if (isNew)
                    Log.Info("Создаётся архив {0}", archivePath);

                foreach (SourceFile file in changed)
                {
                    if (stopEvent.WaitOne(0))
                        break;   // служба останавливается: сохраняем то, что уже добавлено

                    FileStream src;
                    try
                    {
                        src = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    }
                    catch (FileNotFoundException)
                    {
                        continue;   // файл удалили после сканирования
                    }
                    catch (DirectoryNotFoundException)
                    {
                        continue;
                    }
                    catch (IOException ex)
                    {
                        Log.Warn("  файл занят, повторю позже: {0} ({1})", file.EntryName, ex.Message);
                        retrySoon = true;
                        skipped++;
                        continue;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Log.Warn("  нет доступа к файлу: {0} ({1})", file.EntryName, ex.Message);
                        skipped++;
                        continue;
                    }

                    using (src)
                    {
                        bool existed = false;
                        if (!isNew)
                        {
                            List<ZipArchiveEntry> old = zip.Entries
                                .Where(e => string.Equals(e.FullName, file.EntryName, StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            foreach (ZipArchiveEntry e in old)
                                e.Delete();
                            existed = old.Count > 0;
                        }

                        ZipArchiveEntry entry = zip.CreateEntry(file.EntryName, CompressionLevel.Optimal);
                        entry.LastWriteTime = ToZipTime(file.LastWriteUtc);
                        using (Stream dst = entry.Open())
                            src.CopyTo(dst);

                        written[file.EntryName] = new Stamp(file.LastWriteUtc, file.Length);
                        Log.Info("  {0}: {1}", existed ? "заменён " : "добавлен", file.EntryName);
                        if (existed)
                            replaced++;
                        else
                            added++;
                    }
                }
            }   // ZipArchive.Dispose записывает изменения в файл архива

            Log.Info("Архив обновлён: добавлено {0}, заменено {1}, пропущено {2}", added, replaced, skipped);
        }

        // В ZIP хранится локальное время в формате MS-DOS (1980–2107 гг.)
        private static DateTimeOffset ToZipTime(DateTime utc)
        {
            DateTime local = utc.ToLocalTime();
            if (local.Year < 1980)
                local = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local);
            if (local.Year > 2107)
                local = new DateTime(2107, 12, 31, 23, 59, 58, DateTimeKind.Local);
            return new DateTimeOffset(local);
        }

        // ---- Наблюдение за директорией ----

        private void EnsureWatcher()
        {
            if (watcherFailed)
            {
                StopWatcher();
                watcherFailed = false;
            }
            try
            {
                if (watcher != null)
                {
                    watcher.EnableRaisingEvents = true;   // после паузы
                    return;
                }

                var w = new FileSystemWatcher(sourceRoot);
                w.IncludeSubdirectories = true;
                w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite | NotifyFilters.Size;
                w.InternalBufferSize = 64 * 1024;
                w.Created += (s, e) => changeEvent.Set();
                w.Changed += (s, e) => changeEvent.Set();
                w.Renamed += (s, e) => changeEvent.Set();
                w.Error += (s, e) =>
                {
                    Log.Warn("Сбой наблюдения за директорией: {0}", e.GetException().Message);
                    watcherFailed = true;
                    changeEvent.Set();
                };
                w.EnableRaisingEvents = true;
                watcher = w;
                Log.Info("Наблюдение за изменениями в директории включено");
            }
            catch (Exception ex)
            {
                Log.Warn("Не удалось включить наблюдение за директорией ({0}), работает только периодическая проверка", ex.Message);
                StopWatcher();
            }
        }

        private void StopWatcher()
        {
            if (watcher == null)
                return;
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            watcher = null;
        }
    }
}
