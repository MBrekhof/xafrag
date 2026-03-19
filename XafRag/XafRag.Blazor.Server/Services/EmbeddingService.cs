using Microsoft.Extensions.AI;
using Pgvector;

namespace XafRag.Blazor.Server.Services;

public class EmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<EmbeddingService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    public async Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var result = await _embeddingGenerator.GenerateAsync([text], cancellationToken: ct);
        return new Vector(result[0].Vector);
    }

    public async Task<List<Vector>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        _logger.LogInformation("Generating embeddings for {Count} chunks", texts.Count);

        var result = await _embeddingGenerator.GenerateAsync(texts, cancellationToken: ct);
        return result.Select(e => new Vector(e.Vector)).ToList();
    }
}
