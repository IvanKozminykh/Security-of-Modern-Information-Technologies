using System.Windows.Forms;

// Демонстрационная программа: её копии кладутся в исходную директорию, попадают в архив,
// а затем запускаются прямо из архива встроенным просмотрщиком ZIP-файлов Windows.
internal static class Hello
{
    private static void Main()
    {
        MessageBox.Show(
            "Программа запущена из резервной копии.\n\nФайл: " + Application.ExecutablePath,
            "Lab-2 demo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
