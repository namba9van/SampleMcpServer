using System.ComponentModel;
using ModelContextProtocol.Server;

/// <summary>
/// Предоставляет базовые операции файловой системы как MCP-инструменты.
/// </summary>
public class FileOperationsTools
{
    /// <summary>
    /// Создаёт текстовый файл и записывает в него переданное содержимое. По умолчанию не
    /// перезаписывает уже существующий файл — для этого нужно явно передать overwrite=true.
    /// </summary>
    /// <param name="filename">Полный путь к файлу.</param>
    /// <param name="content">Текст для записи.</param>
    /// <param name="overwrite">true — заменить содержимое существующего файла. По умолчанию false.</param>
    /// <returns>Сообщение о статусе выполнения операции.</returns>
    [McpServerTool]
    [Description("Создаёт файл с указанным текстовым содержимым. По умолчанию НЕ перезаписывает уже существующий файл — вызов с overwrite=false (по умолчанию) на существующем файле вернёт отказ. Чтобы заменить содержимое существующего файла, повторите вызов с overwrite=true.")]
    public string WriteFile(
        [Description("Полный путь к файлу.")] string filename,
        [Description("Текстовое содержимое для записи.")] string content,
        [Description("true — перезаписать файл, если он уже существует. По умолчанию false: существующий файл не изменяется.")] bool overwrite = false)
    {
        try
        {
            var exists = File.Exists(filename);
            if (exists && !overwrite)
                return "Файл уже существует и не был изменён. Чтобы перезаписать его содержимое, повторите вызов с overwrite=true.";

            File.WriteAllText(filename, content);
            return exists ? "Файл перезаписан." : "Файл создан.";
        }
        catch (Exception ex)
        {
            return $"Ошибка записи файла: {ex.Message}";
        }
    }

    /// <summary>
    /// Читает содержимое текстового файла. Кодировка определяется автоматически по BOM файла
    /// (аналогично поведению File.ReadAllText без явно указанной кодировки), UTF-8 используется
    /// как запасной вариант при отсутствии BOM.
    /// </summary>
    /// <param name="filename">Полный путь к читаемому файлу.</param>
    /// <returns>Декодированное содержимое файла.</returns>
    [McpServerTool]
    [Description("Читает текстовый файл по указанному пути.")]
    public string ReadFile(
        [Description("Полный путь к файлу.")] string filename)
    {
        try
        {
            return File.ReadAllText(filename);
        }
        catch (Exception ex)
        {
            return $"Ошибка чтения файла: {ex.Message}";
        }
    }

    /// <summary>
    /// Перечисляет файлы и непосредственные подкаталоги указанного каталога.
    /// </summary>
    /// <param name="path">Путь к каталогу для проверки.</param>
    /// <returns>Отформатированный список файлов и подкаталогов либо сообщение об ошибке.</returns>
    [McpServerTool]
    [Description("Возвращает файлы и непосредственные подкаталоги указанного каталога.")]
    public string ListFiles(
        [Description("Путь к каталогу для проверки.")] string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return $"Ошибка: каталог '{path}' не существует.";

            var files = Directory.GetFiles(path);
            var directories = Directory.GetDirectories(path);

            var result = new List<string> { "Файлы:" };
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                result.Add($"  {fileInfo.Name} ({fileInfo.Length} байт)");
            }

            result.Add("\nПодкаталоги:");
            foreach (var directory in directories)
            {
                var dirInfo = new DirectoryInfo(directory);
                result.Add($"  {dirInfo.Name}/");
            }

            return string.Join("\n", result);
        }
        catch (Exception ex)
        {
            return $"Ошибка получения списка файлов: {ex.Message}";
        }
    }
}
