using System.ComponentModel;
using ModelContextProtocol.Server;
using System.Text;
/// <summary>
/// Предоставляет базовые операции файловой системы как MCP-инструменты.
/// </summary>
public class FileOperationsTools
{
    /// <summary>
    /// Создаёт один новый текст файл и записывает переданное содержимое для it.
    /// </summary>
    /// <param name="filename">full путь из файл для create.</param>
    /// <param name="content">Текст для записи.</param>
    /// <returns>статус message describing outcome.</returns>
    [McpServerTool]
    [Description("Создаёт новый файл с указанным текстовым содержимым; существующий файл никогда не перезаписывается.")]

    public string WriteFile(
        [Description("Полный путь к файлу.")] string filename,
        [Description("Текстовое содержимое для записи.")] string content
    )
    {
        try
        {
            if (File.Exists(filename))
                return "Such a file already exists!";

            File.WriteAllText(filename, content);
            return "Context write successful.";
        }
        catch (Exception ex)
        {
            return $"Error write context to file: {ex.Message}";
        }
    }
    /// <summary>
    /// Читает содержимое из один текст файл с использованием encoding detected из его BOM.
    /// </summary>
    /// <param name="filename">full путь из файл для чтения.</param>
    /// <returns>decoded файл содержимое.</returns>
    [McpServerTool]
    [Description("Читает текстовый файл по указанному пути.")]

    public string ReadFile(
    [Description("Полный путь к файлу.")] string filename)
    {
        try
        {
            Encoding encoding = Encoding.Unicode;
            using (StreamReader reader = new StreamReader(filename, true))
            {
                while (reader.Peek() >= 0)
                {
                    encoding = reader.CurrentEncoding;
                    break;
                }
                reader.Close();
            }

            return File.ReadAllText(filename, encoding);
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }
    /// <summary>
    /// Описывает назначение элемента.
    /// </summary>
    /// <param name="path">Путь к каталогу для проверки.</param>
    /// <returns>formatted список из файлы и directories, или один ошибки message.</returns>
    [McpServerTool]
    [Description("Возвращает файлы и непосредственные подкаталоги указанного каталога.")]

    public string ListFiles(
        [Description("Путь к каталогу для проверки.")] string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return $"Error: Directory '{path}' does not exist.";
            }

            var files = Directory.GetFiles(path);
            var directories = Directory.GetDirectories(path);

            var result = new List<string>();
            result.Add("Files:");
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                result.Add($"  {fileInfo.Name} ({fileInfo.Length} bytes)");
            }

            result.Add("\nDirectories:");
            foreach (var directory in directories)
            {
                var dirInfo = new DirectoryInfo(directory);
                result.Add($"  {dirInfo.Name}/");
            }

            return string.Join("\n", result);
        }
        catch (Exception ex)
        {
            return $"Error listing files: {ex.Message}";
        }
    }
}
