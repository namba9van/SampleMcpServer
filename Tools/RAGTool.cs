#pragma warning disable KMEXP00

using ModelContextProtocol.Server;
using Services;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
/// <summary>
/// Предоставляет один MCP инструмент для поиска постоянный локальный RAG индекс.
/// </summary>
public partial class RAGTool
{
    /// <summary>
    /// Проверяет актуальность локального индекса RAG и выполняет поиск по векторному сходству.
    /// </summary>
    /// <param name="path">Файл или каталог с документами для индексации.</param>
    /// <param name="query">Поисковый запрос на естественном языке.</param>
    /// <param name="limit">Максимальное количество результатов для возврата.</param>
    /// <param name="threshold">минимальный балл сходства один результат должен reach.</param>
    /// <param name="ragIndexService">Управляемый приложением сервис постоянного индекса RAG.</param>
    /// <returns>Массив JSON с именами подходящих файлов, оценками и содержимым либо результатом ошибки.</returns>
    [McpServerTool]
    [Description("Выполняет поиск в постоянном локальном индексе RAG и возвращает наиболее релевантные фрагменты документов.")]

    public async Task<string> RagSearch(
        [Description("Файл или каталог, который необходимо включить в индекс RAG.")]
        string path,

        [Description("Поисковый запрос на естественном языке.")]
        string query,

        [Description("Максимальное количество результатов для возврата.")]
        int limit = 3,

        [Description("Минимальная оценка сходства, необходимая для включения результата.")]
        double threshold = 0.2,

        RagIndexService ragIndexService = null!
    )
    {
        var results =
            new List<RagResult>();

        try
        {
            Console.Error.WriteLine(
                "=== RAG SEARCH START ===");

            Console.Error.WriteLine(
                $"RAG path: {path}");

            Console.Error.WriteLine(
                $"RAG query: {query}");

            await ragIndexService
                .EnsureIndexUpToDateAsync(
                    path);

            var searchResults =
                await ragIndexService
                    .SearchAsync(
                        query,
                        limit,
                        threshold);

            Console.Error.WriteLine(
                $"Search results count: " +
                $"{searchResults.Count}");

            foreach (var item in searchResults)
            {
                results.Add(
                    new RagResult
                    {
                        FileName =
                            item.Record.FileName ??
                            string.Empty,

                        Score =
                            item.Score,

                        Content =
                            item.Record.Content ??
                            string.Empty
                    });
            }

            Console.Error.WriteLine(
                "=== RAG SEARCH END ===");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"RAG ERROR: {ex}");

            results.Add(
                new RagResult
                {
                    Content =
                        $"Error performing RAG search: " +
                        $"{ex.Message}"
                });
        }

        var options =
            new JsonSerializerOptions
            {
                WriteIndented = true,

                Encoder =
                    JavaScriptEncoder.Create(
                        System.Text.Unicode
                            .UnicodeRanges.All)
            };

        return JsonSerializer.Serialize(
            results,
            options);
    }
    /// <summary>
    /// Представляет один результат возвращённый по RAG поиск инструмент.
    /// </summary>
    /// <summary>
    /// Представляет один результат возвращённый по RAG поиск инструмент.
    /// </summary>
    public class RagResult
    {
        /// <summary>
        /// Получает или задаёт matched документ содержимое.
        /// </summary>
        public string Content { get; set; } =
            string.Empty;

        /// <summary>
        /// Получает или задаёт исходный файл имя.
        /// </summary>
        public string FileName { get; set; } =
            string.Empty;

        /// <summary>
        /// Получает или задаёт балл сходства возвращённый по вектор поиск.
        /// </summary>
        public double? Score { get; set; }
    }
}
