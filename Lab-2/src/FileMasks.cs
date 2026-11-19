using System.Collections.Generic;

namespace BackupService
{
    // Набор масок файлов из конфигурации.
    // Маска без разделителя каталогов сравнивается с именем файла (*.docx, file?.txt, readme.txt),
    // маска с разделителем — с путём относительно исходной директории (folder1/*.txt).
    internal sealed class FileMasks
    {
        private readonly List<string> nameMasks = new List<string>();
        private readonly List<string> pathMasks = new List<string>();

        public FileMasks(IEnumerable<string> masks)
        {
            foreach (string mask in masks)
            {
                if (mask.IndexOf('/') >= 0 || mask.IndexOf('\\') >= 0)
                    pathMasks.Add(mask.Replace('\\', '/').TrimStart('/'));
                else
                    nameMasks.Add(mask);
            }
        }

        // relPath — путь относительно исходной директории с разделителем '/'
        public bool IsMatch(string relPath)
        {
            string name = relPath.Substring(relPath.LastIndexOf('/') + 1);
            foreach (string mask in nameMasks)
                if (Wildcard.IsMatch(name, mask))
                    return true;
            foreach (string mask in pathMasks)
                if (Wildcard.IsMatch(relPath, mask))
                    return true;
            return false;
        }
    }

    internal static class Wildcard
    {
        // Сопоставление строки с маской без учёта регистра:
        // * — любая последовательность символов (в том числе пустая), ? — ровно один любой символ.
        public static bool IsMatch(string text, string mask)
        {
            int t = 0, m = 0;
            int starM = -1, starT = 0;   // позиция последней '*' в маске и место в тексте, с которого она начала совпадать
            while (t < text.Length)
            {
                if (m < mask.Length && mask[m] == '*')
                {
                    starM = m++;
                    starT = t;
                }
                else if (m < mask.Length && (mask[m] == '?' || SameChar(mask[m], text[t])))
                {
                    t++;
                    m++;
                }
                else if (starM >= 0)
                {
                    // Несовпадение: пусть последняя '*' поглотит ещё один символ
                    m = starM + 1;
                    t = ++starT;
                }
                else
                {
                    return false;
                }
            }
            while (m < mask.Length && mask[m] == '*')
                m++;
            return m == mask.Length;
        }

        private static bool SameChar(char a, char b)
        {
            return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
        }
    }
}
