#pragma warning disable KMEXP00

using Microsoft.KernelMemory.DataFormats;
using Microsoft.KernelMemory.DataFormats.Office;
using Microsoft.KernelMemory.DataFormats.Pdf;
using Microsoft.KernelMemory.Pipeline;

namespace Services;
/// <summary>
/// Загружает поддерживаемые форматы файлов и преобразует их содержимое во фрагменты RAG-документов.
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
    /// Загружает все поддерживаемые файлы по указанному пути и преобразует их во фрагменты RAG-документов.
    /// </summary>
    /// <param name="path">Файл или каталог для загрузки.</param>
    /// <param name="cancellationToken">Токен отмены для обработки файлов.</param>
    /// <returns>Загруженные фрагменты документов.</returns>
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
    /// Определяет один файл по пути или рекурсивно перечисляет все файлы в каталоге.
    /// </summary>
    /// <param name="path">Путь к файлу или каталогу.</param>
    /// <returns>Пути файлов для обработки.</returns>
    /// <exception cref="DirectoryNotFoundException">Возникает, когда <paramref name="path"/> не существует.</exception>
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
    /// <param name="file">Путь к загружаемому файлу.</param>
    /// <param name="pdfDecoder">Декодер PDF из Kernel Memory.</param>
    /// <param name="myWordExtractor">Извлекатель содержимого DOCX.</param>
    /// <param name="msExcelDecoder">Декодер XLSX.</param>
    /// <param name="msPowerPointDecoder">Декодер PPTX.</param>
    /// <param name="cancellationToken">Токен отмены для декодирования.</param>
    /// <returns>Фрагменты документа, извлечённые из файла.</returns>
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
                myWordExtractor.Decode(file);

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
    /// Преобразует непустые разделы, полученные от декодера, во фрагменты RAG-документа.
    /// </summary>
    /// <param name="result">Выходная коллекция.</param>
    /// <param name="file">Путь к исходному файлу.</param>
    /// <param name="content">Декодированное содержимое файла.</param>
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
    /// Создаёт нормализованный RAG-документ из исходного файла и его содержимого.
    /// </summary>
    /// <param name="file">Путь к исходному файлу.</param>
    /// <param name="content">Содержимое документа.</param>
    /// <returns>Новый RAG-документ со сгенерированным идентификатором.</returns>
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
