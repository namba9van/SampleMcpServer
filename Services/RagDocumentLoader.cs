#pragma warning disable KMEXP00

using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.DataFormats.Office;
using Microsoft.KernelMemory.DataFormats.Pdf;
using Microsoft.KernelMemory.Pipeline;

namespace Services;
/// <summary>
/// Загружает поддерживаемые файл форматы и преобразует their содержимое в RAG документ фрагменты.
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
    /// Загружает все поддерживаемые файлы под один путь и преобразует them в RAG документ фрагменты.
    /// </summary>
    /// <param name="path">файл или каталог для загрузки.</param>
    /// <param name="cancellationToken">отмена токен для файл обработка.</param>
    /// <returns>загруженный документ фрагменты.</returns>
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
    /// Определяет один файл путь или рекурсивно перечисляет все файлы под один каталог.
    /// </summary>
    /// <param name="path">файл или каталог путь.</param>
    /// <returns>файл paths для обрабатывать.</returns>
    /// <exception cref="DirectoryNotFoundException">возникает когда <paramref name="path"/> не существует.</exception>
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
    /// Декодирует файл в соответствии с поддерживаемым форматом и создаёт фрагменты документа.
    /// </summary>
    /// <param name="file">файл путь для загрузки.</param>
    /// <param name="pdfDecoder">Kernel Memory PDF декодер.</param>
    /// <param name="myWordExtractor">DOCX извлекатель.</param>
    /// <param name="msExcelDecoder">XLSX декодер.</param>
    /// <param name="msPowerPointDecoder">PPTX декодер.</param>
    /// <param name="cancellationToken">отмена токен для декодирования.</param>
    /// <returns>документ фрагменты извлечённый из файл.</returns>
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
    /// Преобразует декодер разделы в непустой RAG документ фрагменты.
    /// </summary>
    /// <param name="result">выходной коллекция.</param>
    /// <param name="file">исходный файл путь.</param>
    /// <param name="content">decoded файл содержимое.</param>
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
    /// Создаёт один нормализованный RAG документ из один исходный файл и его содержимое.
    /// </summary>
    /// <param name="file">исходный файл путь.</param>
    /// <param name="content">документ содержимое.</param>
    /// <returns>новый RAG документ с один сгенерированный идентификатор.</returns>
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
