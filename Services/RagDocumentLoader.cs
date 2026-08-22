#pragma warning disable KMEXP00

using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.DataFormats.Office;
using Microsoft.KernelMemory.DataFormats.Pdf;
using Microsoft.KernelMemory.Pipeline;

namespace Services;
/// <summary>
/// Loads supported file formats and converts their contents into RAG document chunks.
/// </summary>
public sealed class RagDocumentLoader
{
    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt",
            ".md",
            ".csv",
            ".log",
            ".json",
            ".xml",
            ".html",
            ".htm",
            ".yaml",
            ".yml",
            ".cs",
            ".js",
            ".ts",
            ".tsx",
            ".jsx",
            ".py",
            ".java",
            ".cpp",
            ".h",
            ".hpp",
            ".c",
            ".sql",
            ".ps1",
            ".psm1",
            ".bat",
            ".cmd",
            ".ini",
            ".cfg",
            ".conf",
            ".config"
        };
    /// <summary>
    /// Loads all supported files under a path and converts them into RAG document chunks.
    /// </summary>
    /// <param name="path">A file or directory to load.</param>
    /// <param name="cancellationToken">The cancellation token for file processing.</param>
    /// <returns>The loaded document chunks.</returns>
    public async Task<List<RagDocument>> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var files = GetFiles(path);

        var result = new List<RagDocument>();

        var pdfDecoder = new PdfDecoder();
        var myWordExtractor = new MyWordExtractor();
        var msExcelDecoder = new MsExcelDecoder();
        var msPowerPointDecoder = new MsPowerPointDecoder();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var documents =
                await LoadFileAsync(
                    file,
                    pdfDecoder,
                    myWordExtractor,
                    msExcelDecoder,
                    msPowerPointDecoder,
                    cancellationToken);

            result.AddRange(documents);
        }

        return result;
    }

    /// <summary>
    /// Resolves a file path or recursively enumerates all files under a directory.
    /// </summary>
    /// <param name="path">A file or directory path.</param>
    /// <returns>The file paths to process.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="path"/> does not exist.</exception>
    public IEnumerable<string> GetFiles(string path)
    {
        if (File.Exists(path))
        {
            yield return path;
            yield break;
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Путь не найден: {path}");
        }

        foreach (var file in Directory.GetFiles(
                     path,
                     "*",
                     SearchOption.AllDirectories))
        {
            yield return file;
        }
    }

    /// <summary>
    /// Decodes one file according to its supported format and creates document chunks.
    /// </summary>
    /// <param name="file">The file path to load.</param>
    /// <param name="pdfDecoder">The Kernel Memory PDF decoder.</param>
    /// <param name="myWordExtractor">The DOCX extractor.</param>
    /// <param name="msExcelDecoder">The XLSX decoder.</param>
    /// <param name="msPowerPointDecoder">The PPTX decoder.</param>
    /// <param name="cancellationToken">The cancellation token for decoding.</param>
    /// <returns>The document chunks extracted from the file.</returns>
    private async Task<List<RagDocument>> LoadFileAsync(
        string file,
        PdfDecoder pdfDecoder,
        MyWordExtractor myWordExtractor,
        MsExcelDecoder msExcelDecoder,
        MsPowerPointDecoder msPowerPointDecoder,
        CancellationToken cancellationToken)
    {
        var result = new List<RagDocument>();

        var extension =
            Path.GetExtension(file);

        if (string.Equals(
                extension,
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            var content =
                await pdfDecoder.DecodeAsync(file);

            AddChunks(
                result,
                file,
                content);
        }
        else if (string.Equals(
                     extension,
                     ".docx",
                     StringComparison.OrdinalIgnoreCase))
        {
            var sections =
                myWordExtractor.DecodeAsync(file);

            foreach (var section in sections)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(
                        section.Content))
                {
                    continue;
                }

                result.Add(
                    CreateDocument(
                        file,
                        section.Title +
                        ". " +
                        section.Content));
            }
        }
        else if (string.Equals(
                     extension,
                     ".xlsx",
                     StringComparison.OrdinalIgnoreCase))
        {
            var content =
                await msExcelDecoder.DecodeAsync(file);

            AddChunks(
                result,
                file,
                content);
        }
        else if (string.Equals(
                     extension,
                     ".pptx",
                     StringComparison.OrdinalIgnoreCase))
        {
            var content =
                await msPowerPointDecoder
                    .DecodeAsync(file);

            AddChunks(
                result,
                file,
                content);
        }
        else if (TextExtensions.Contains(extension))
        {
            var text =
                await File.ReadAllTextAsync(
                    file,
                    cancellationToken);

            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(
                    CreateDocument(
                        file,
                        text));
            }
        }
        else
        {
            if (FileUtils.IsPlainText(file))
            {
                var text =
                    await File.ReadAllTextAsync(
                        file,
                        cancellationToken);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(
                        CreateDocument(
                            file,
                            text));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Converts decoder sections into non-empty RAG document chunks.
    /// </summary>
    /// <param name="result">The output collection.</param>
    /// <param name="file">The source file path.</param>
    /// <param name="content">The decoded file content.</param>
    private static void AddChunks(
        List<RagDocument> result,
        string file,
        FileContent content)
    {
        foreach (Chunk section in content.Sections)
        {
            if (string.IsNullOrWhiteSpace(
                    section.Content))
            {
                continue;
            }

            result.Add(
                CreateDocument(
                    file,
                    section.Content));
        }
    }

    /// <summary>
    /// Creates a normalized RAG document from one source file and its content.
    /// </summary>
    /// <param name="file">The source file path.</param>
    /// <param name="content">The document content.</param>
    /// <returns>A new RAG document with a generated identifier.</returns>
    private static RagDocument CreateDocument(
        string file,
        string content)
    {
        return new RagDocument
        {
            Id = Guid.NewGuid().ToString(),
            FileName = file,
            Content = content.Replace("\n", ". ")
        };
    }
}
