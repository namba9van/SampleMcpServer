namespace Services;

public sealed class RagIndexService(EmbeddingService embeddings, RagDocumentLoader loader)
{
    public async Task<IReadOnlyList<RagSearchResult>> SearchAsync(
        string path,
        string query,
        int limit = 3,
        double threshold = 0.2,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        threshold = Math.Clamp(threshold, -1, 1);
        var queryVector = await embeddings.GenerateAsync(query, cancellationToken);
        var chunks = await loader.LoadAsync(path, cancellationToken);
        var results = new List<RagSearchResult>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var vector = await embeddings.GenerateAsync(chunk.Content, cancellationToken);
            var score = Cosine(queryVector, vector);
            if (score >= threshold)
                results.Add(new RagSearchResult(chunk.FileName, chunk.Content, score));
        }

        return results.OrderByDescending(r => r.Score).Take(limit).ToArray();
    }

    private static double Cosine(float[] left, float[] right)
    {
        if (left.Length == 0 || left.Length != right.Length)
            return 0;
        double dot = 0, a = 0, b = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            a += left[i] * left[i];
            b += right[i] * right[i];
        }
        return a <= 0 || b <= 0 ? 0 : dot / Math.Sqrt(a * b);
    }
}

public sealed record RagSearchResult(string FileName, string Content, double Score);
