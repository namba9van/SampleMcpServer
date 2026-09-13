using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace Services;

public sealed class RagDocumentLoader
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".xml", ".csv", ".log", ".cs", ".js", ".ts", ".py", ".html", ".css", ".yml", ".yaml"
    };

    public async Task<IReadOnlyList<RagChunk>> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var files = File.Exists(path)
            ? new[] { Path.GetFullPath(path) }
            : Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToArray()
                : throw new FileNotFoundException($"File or directory was not found: {path}");

        var chunks = new List<RagChunk>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await ReadDocumentAsync(file, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (var chunk in Split(text, 1600, 220))
                chunks.Add(new RagChunk(file, chunk));
        }
        return chunks;
    }

    private static async Task<string> ReadDocumentAsync(string file, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(file);
        if (TextExtensions.Contains(ext))
            return await File.ReadAllTextAsync(file, cancellationToken);

        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var sb = new StringBuilder();
            using var document = PdfDocument.Open(file);
            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
                sb.AppendLine(document.GetPage(pageNumber).Text);
            return sb.ToString();
        }

        if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return ReadOpenXmlText(file, p => p.StartsWith("word/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (ext.Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            return ReadOpenXmlText(file, p => p.StartsWith("ppt/slides/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return ReadOpenXmlText(file, p => (p.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) || p.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase)) && p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        return string.Empty;
    }

    private static string ReadOpenXmlText(string file, Func<string, bool> includeEntry)
    {
        var sb = new StringBuilder();
        using var archive = ZipFile.OpenRead(file);
        foreach (var entry in archive.Entries.Where(e => includeEntry(e.FullName)))
        {
            using var stream = entry.Open();
            var doc = XDocument.Load(stream, LoadOptions.None);
            foreach (var node in doc.DescendantNodes().OfType<XText>())
            {
                if (!string.IsNullOrWhiteSpace(node.Value))
                    sb.Append(node.Value).Append(' ');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static IEnumerable<string> Split(string text, int maxChars, int overlap)
    {
        text = text.Replace("\r", " ").Replace("\n", " ");
        if (text.Length <= maxChars)
        {
            yield return text.Trim();
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(maxChars, text.Length - start);
            var chunk = text.Substring(start, length).Trim();
            if (chunk.Length > 0)
                yield return chunk;
            if (start + length >= text.Length)
                break;
            start += Math.Max(1, length - overlap);
        }
    }
}

public sealed record RagChunk(string FileName, string Content);
