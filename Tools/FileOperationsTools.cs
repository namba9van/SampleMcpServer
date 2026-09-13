using System.ComponentModel;
using ModelContextProtocol.Server;

public sealed class FileOperationsTools
{
    [McpServerTool(Name = "file_read")]
    [Description("Reads a UTF-8 text file. This legacy tool accepts an explicit path; prefer governed agent filesystem tools for autonomous actions.")]
    public async Task<string> ReadFile(string filename, CancellationToken cancellationToken = default)
        => await File.ReadAllTextAsync(Path.GetFullPath(filename), cancellationToken);

    [McpServerTool(Name = "file_write")]
    [Description("Creates a UTF-8 text file without overwriting an existing file.")]
    public async Task<string> WriteFile(string filename, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filename);
        if (File.Exists(fullPath))
            return "Such a file already exists.";
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        return fullPath;
    }

    [McpServerTool(Name = "file_list")]
    [Description("Lists files and directories under a path.")]
    public string ListFiles(string path, bool recursive = false)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return string.Join('\n', Directory.EnumerateFileSystemEntries(fullPath, "*", option));
    }
}
