using System.ComponentModel;
using ModelContextProtocol.Server;

/// <summary>
/// Предоставляет базовые операции файловой системы как MCP-инструменты.
/// </summary>
public class FileOperationsTools
{
    /// <summary>
    /// Создаёт новый текстовый файл и записывает в него переданное содержимое.
    /// Если файл по указанному пути уже существует, инструмент отказывается от записи
    /// и не изменяет существующий файл. Для замены содержимого используйте rewrite_file.
    /// </summary>
    /// <param name="filename">Полный путь к создаваемому файлу.</param>
    /// <param name="content">Текст для записи.</param>
    /// <returns>Сообщение о статусе выполнения операции.</returns>
    [McpServerTool]
    [Description("Создаёт новый текстовый файл. Никогда не перезаписывает существующий файл: если путь уже занят, операция завершается отказом без изменения файла. Для полной замены содержимого существующего файла используйте rewrite_file.")]
    public string WriteFile(
        [Description("Полный путь к новому файлу. Если файл уже существует, он не будет изменён.")] string filename,
        [Description("Текстовое содержимое для записи в новый файл.")] string content)
    {
        try
        {
            // FileMode.CreateNew гарантирует на уровне файловой системы, что существующий
            // файл не будет перезаписан даже при одновременном создании из другого процесса.
            using var stream = new FileStream(filename, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
            return "Файл создан.";
        }
        catch (IOException) when (File.Exists(filename))
        {
            return "Файл уже существует и не был изменён. Для полной замены его содержимого используйте rewrite_file.";
        }
        catch (Exception ex)
        {
            return $"Ошибка создания файла: {ex.Message}";
        }
    }

    /// <summary>
    /// Полностью заменяет содержимое существующего текстового файла.
    /// Если файл по указанному пути отсутствует, инструмент отказывается от операции
    /// и не создаёт новый файл. Для создания нового файла используйте write_file.
    /// </summary>
    /// <param name="filename">Полный путь к существующему файлу.</param>
    /// <param name="content">Новое текстовое содержимое файла.</param>
    /// <returns>Сообщение о статусе выполнения операции.</returns>
    [McpServerTool]
    [Description("Полностью перезаписывает существующий текстовый файл новым содержимым. Не создаёт файл, если он отсутствует; для создания нового файла используйте write_file.")]
    public string RewriteFile(
        [Description("Полный путь к существующему файлу, содержимое которого нужно полностью заменить.")] string filename,
        [Description("Новое текстовое содержимое, которое полностью заменит текущее содержимое файла.")] string content)
    {
        try
        {
            // FileMode.Truncate требует существования файла и поэтому не может случайно
            // создать новый файл вместо запрошенной операции перезаписи.
            using var stream = new FileStream(filename, FileMode.Truncate, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
            return "Файл перезаписан.";
        }
        catch (FileNotFoundException)
        {
            return "Файл не существует и не был создан. Для создания нового файла используйте write_file.";
        }
        catch (DirectoryNotFoundException)
        {
            return "Каталог для указанного файла не существует. Файл не был создан.";
        }
        catch (Exception ex)
        {
            return $"Ошибка перезаписи файла: {ex.Message}";
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
