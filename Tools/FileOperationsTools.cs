using System.ComponentModel;
using ModelContextProtocol.Server;
using System.Text;
/// <summary>
/// Provides basic file-system operations exposed as MCP tools.
/// </summary>
public class FileOperationsTools
{
    /// <summary>
    /// Creates a new text file and writes the supplied content to it.
    /// </summary>
    /// <param name="filename">The full path of the file to create.</param>
    /// <param name="content">The text to write.</param>
    /// <returns>A status message describing the outcome.</returns>
    [McpServerTool]
    [Description("Creates a new file with the supplied text content; an existing file is never overwritten.")]

    public string WriteFile(
        [Description("The full path of the file.")] string filename,
        [Description("The text content to write.")] string content
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
    /// Reads the contents of a text file using the encoding detected from its BOM.
    /// </summary>
    /// <param name="filename">The full path of the file to read.</param>
    /// <returns>The decoded file contents.</returns>
    [McpServerTool]
    [Description("Reads a text file from the specified path.")]

    public string ReadFile(
    [Description("The full path of the file.")] string filename)
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
    /// <summary>
    /// Lists files and immediate subdirectories in a directory.
    /// </summary>
    /// <param name="path">The path of the directory to inspect.</param>
    /// <returns>A formatted list of files and directories, or an error message.</returns>
    [McpServerTool]
    [Description("Lists files and immediate subdirectories in the specified directory.")]

    public string ListFiles(
        [Description("The path of the directory to inspect.")] string path)
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
